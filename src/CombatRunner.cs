using System;
using System.Diagnostics;
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

    internal static class CombatRunner
    {
        internal static CombatOutcome Engage(WorldSession world, CombatSettings settings, CancellationToken token,
            int timeoutMs = 120000, int waitForCombatMs = 0, bool seekTargets = false, int approachMs = 0,
            Action onCombatStarted = null, bool timeoutFromEntry = false, Func<long, bool> captureDuringCombat = null)
        {
            bool cloud = settings.Source == GameSource.Cloud || settings.Source == GameSource.ManagedCloud;
            var planner = new BattlePlanner(settings, cloud, seekTargets,
                attackVisibleEnemies: seekTargets || settings.AutoTarget,
                challengeObservation: world.ChallengeObservation, challengeClock: () => world.Now);
            var watch = Stopwatch.StartNew();
            var clearCheck = new CombatClearCheck();
            int searchMs = waitForCombatMs > 0 ? waitForCombatMs : timeoutMs;
            RunLog.Info("任务战斗", string.Format("准备战斗，主动锁敌={0}，接近={1} ms，等待锁敌={2} ms，战斗时限={3} ms",
                seekTargets, approachMs, searchMs, timeoutMs));
            long deadline = approachMs + (long)searchMs;
            long nextProbe = 0;
            bool approaching = approachMs > 0, walking = false, started = false;
            long unlockedSince = -1;
            bool triedForeground = false;
            int inputRoute = world.InputRouteVersion;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    long now = watch.ElapsedMilliseconds;
                    if (timeoutFromEntry && now >= timeoutMs)
                        return ReportOutcome(CombatOutcome.Timeout, watch);
                    if (!world.Tick(false, planner, now))
                    {
                        clearCheck.Observe(false, false, now);
                        if (now >= deadline)
                            return ReportOutcome(started ? CombatOutcome.Timeout :
                                seekTargets ? CombatOutcome.Unverified : CombatOutcome.NoCombat, watch);
                        if (walking)
                        {
                            world.KeyUp('W');
                            walking = false;
                        }
                        planner.Invalidate(now);
                        world.BattleInput.ReleaseAll();
                        token.WaitHandle.WaitOne(80);
                        continue;
                    }

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
                    if (playable && (started || enemyVisible) && captureDuringCombat != null && captureDuringCombat(now))
                        return ReportOutcome(CombatOutcome.Captured, watch);
                    if (now >= deadline)
                    {
                        if (started)
                            return ReportOutcome(CombatOutcome.Timeout, watch);
                        if (seekTargets && (clearCheck.SawEnemy || !clearCheck.IsQuiet(now)))
                            return ReportOutcome(CombatOutcome.Unverified, watch);
                        return ReportOutcome(CombatOutcome.NoCombat, watch);
                    }
                    bool targetVisible = hud.Locked || cloud && hud.EnemyHealth;
                    if (seekTargets && hud.ChallengePrompt && !planner.Fighting)
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

                    BattleAction action = planner.Observe(hud, now);
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
                        deadline = timeoutFromEntry ? timeoutMs : now + timeoutMs;
                        RunLog.Info("任务战斗", "已开始攻击，耗时 " + now + " ms，依据=" +
                            (hud.Locked ? "锁定目标" : hud.EnemyHealth ? "可见敌人血条" : "挑战状态"));
                        if (onCombatStarted != null)
                            onCombatStarted();
                    }

                    // The task explicitly seeks nearby enemies even before any health bar appears.
                    if (seekTargets && !started && hud.Valid && !targetVisible && !hud.ChallengePrompt)
                    {
                        if (action.Kind == ActionKind.Wait && now >= nextProbe)
                            action = BattleAction.Target();
                        if (action.Kind == ActionKind.Target && now < nextProbe)
                            action = default(BattleAction);
                    }

                    if (timeoutFromEntry && watch.ElapsedMilliseconds >= timeoutMs)
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

                    if (started && hud.Valid && !planner.Fighting && !enemyVisible &&
                        (!seekTargets || clearCheck.IsQuiet(now)))
                        return ReportOutcome(CombatOutcome.Finished, watch);
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
