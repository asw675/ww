using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace TideCombat
{
    internal enum CombatOutcome { Finished, NoCombat, Dead, Timeout, Unverified, Captured }

    internal sealed class CombatClearCheck
    {
        private long quietSince = -1;
        private int quietSamples;
        internal bool SawEnemy { get; private set; }

        internal void Observe(bool playable, bool enemyVisible, long now)
        {
            if (!playable || enemyVisible)
            {
                SawEnemy |= playable && enemyVisible;
                quietSince = -1;
                quietSamples = 0;
                return;
            }
            if (quietSince < 0)
                quietSince = now;
            quietSamples++;
        }

        internal bool IsQuiet(long now)
        {
            return quietSamples >= 3 && now - quietSince >= 1700;
        }
    }

    // The reference performs character-specific cleanup and optional healer
    // handoff before waiting for the team HUD to settle. Keep a separate bounded
    // post-fight window rather than treating the first unlocked frame as success.
    internal sealed class CombatPostFightCheck
    {
        private long startAt = -1, cleanupAt = -1, readyAt = -1;
        internal bool Active { get { return startAt >= 0; } }

        internal bool Observe(bool ending, bool recovering, bool ready, long now)
        {
            if (!ending)
            {
                startAt = cleanupAt = readyAt = -1;
                return false;
            }
            if (startAt < 0) startAt = now;
            if (recovering)
            {
                cleanupAt = readyAt = -1;
                return false;
            }
            if (cleanupAt < 0) cleanupAt = now;
            if (!ready) readyAt = -1;
            else if (readyAt < 0) readyAt = now;
            return readyAt >= 0 && now - readyAt >= 500;
        }

        internal bool Expired(long now)
        {
            return Active && (now - startAt >= 22000 || cleanupAt >= 0 && now - cleanupAt >= 10000);
        }
    }

    internal static class CombatRunner
    {
        internal static CombatOutcome Engage(WorldSession world, CombatSettings settings, CancellationToken token,
            int timeoutMs = 120000, int waitForCombatMs = 0, bool seekTargets = false, int approachMs = 0,
            Action onCombatStarted = null, bool timeoutFromEntry = false, Func<long, bool> captureDuringCombat = null,
            bool? openWorldAutoCombat = null)
        {
            bool cloud = settings.Source == GameSource.Cloud || settings.Source == GameSource.ManagedCloud;
            var planner = CreateTaskPlanner(settings, cloud, world.ChallengeObservation, () => world.Now, openWorldAutoCombat);
            var watch = Stopwatch.StartNew();
            var clearCheck = new CombatClearCheck();
            var postFight = new CombatPostFightCheck();
            bool noOverallTimeout = timeoutMs == 0 && waitForCombatMs > 0 && !timeoutFromEntry;
            int searchMs = waitForCombatMs > 0 ? waitForCombatMs : timeoutMs;
            RunLog.Info("任务战斗", string.Format("准备战斗，主动锁敌={0}，接近={1} ms，等待锁敌={2} ms，战斗时限={3} ms",
                seekTargets, approachMs, searchMs, timeoutMs));
            long deadline = approachMs + (long)searchMs;
            long nextProbe = 0;
            bool approaching = approachMs > 0, walking = false, started = false;
            long unlockedSince = -1;
            bool triedForeground = false;
            long invalidCaptureSince = -1;
            int inputRoute = world.InputRouteVersion;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    long now = watch.ElapsedMilliseconds;
                    if (timeoutFromEntry && !postFight.Active && now >= timeoutMs)
                        return ReportOutcome(CombatOutcome.Timeout, watch);
                    if (!world.Tick(false, planner, now))
                    {
                        if (invalidCaptureSince < 0) invalidCaptureSince = now;
                        clearCheck.Observe(false, false, now);
                        if (postFight.Active)
                        {
                            postFight.Observe(true, planner.RecoveringAfterBattle, false, now);
                            if (postFight.Expired(now)) return ReportOutcome(CombatOutcome.Unverified, watch);
                        }
                        else if (now >= deadline)
                            return ReportOutcome(started ? CombatOutcome.Timeout :
                                seekTargets ? CombatOutcome.Unverified : CombatOutcome.NoCombat, watch);
                        else if (noOverallTimeout && started && now - invalidCaptureSince >= 10000)
                            return ReportOutcome(CombatOutcome.Unverified, watch);
                        if (walking)
                        {
                            world.KeyUp('W');
                            walking = false;
                        }
                        if (!postFight.Active) planner.Invalidate(now);
                        else planner.HoldsCanceled(now);
                        world.BattleInput.ReleaseAll();
                        token.WaitHandle.WaitOne(80);
                        continue;
                    }

                    invalidCaptureSince = -1;

                    HudSnapshot hud = world.Hud ?? new HudSnapshot();
                    if (inputRoute != world.InputRouteVersion)
                    {
                        inputRoute = world.InputRouteVersion;
                        walking = false;
                        planner.HoldsCanceled(now);
                    }
                    if (hud.Dead || world.Ui != null && world.Ui.Death)
                        return ReportOutcome(CombatOutcome.Dead, watch);

                    FarmUi ui = world.Ui;
                    bool playable = hud.Valid && ui != null && !ui.Claim && !ui.Challenge &&
                        ui.ConfirmBox.IsEmpty && !hud.ChallengePrompt;
                    bool enemyVisible = hud.Locked || hud.EnemyHealth;
                    clearCheck.Observe(playable, enemyVisible, now);
                    if (TryCaptureDuringCombat(playable, started, captureDuringCombat, now))
                        return ReportOutcome(CombatOutcome.Captured, watch);
                    if (!postFight.Active && now >= deadline)
                    {
                        if (started)
                            return ReportOutcome(CombatOutcome.Timeout, watch);
                        if (seekTargets && (clearCheck.SawEnemy || !clearCheck.IsQuiet(now)))
                            return ReportOutcome(CombatOutcome.Unverified, watch);
                        return ReportOutcome(CombatOutcome.NoCombat, watch);
                    }
                    bool targetVisible = hud.Locked || cloud && hud.EnemyHealth;
                    if (seekTargets && hud.ChallengePrompt && !planner.Fighting && !postFight.Active)
                    {
                        world.ChallengeObservation.Observe(hud, world.Now, consume: false);
                        token.WaitHandle.WaitOne(50);
                        continue;
                    }
                    if (seekTargets && hud.Valid && !playable)
                        hud = new HudSnapshot { Detail = "等待可战斗画面" };

                    if (seekTargets && !triedForeground && settings.Source == GameSource.AutoLocal &&
                        world.Window.Mode == GameSource.Background && playable && hud.EnemyHealth && !hud.Locked)
                    {
                        if (unlockedSince < 0)
                            unlockedSince = now;
                        if (now - unlockedSince >= 2500)
                        {
                            triedForeground = true;
                            RunLog.Warn("任务战斗", "后台持续发现敌人但未锁定，检查能否切到游戏前台");
                            if (world.TryRecoverForegroundCapture())
                                continue;
                        }
                    }
                    else
                        unlockedSince = -1;

                    BattleAction action = postFight.Active ? planner.ObserveTaskAfterBattle(hud, now) :
                        planner.Observe(hud, now, () => ReadAbyssTimer(world.Frame, world.Ocr));
                    if (approaching && (now >= approachMs || targetVisible || !seekTargets && planner.Fighting))
                    {
                        approaching = false;
                        if (!started)
                            deadline = now + searchMs;
                        RunLog.Info("任务战斗", string.Format("结束接近，耗时={0} ms，已锁定={1}，可见血条={2}，已开战={3}",
                            now, hud.Locked, hud.EnemyHealth, planner.Fighting));
                    }
                    if (walking && (!approaching || !hud.Valid))
                    {
                        world.KeyUp('W');
                        walking = false;
                    }
                    if (approaching && hud.Valid && !walking)
                        walking = world.KeyDown('W');

                    if (!started && planner.Fighting)
                    {
                        started = true;
                        deadline = noOverallTimeout ? long.MaxValue : timeoutFromEntry ? timeoutMs : now + timeoutMs;
                        RunLog.Info("任务战斗", "已开始攻击，耗时 " + now + " ms，依据=" +
                            (hud.Locked ? "锁定目标" : hud.EnemyHealth ? "可见敌人血条" : "挑战状态"));
                        if (onCombatStarted != null)
                            onCombatStarted();
                    }

                    action = AddBlindTargetProbe(action, hud, seekTargets && !started && now >= nextProbe);

                    if (timeoutFromEntry && !postFight.Active && watch.ElapsedMilliseconds >= timeoutMs)
                        return ReportOutcome(CombatOutcome.Timeout, watch);
                    if (planner.TakeReleaseRequest())
                        world.BattleInput.ReleaseAll();
                    if (!hud.Valid && !planner.KeepAnimationHold(hud, now) && world.BattleInput.ReleaseAll())
                        planner.HoldsCanceled(now);
                    if (action.AttackBefore)
                        world.BattleInput.Send(BattleAction.Attack(), token);
                    if (action.Kind != ActionKind.Wait && world.BattleInput.Send(action, token))
                    {
                        long sentAt = watch.ElapsedMilliseconds;
                        planner.Sent(action, sentAt);
                        if (action.Kind == ActionKind.Target)
                            nextProbe = sentAt + 500;
                        if (action.FollowWithAttack)
                            world.BattleInput.Send(BattleAction.Attack(), token);
                    }

                    // combat_once returns the result of entering combat once its
                    // loop exits and cleanup settles. The caller verifies loot or
                    // completion; no additional arbitrary quiet-sample delay.
                    if (postFight.Observe(started && !planner.Fighting,
                        planner.RecoveringAfterBattle, hud.Valid, now))
                        return ReportOutcome(planner.CombatAborted || enemyVisible ? CombatOutcome.Unverified : CombatOutcome.Finished, watch);
                    if (postFight.Expired(now))
                        return ReportOutcome(CombatOutcome.Unverified, watch);
                    token.WaitHandle.WaitOne(50);
                }
                RunLog.Info("任务战斗", "已取消，耗时 " + watch.ElapsedMilliseconds + " ms");
                return CombatOutcome.Timeout;
            }
            finally
            {
                try
                {
                    if (walking)
                        world.KeyUp('W');
                }
                finally { world.BattleInput.ReleaseAll(); }
            }
        }

        internal static BattlePlanner CreateTaskPlanner(CombatSettings settings, bool cloud,
            ChallengeStartObservation challengeObservation = null, Func<long> challengeClock = null,
            bool? openWorldAutoCombat = null)
        {
            // An explicit account setting retains the existing world-only preference.
            bool? useLiberation = settings.HasAccountLiberationOverride ? (bool?)null : true;
            return new BattlePlanner(settings, cloud, targetForTask: true, useLiberation: useLiberation,
                challengeObservation: challengeObservation, challengeClock: challengeClock,
                openWorldAutoCombat: openWorldAutoCombat);
        }

        internal static BattleAction AddBlindTargetProbe(BattleAction action, HudSnapshot hud, bool probeDue)
        {
            if (probeDue && action.Kind == ActionKind.Wait && hud.Valid && !hud.Locked && !hud.EnemyHealth && !hud.ChallengePrompt)
                return BattleAction.Target();
            return action;
        }

        internal static double ReadAbyssTimer(PixelFrame frame, OcrReader ocr)
        {
            if (ocr == null) return double.NaN;
            try
            {
                var viewport = new Rectangle(0, 0, frame.Width, frame.Height);
                Rectangle area = FramePatch.ScaleCentered(viewport, 3840, 2160, 1820, 266, 2100, 340);
                foreach (OcrLine line in ocr.Read(frame, area))
                {
                    string label = (line.Text ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKC).Trim();
                    double seconds = HudGlyphs.ParseChallengeClock(label);
                    if (!double.IsNaN(seconds)) return seconds;
                }
            }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException error)
            {
                RunLog.Error("任务战斗", "深塔计时识别失败", error);
            }
            return double.NaN;
        }

        internal static bool TryCaptureDuringCombat(bool playable, bool started, Func<long, bool> capture, long now)
        {
            return playable && started && capture != null && capture(now);
        }

        private static CombatOutcome ReportOutcome(CombatOutcome outcome, Stopwatch watch)
        {
            string message = "战斗结果=" + outcome + "，耗时 " + watch.ElapsedMilliseconds + " ms";
            if (outcome == CombatOutcome.Timeout || outcome == CombatOutcome.Dead || outcome == CombatOutcome.Unverified)
                RunLog.Warn("任务战斗", message);
            else
                RunLog.Info("任务战斗", message);
            return outcome;
        }
    }
}
