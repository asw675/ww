using System;
using System.Drawing;

namespace TideCombat
{
    public enum FarmTeleportMode { Off, BossChallenge, WeeklyChallenge, SpecialNightmare }
    public enum FarmPickupMode { Walk, Circle, Yolo }
    public enum FarmBoss
    {
        Other, Hyvatia, FallacyOfNoReturn, SentryConstruct, Lorelei, LionessOfGlory,
        NightmareHecate, Fenrico, NamelessExplorer
    }

    public sealed class FarmOptions
    {
        public bool Teleport { get; set; }
        public FarmTeleportMode TeleportMode { get; set; }
        public int BossIndex { get; set; }
        public int BossLevel { get; set; }
        public int RepeatCount { get; set; }
        public int WaitSeconds { get; set; }
        public bool SwitchHealer { get; set; }
        public FarmPickupMode Pickup { get; set; }
        public FarmBoss Boss { get; set; }
        public bool ExitAfterTask { get; set; }

        public static FarmOptions Defaults()
        {
            return new FarmOptions
            {
                Teleport = false, TeleportMode = FarmTeleportMode.BossChallenge, BossIndex = 1,
                BossLevel = 80, RepeatCount = 10000, WaitSeconds = 0, SwitchHealer = true,
                Pickup = FarmPickupMode.Walk, Boss = FarmBoss.Other
            };
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(FarmBoss), Boss) ||
                !Enum.IsDefined(typeof(FarmTeleportMode), TeleportMode) ||
                !Enum.IsDefined(typeof(FarmPickupMode), Pickup) ||
                BossIndex < 1 || BossIndex > 20 || RepeatCount < 1 || RepeatCount > 100000 ||
                WaitSeconds < 0 || WaitSeconds > 60)
                throw new System.IO.InvalidDataException("刷取配置无效。");
            BossLevel = Math.Max(50, Math.Min(90, BossLevel));
        }
    }

    internal enum FarmRequest { None, Click, Key, KeyDown, KeyUp, Middle, Scroll, Battle, Text }

    internal struct FarmDecision
    {
        internal FarmRequest Request;
        internal Point Point;
        internal char Key;
        internal ushort VirtualKey;
        internal BattleAction Battle;
        internal string Text;

        internal static FarmDecision None() { return new FarmDecision { Request = FarmRequest.None }; }
    }

    internal sealed class FarmFrame
    {
        internal readonly HudSnapshot Hud;
        internal readonly FarmUi Ui;
        internal readonly Size FrameSize;
        internal readonly Rectangle Bounds;
        internal readonly bool HudValid;
        internal readonly System.Collections.Generic.List<EchoBox> Echoes;
        internal readonly double NavTurn;
        internal readonly System.Collections.Generic.List<OcrLine> LevelLines;
        internal readonly bool AbsorbText;
        internal readonly string DialogText;
        internal readonly string BossName;

        internal FarmFrame(HudSnapshot hud, bool hudValid, FarmUi ui, Size frameSize, Rectangle bounds,
            System.Collections.Generic.List<EchoBox> echoes = null, double navTurn = double.NaN,
            System.Collections.Generic.List<OcrLine> levelLines = null, bool absorbText = false,
            string dialogText = null, string bossName = null)
        {
            BossName = bossName;
            NavTurn = navTurn;
            LevelLines = levelLines;
            AbsorbText = absorbText;
            DialogText = dialogText;
            Hud = hud;
            HudValid = hudValid;
            Ui = ui;
            FrameSize = frameSize;
            Bounds = bounds;
            Echoes = echoes;
        }
    }

    internal sealed class Farm4C
    {
        private const int ApproachTimeout = 20000;
        private const int CombatLostMs = 2500;
        private const int EnterTimeout = 8000;

        private enum Step { WaitGame, BossScript, Approach, Enter, Fight, Loot, Claim, Revive, Teleport, HealTeleport, MapTeleport, AimTeleport, Night, Done, Failed }
        private enum TeleportStep { OpenBook, PickCategory, PickKind, PickBoss, Travel, Confirm, WaitWorld }

        private readonly CombatSettings settings;
        private readonly FarmOptions options;
        private readonly BattlePlanner planner;
        private Step step = Step.WaitGame;
        private TeleportStep teleportStep = TeleportStep.OpenBook;
        private long stepAt, nextAction, lastCombatAt, lastCameraAt;
        private bool forwardHeld, combatSeen, entered;
        private int teleportAttempts, enterClicks, kills, echoes, retries, circleStep, scrollClicks, nightStep,
            mapStep, aimStep, bossStep, healStep;
        private bool healUsed;
        private string aimBoss;
        private long nextCircleAt, lastNightChange, nightStepAt, nextPopupAt, realmExitAt, lastScrollAt, nextPickupAt;
        private int circleDuration = 800;
        private bool backedUp;
        private int popupClicks;
        private bool absorbSeen;
        private readonly int combatWaitMs;
        private readonly bool bypassEndWait, treatAsNotInRealm;
        private string detail = "等待游戏窗口";

        internal Farm4C(CombatSettings settings, FarmOptions options, bool cloud = false)
        {
            this.settings = settings;
            this.options = options;
            planner = new BattlePlanner(settings, cloud);
            combatWaitMs = options.WaitSeconds > 0 ? options.WaitSeconds * 1000 : DefaultWait(options.Boss);
            bypassEndWait = options.Boss == FarmBoss.Fenrico || options.Boss == FarmBoss.FallacyOfNoReturn ||
                options.Boss == FarmBoss.NamelessExplorer;
            treatAsNotInRealm = options.Boss == FarmBoss.NightmareHecate;
        }

        private static int DefaultWait(FarmBoss boss)
        {
            switch (boss)
            {
                case FarmBoss.Hyvatia: return 7000;
                case FarmBoss.SentryConstruct:
                case FarmBoss.LionessOfGlory:
                case FarmBoss.FallacyOfNoReturn: return 5000;
                default: return 0;
            }
        }

        internal string Status { get { return detail; } }
        internal string Failure { get; private set; }
        internal bool Finished { get { return step == Step.Done || step == Step.Failed; } }
        internal int Kills { get { return kills; } }
        internal int Echoes { get { return echoes; } }
        internal double Hours
        {
            get { return Math.Max((DateTime.UtcNow - startedAt).TotalHours, 1.0 / 3600); }
        }

        private DateTime startedAt = DateTime.UtcNow;

        internal BattlePlanner Planner { get { return planner; } }
        internal bool ForwardHeld { get { return forwardHeld; } }
        internal bool WantsLevelScan { get { return step == Step.Enter && enterClicks == 0; } }
        internal bool NeedsMapMarker { get { return step == Step.MapTeleport && mapStep == 1; } }
        internal bool WantsPickupScan
        {
            get { return (step == Step.Loot || step == Step.Claim) && !absorbSeen; }
        }

        internal FarmDecision Observe(FarmFrame frame, long now)
        {
            detail = Describe();
            if (step == Step.Done || step == Step.Failed)
                return Idle();
            FarmUi ui = frame.Ui ?? new FarmUi();
            HudSnapshot hud = frame.HudValid ? frame.Hud : null;
            bool realm = InRealm(ui);
            planner.SwitchTimeoutMs = realm ? 5000 : 3000;
            planner.LostCombatTimeoutMs = realm ? 3000 : 1200;
            if (!string.IsNullOrEmpty(frame.DialogText) &&
                (frame.DialogText.Contains("断") || frame.DialogText.Contains("云服务") ||
                 frame.DialogText.Contains("重连")) && now >= nextPopupAt && step != Step.Night)
            {
                nextPopupAt = now + 3000;
                return Click(At(frame, .50, .62), now);
            }
            if (ui.MonthlyCard && now >= nextPopupAt && step != Step.Night)
            {
                nextPopupAt = now + 2500;
                popupClicks++;
                return popupClicks <= 2 ? Click(At(frame, .50, .89), now) : Key(0x1B, now);
            }
            if (!ui.MonthlyCard && step != Step.Night)
                popupClicks = 0;

            if (step != Step.Night && options.Boss == FarmBoss.Lorelei && !ui.Death &&
                now - lastNightChange > 660000 && hud == null && step != Step.Teleport)
            {
                lastNightChange = now;
                nightStep = 0;
                nightStepAt = now;
                step = Step.Night;
                ReleaseForward(now);
                return Idle();
            }
            if (aimBoss == null && !string.IsNullOrEmpty(frame.BossName))
                aimBoss = MatchBoss(frame.BossName);
            if (ui.Death && step != Step.Revive && step != Step.Teleport && step != Step.Night &&
                step != Step.AimTeleport && step != Step.MapTeleport)
            {
                step = Step.Revive;
                stepAt = now;
                nextAction = now;
                ReleaseForward(now);
                return Idle();
            }

            switch (step)
            {
                case Step.WaitGame: return WaitGame(hud, now);
                case Step.BossScript: return BossScript(frame, hud, ui, now);
                case Step.Approach: return Approach(frame, hud, ui, now);
                case Step.Enter: return Enter(frame, ui, now);
                case Step.Fight: return Fight(frame, hud, ui, now);
                case Step.Loot: return Loot(frame, hud, ui, now);
                case Step.Claim: return Claim(frame, hud, ui, now);
                case Step.Revive: return Revive(frame, ui, now);
                case Step.Teleport: return Teleport(frame, ui, now);
                case Step.HealTeleport: return HealTeleport(frame, ui, now);
                case Step.Night: return Night(frame, now);
                case Step.MapTeleport: return MapTeleport(frame, ui, now);
                case Step.AimTeleport: return AimTeleport(frame, ui, now);
                default: return Idle();
            }
        }

        internal void Sent(FarmDecision decision, long now)
        {
            if (decision.Request == FarmRequest.Battle && decision.Battle.Kind != ActionKind.Wait)
                planner.Sent(decision.Battle, now);
        }

        internal void ResetToApproach(long now)
        {
            backedUp = false;
            circleStep = 0;
            circleDuration = 800;
            absorbSeen = false;
            step = Step.Approach;
            stepAt = now;
            nextAction = now;
            combatSeen = false;
            entered = false;
            ReleaseForward(now);
        }

        private FarmDecision WaitGame(HudSnapshot hud, long now)
        {
            detail = "等待进入游戏世界";
            if (hud != null && hud.Valid)
            {
                step = options.Teleport ? Step.Teleport : HasBossScript ? Step.BossScript : Step.Approach;
                bossStep = 0;
                stepAt = now;
                nextAction = now;
                if (options.Teleport)
                {
                    teleportStep = TeleportStep.OpenBook;
                    teleportAttempts = 0;
                }
            }
            return Idle();
        }

        private bool HasBossScript
        {
            get
            {
                return options.Boss == FarmBoss.Fenrico || options.Boss == FarmBoss.FallacyOfNoReturn ||
                    options.Boss == FarmBoss.NamelessExplorer;
            }
        }

        private FarmDecision BossScript(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "Boss 特化流程";
            if (ui.Death) { step = Step.Revive; stepAt = now; return Idle(); }
            if (hud != null && (hud.EnemyHealth || hud.Locked))
            {
                step = Step.Fight;
                stepAt = now;
                lastCombatAt = now;
                combatSeen = true;
                return Idle();
            }
            if (now < nextAction)
                return Idle();
            if (options.Boss == FarmBoss.Fenrico)
            {
                if (bossStep == 0)
                {
                    if (ui.FPrompt) { nextAction = now + 1000; return Key('F', now); }
                    bossStep = 1;
                    nextAction = now + 500;
                    return Idle();
                }
                if (bossStep == 1)
                {
                    bossStep = 2;
                    nextAction = now + 2500;
                    return TeleportOrMap(frame, ui, now);
                }
                step = Step.Approach;
                stepAt = now;
                nextAction = now;
                return Idle();
            }
            if (options.Boss == FarmBoss.FallacyOfNoReturn)
            {
                if (bossStep == 0)
                {
                    if (ui.FPrompt) { step = Step.Enter; stepAt = now; nextAction = now; return Key('F', now); }
                    bossStep = 1;
                    nextAction = now + 2500;
                    return TeleportOrMap(frame, ui, now);
                }
                if (bossStep == 1) { bossStep = 2; nextAction = now + 400; return Key('D', now); }
                step = Step.Approach;
                stepAt = now;
                nextAction = now;
                return Key('W', now);
            }
            if (bossStep == 0)
            {
                bossStep = 1;
                nextAction = now + 8000;
                forwardHeld = true;
                return KeyDown('W', now);
            }
            if (bossStep == 1)
            {
                bossStep = 2;
                nextAction = now + 2500;
                return TeleportOrMap(frame, ui, now);
            }
            step = Step.Approach;
            stepAt = now;
            nextAction = now;
            return Idle();
        }

        private FarmDecision TeleportOrMap(FarmFrame frame, FarmUi ui, long now)
        {
            if (aimBoss != null)
            {
                aimStep = 0;
                step = Step.AimTeleport;
            }
            else
            {
                mapStep = 0;
                step = Step.MapTeleport;
            }
            stepAt = now;
            nextAction = now;
            return Idle();
        }

        private FarmDecision Approach(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            if (now - stepAt > ApproachTimeout)
            {
                if (options.Teleport && teleportAttempts < 3)
                {
                    step = Step.Teleport;
                    teleportStep = TeleportStep.OpenBook;
                    stepAt = now;
                    teleportAttempts++;
                    return Idle();
                }
                bool mapFirst = !options.Teleport || options.Boss == FarmBoss.FallacyOfNoReturn ||
                    options.Boss == FarmBoss.Fenrico || options.Boss == FarmBoss.NamelessExplorer;
                if (mapFirst && teleportAttempts < 2)
                {
                    teleportAttempts++;
                    stepAt = now;
                    nextAction = now;
                    if (aimBoss != null)
                    {
                        aimStep = 0;
                        step = Step.AimTeleport;
                    }
                    else
                    {
                        mapStep = 0;
                        step = Step.MapTeleport;
                    }
                    return Idle();
                }
                return Fail("20 秒内没有找到可交互目标或敌人。请确认已站在 Boss 前。");
            }
            if (ui.FPrompt)
            {
                PressForwardRelease(now);
                step = Step.Enter;
                stepAt = now;
                nextAction = now;
                return Key('F', now);
            }
            if (hud != null && (hud.EnemyHealth || hud.Locked))
            {
                step = Step.Fight;
                stepAt = now;
                lastCombatAt = now;
                combatSeen = true;
                return Idle();
            }
            if (now >= nextAction)
            {
                nextAction = now + 180;
                if (ui.Treasure)
                    return Steer(ui, frame, now);
                if (options.Pickup == FarmPickupMode.Circle && now >= nextCircleAt)
                {
                    if (circleStep > 0 && circleStep % 2 == 0 && circleDuration < 4000)
                        circleDuration += 800;
                    nextCircleAt = now + circleDuration;
                    char direction = "AWDS"[circleStep++ % 4];
                    return Key(direction, now);
                }
                if (!double.IsNaN(frame.NavTurn) && !options.Teleport)
                    return Navigate(frame.NavTurn, now);
                if (now - lastCameraAt > 2600)
                {
                    lastCameraAt = now;
                    return Middle(now);
                }
                if (!forwardHeld)
                {
                    forwardHeld = true;
                    return KeyDown('W', now);
                }
            }
            return Idle();
        }

        private FarmDecision Enter(FarmFrame frame, FarmUi ui, long now)
        {
            long elapsed = now - stepAt;
            detail = "进入挑战";
            if (elapsed > EnterTimeout + 8000)
                return Fail("挑战界面没有出现，无法进入。");
            if (ui.Challenge || ui.TeamStart)
            {
                if (elapsed >= 300 && enterClicks == 0)
                {
                    enterClicks = 1;
                    Point level = LevelPoint(frame);
                    return Click(level, now);
                }
                if (elapsed >= 900 && enterClicks == 1)
                {
                    enterClicks = 2;
                    return Click(new Point((int)(frame.FrameSize.Width * .880),
                        (int)(frame.FrameSize.Height * .911)), now);
                }
                if (elapsed >= 1500 && enterClicks == 2)
                {
                    enterClicks = 3;
                    if (ui.SkipDialog)
                        return Click(new Point((int)(frame.FrameSize.Width * .49),
                            (int)(frame.FrameSize.Height * .55)), now);
                }
                if (elapsed >= 2100 && enterClicks == 3)
                {
                    enterClicks = 4;
                    return Click(new Point((int)(frame.FrameSize.Width * .908),
                        (int)(frame.FrameSize.Height * .919)), now);
                }
                if (elapsed >= 3000 && !entered)
                {
                    entered = true;
                    step = Step.Fight;
                    stepAt = now;
                    lastCombatAt = now;
                    combatSeen = false;
                }
                return Idle();
            }
            if (elapsed >= 3000 && (ui.FPrompt || ui.Treasure))
                return Idle();
            if (elapsed >= 6000)
            {
                step = Step.Fight;
                stepAt = now;
                lastCombatAt = now;
                combatSeen = false;
            }
            return Idle();
        }

        private FarmDecision Fight(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            if (!InRealm(ui) && ui.FPrompt && frame.AbsorbText && now >= nextPickupAt)
            {
                nextPickupAt = now + 1200;
                echoes++;
                return Key('F', now);
            }
            if (combatSeen && frame.Echoes != null && frame.Echoes.Count > 0)
            {
                Kill();
                step = Step.Loot;
                stepAt = now;
                nextAction = now;
                return Idle();
            }
            if (hud != null && (hud.EnemyHealth || hud.Locked))
            {
                lastCombatAt = now;
                combatSeen = true;
                detail = planner.Fighting ? "战斗中" : "锁定目标";
                BattleAction action = planner.Observe(hud, now);
                if (planner.TakeReleaseRequest())
                    return Idle();
                return Battle(action, now);
            }
            if (now - stepAt < combatWaitMs)
                return Idle();
            if (ui.Claim)
            {
                Kill();
                step = Step.Claim;
                stepAt = now;
                nextAction = now + 400;
                return Idle();
            }
            if (combatSeen && now - lastCombatAt > CombatLostMs)
            {
                Kill();
                step = Step.Loot;
                stepAt = now;
                nextAction = now;
                return Idle();
            }
            if (!combatSeen && now - stepAt > 15000)
                return Fail("没有检测到战斗。请确认已在 Boss 面前并开始挑战。");
            return Idle();
        }

        private FarmDecision Loot(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "拾取与重开";
            if (ui.FPrompt)
            {
                if (frame.AbsorbText && !absorbSeen)
                {
                    absorbSeen = true;
                    echoes++;
                }
                step = Step.Claim;
                stepAt = now;
                nextAction = now + 900;
                return Key('F', now);
            }
            if (ui.Claim)
            {
                step = Step.Claim;
                stepAt = now;
                return Idle();
            }
            if (!backedUp && now - stepAt < 1000)
                return Key('S', now);
            if (!backedUp)
                backedUp = true;
            if (now - stepAt > 3500)
            {
                step = Step.Claim;
                stepAt = now;
                return Idle();
            }
            if (now >= nextAction)
            {
                nextAction = now + 200;
                if (ui.Treasure)
                    return Steer(ui, frame, now);
                if (options.Pickup == FarmPickupMode.Circle && now >= nextCircleAt)
                {
                    if (circleStep > 0 && circleStep % 2 == 0 && circleDuration < 4000)
                        circleDuration += 800;
                    nextCircleAt = now + circleDuration;
                    char direction = "AWDS"[circleStep++ % 4];
                    return Key(direction, now);
                }
                if (options.Pickup == FarmPickupMode.Yolo)
                    return ChaseEcho(frame, now);
                if (!forwardHeld && options.Pickup != FarmPickupMode.Circle)
                {
                    forwardHeld = true;
                    return KeyDown('W', now);
                }
            }
            return Idle();
        }

        private FarmDecision Claim(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "关闭领奖 / 重开";
            if (ui.Claim)
            {
                if (now >= nextAction)
                {
                    nextAction = now + 700;
                    return Key(0x1B, now);
                }
                return Steer(ui, frame, now);
            }
            if (ui.SkipDialog)
            {
                if (now >= nextAction)
                {
                    nextAction = now + 600;
                    return Click(new Point((int)(frame.FrameSize.Width * .49),
                        (int)(frame.FrameSize.Height * .55)), now);
                }
                return Idle();
            }
            if (ui.FPrompt)
            {
                if (now >= nextAction)
                {
                    nextAction = now + 1200;
                    if (now - lastScrollAt > 5000)
                    {
                        lastScrollAt = now;
                        return Scroll(now);
                    }
                    return Key('F', now);
                }
                return Idle();
            }
            if (ui.Challenge || ui.TeamStart)
            {
                step = Step.Enter;
                stepAt = now;
                enterClicks = 0;
                return Idle();
            }
            if (now - stepAt > (bypassEndWait ? 1200 : 2600))
            {
                if (options.Teleport && !InRealm(ui) && teleportAttempts < 3)
                {
                    teleportAttempts++;
                    step = Step.Teleport;
                    teleportStep = TeleportStep.OpenBook;
                    stepAt = now;
                    return Idle();
                }
                step = Step.Approach;
                stepAt = now;
                nextAction = now;
                combatSeen = false;
                return Idle();
            }
            if (ui.Treasure && now >= nextAction)
            {
                nextAction = now + 300;
                return Steer(ui, frame, now);
            }
            return Idle();
        }

        private FarmDecision Revive(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "角色倒下，等待复活";
            if (!ui.Death)
            {
                if (options.Teleport && InRealm(ui) && realmExitAt == 0)
                {
                    realmExitAt = now;
                    return Key(0x1B, now);
                }
                if (realmExitAt > 0 && now - realmExitAt < 3000)
                    return Idle();
                if (now - stepAt > 2500)
                {
                    realmExitAt = 0;
                    if (options.Teleport && !InRealm(ui) && !healUsed)
                    {
                        healUsed = true;
                        healStep = 0;
                        step = Step.HealTeleport;
                        stepAt = now;
                        nextAction = now;
                        return Idle();
                    }
                    if (options.Teleport)
                    {
                        step = Step.Teleport;
                        teleportStep = TeleportStep.OpenBook;
                        stepAt = now;
                        return Idle();
                    }
                    step = Step.Approach;
                    stepAt = now;
                    nextAction = now;
                    combatSeen = false;
                }
                return Idle();
            }
            if (now >= nextAction)
            {
                nextAction = now + 1200;
                return Click(new Point((int)(frame.FrameSize.Width * .66), (int)(frame.FrameSize.Height * .70)), now);
            }
            return Idle();
        }

        private FarmDecision Teleport(FarmFrame frame, FarmUi ui, long now)
        {
            detail = TeleportDetail();
            if (now - stepAt > 30000)
            {
                if (teleportAttempts >= 3)
                    return Fail("自动传送失败。请手动传送到 Boss 附近，然后重新开始。");
                teleportAttempts++;
                teleportStep = TeleportStep.OpenBook;
                stepAt = now;
                nextAction = now;
                return Idle();
            }
            switch (teleportStep)
            {
                case TeleportStep.OpenBook:
                    if (ui.Claim || ui.Challenge || ui.TeamStart)
                    {
                        nextAction = now + 900;
                        return Key(0x1B, now);
                    }
                    if (ui.Book)
                    {
                        teleportStep = TeleportStep.PickCategory;
                        stepAt = now;
                        nextAction = now;
                        return Idle();
                    }
                    if (now >= nextAction)
                    {
                        nextAction = now + 4500;
                        return Function(2);
                    }
                    return Idle();
                case TeleportStep.PickCategory:
                    if (ui.Book)
                    {
                        if (now >= nextAction)
                        {
                            nextAction = now + 1600;
                            teleportStep = TeleportStep.PickKind;
                            Rectangle box = ui.BookBox;
                            Point point = box.IsEmpty
                                ? new Point((int)(frame.FrameSize.Width * .036), (int)(frame.FrameSize.Height * .236))
                                : new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
                            return Click(point, now);
                        }
                        return Idle();
                    }
                    teleportStep = TeleportStep.OpenBook;
                    stepAt = now;
                    return Idle();
                case TeleportStep.PickKind:
                    if (now >= nextAction)
                    {
                        nextAction = now + 2000;
                        teleportStep = TeleportStep.PickBoss;
                        return Click(new Point((int)(frame.FrameSize.Width * .24),
                            (int)(frame.FrameSize.Height * KindRow())), now);
                    }
                    return Idle();
                case TeleportStep.PickBoss:
                    if (now >= nextAction)
                    {
                        int wanted = options.BossIndex - 1;
                        if (ui.BossRows != null && ui.BossRows.Count > 0 && wanted < ui.BossRows.Count)
                        {
                            nextAction = now + 2000;
                            teleportStep = TeleportStep.Travel;
                            Rectangle row = ui.BossRows[wanted];
                            return Click(new Point(row.X + row.Width / 2, row.Y + row.Height / 2), now);
                        }
                        if (ui.BossRows != null && scrollClicks < 2 && (ui.BossRows.Count == 0 || wanted >= ui.BossRows.Count))
                        {
                            scrollClicks++;
                            nextAction = now + 1500;
                            return Click(At(frame, .973, ScrollY(ui)), now);
                        }
                        nextAction = now + 2000;
                        teleportStep = TeleportStep.Travel;
                        if (ui.BossRows != null && ui.BossRows.Count > 0)
                        {
                            Rectangle row = ui.BossRows[ui.BossRows.Count - 1];
                            return Click(new Point(row.X + row.Width / 2, row.Y + row.Height / 2), now);
                        }
                        return Idle();
                    }
                    return Idle();
                case TeleportStep.Travel:
                    if (ui.Teleport && now >= nextAction)
                    {
                        nextAction = now + 2000;
                        teleportStep = TeleportStep.Confirm;
                        return Click(new Point(ui.TeleportBox.X + ui.TeleportBox.Width / 2,
                            ui.TeleportBox.Y + ui.TeleportBox.Height / 2), now);
                    }
                    if (now >= nextAction)
                    {
                        nextAction = now + 2000;
                        teleportStep = TeleportStep.Confirm;
                        return Click(new Point((int)(frame.FrameSize.Width * .89),
                            (int)(frame.FrameSize.Height * .92)), now);
                    }
                    return Idle();
                case TeleportStep.Confirm:
                    if (ui.SkipDialog && now >= nextAction)
                    {
                        nextAction = now + 600;
                        return Click(new Point((int)(frame.FrameSize.Width * .49),
                            (int)(frame.FrameSize.Height * .55)), now);
                    }
                    if (now >= nextAction)
                    {
                        teleportStep = TeleportStep.WaitWorld;
                        stepAt = now;
                        nextAction = now + 500;
                    }
                    return Idle();
                case TeleportStep.WaitWorld:
                    if (now - stepAt > 2500)
                    {
                        teleportAttempts = 0;
                        step = Step.Approach;
                        stepAt = now;
                        nextAction = now;
                        combatSeen = false;
                        entered = false;
                    }
                    return Idle();
                default:
                    return Idle();
            }
        }

        private double KindRow()
        {
            switch (options.TeleportMode)
            {
                case FarmTeleportMode.WeeklyChallenge: return .61;
                case FarmTeleportMode.SpecialNightmare: return .86;
                default: return .49;
            }
        }

        private Point LevelPoint(FarmFrame frame)
        {
            string wanted = options.BossLevel.ToString();
            if (frame.LevelLines != null)
            {
                foreach (OcrLine line in frame.LevelLines)
                {
                    if (line.Text == null || line.Text.IndexOf(wanted, StringComparison.Ordinal) < 0)
                        continue;
                    return new Point(line.Box.X + line.Box.Width / 2, line.Box.Y + line.Box.Height / 2);
                }
            }
            return new Point((int)(frame.FrameSize.Width * .15),
                (int)(frame.FrameSize.Height * (0.17 + (BossRow() - 1) * 0.08)));
        }

        private int BossRow()
        {
            int start = options.BossLevel <= 50 ? 1 : options.BossLevel <= 60 ? 2 : options.BossLevel <= 70 ? 3 :
                options.BossLevel <= 80 ? 4 : 5;
            return start;
        }

        private FarmDecision Navigate(double turn, long now)
        {
            if (turn > 45) return Key('D', now);
            if (turn < -45) return Key('A', now);
            if (turn > 8) return Key('D', now);
            if (turn < -8) return Key('A', now);
            if (!forwardHeld)
            {
                forwardHeld = true;
                return KeyDown('W', now);
            }
            if (now - lastCameraAt > 1200)
            {
                lastCameraAt = now;
                return Middle(now);
            }
            return Idle();
        }

        private bool InRealm(FarmUi ui)
        {
            return ui != null && ui.InRealm && !treatAsNotInRealm;
        }

        private double ScrollY(FarmUi ui)
        {
            double top = ui.ScrollTop > 0 ? ui.ScrollTop : .1963;
            double bottom = .8806;
            int total = options.TeleportMode == FarmTeleportMode.WeeklyChallenge ? 9 :
                options.TeleportMode == FarmTeleportMode.BossChallenge ? 20 : 15;
            int header = options.TeleportMode == FarmTeleportMode.SpecialNightmare ? 1 : 0;
            double item = (bottom - top - header * .028) / total;
            double y = top + item * options.BossIndex + header * .028;
            return Math.Min(bottom, y);
        }

        private string TeleportDetail()
        {
            switch (teleportStep)
            {
                case TeleportStep.OpenBook: return "打开图鉴";
                case TeleportStep.PickCategory: return "选择 Boss 图鉴";
                case TeleportStep.PickKind: return "选择分类";
                case TeleportStep.PickBoss: return "选择 Boss";
                case TeleportStep.Travel: return "传送";
                case TeleportStep.Confirm: return "确认传送";
                default: return "等待传送完成";
            }
        }

        private FarmDecision ChaseEcho(FarmFrame frame, long now)
        {
            if (frame.Echoes == null || frame.Echoes.Count == 0)
            {
                if (!forwardHeld)
                {
                    forwardHeld = true;
                    return KeyDown('W', now);
                }
                return Idle();
            }
            EchoBox best = frame.Echoes[0];
            int center = frame.FrameSize.Width / 2;
            float distance = Math.Abs(best.Box.X + best.Box.Width / 2 - center);
            foreach (EchoBox echo in frame.Echoes)
            {
                float candidate = Math.Abs(echo.Box.X + echo.Box.Width / 2 - center);
                if (candidate < distance) { best = echo; distance = candidate; }
            }
            int dx = best.Box.X + best.Box.Width / 2 - center;
            int bottom = best.Box.Y + best.Box.Height;
            if (bottom > frame.FrameSize.Height * .72 && Math.Abs(dx) < frame.FrameSize.Width * .10)
            {
                if (frame.Ui != null && frame.Ui.FPrompt)
                    return Key('F', now);
                if (!forwardHeld)
                {
                    forwardHeld = true;
                    return KeyDown('W', now);
                }
                return Idle();
            }
            if (bottom > frame.FrameSize.Height * .65 && Math.Abs(dx) < frame.FrameSize.Width * .16)
            {
                if (!forwardHeld)
                {
                    forwardHeld = true;
                    return KeyDown('W', now);
                }
                return KeyDown('S', now);
            }
            if (dx > frame.FrameSize.Width * .05) return Key('D', now);
            if (dx < -frame.FrameSize.Width * .05) return Key('A', now);
            if (!forwardHeld)
            {
                forwardHeld = true;
                return KeyDown('W', now);
            }
            return Idle();
        }

        private string MatchBoss(string text)
        {
            if (text == null) return null;
            if (text.Contains("伪作的神王")) return "伪作的神王";
            if (text.Contains("异构武装") || text.Contains("加尔古耶")) return "异构武装";
            if (text.Contains("狮像") || text.Contains("亚狮诺索")) return "荣耀狮像";
            if (text.Contains("罗蕾莱") || text.Contains("夜之女皇")) return "罗蕾莱";
            return null;
        }

        private FarmDecision HealTeleport(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "传送到回血点";
            if (now - stepAt > 20000)
            {
                step = Step.Teleport;
                stepAt = now;
                nextAction = now;
                return Idle();
            }
            if (now < nextAction)
                return Idle();
            switch (healStep)
            {
                case 0:
                    healStep = 1;
                    nextAction = now + 2200;
                    return Key('M', now);
                case 1:
                    if (ui.HasHealPoint)
                    {
                        healStep = 2;
                        nextAction = now + 1500;
                        return Click(new Point(ui.HealBox.X + ui.HealBox.Width / 2,
                            ui.HealBox.Y + ui.HealBox.Height / 2), now);
                    }
                    if (now - stepAt > 7000) { healStep = 3; nextAction = now + 500; }
                    return Idle();
                case 2:
                    healStep = 3;
                    nextAction = now + 2000;
                    if (ui.Teleport)
                        return Click(new Point(ui.TeleportBox.X + ui.TeleportBox.Width / 2,
                            ui.TeleportBox.Y + ui.TeleportBox.Height / 2), now);
                    return Click(At(frame, .89, .92), now);
                case 3:
                    healStep = 4;
                    nextAction = now + 2500;
                    if (ui.SkipDialog)
                        return Click(At(frame, .49, .55), now);
                    return Idle();
                default:
                    step = Step.Teleport;
                    stepAt = now;
                    nextAction = now;
                    return Idle();
            }
        }

        private FarmDecision AimTeleport(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "按名称搜索传送";
            if (now - stepAt > 30000)
            {
                aimStep = 0;
                mapStep = 0;
                step = Step.MapTeleport;
                stepAt = now;
                nextAction = now;
                return Idle();
            }
            if (now < nextAction)
                return Idle();
            switch (aimStep)
            {
                case 0:
                    aimStep = 1;
                    nextAction = now + 4000;
                    return Function(2);
                case 1:
                    if (ui.BookMonsters)
                    {
                        aimStep = 2;
                        nextAction = now + 1600;
                        Rectangle box = ui.BookMonstersBox;
                        return Click(new Point(box.X + box.Width / 2, box.Y + box.Height / 2), now);
                    }
                    return Idle();
                case 2:
                    aimStep = 3;
                    nextAction = now + 900;
                    return Click(At(frame, .13, .14), now);
                case 3:
                    aimStep = 4;
                    nextAction = now + 900;
                    return new FarmDecision { Request = FarmRequest.Text, Text = aimBoss ?? "异构武装" };
                case 4:
                    aimStep = 5;
                    nextAction = now + 1200;
                    return Click(At(frame, .39, .13), now);
                case 5:
                    aimStep = 6;
                    nextAction = now + 1200;
                    return Click(At(frame, .13, .24), now);
                case 6:
                    aimStep = 7;
                    nextAction = now + 1200;
                    return Click(At(frame, .89, .92), now);
                case 7:
                    aimStep = 8;
                    nextAction = now + 2500;
                    return Click(At(frame, .89, .92), now);
                default:
                    step = Step.Approach;
                    stepAt = now;
                    nextAction = now;
                    combatSeen = false;
                    return Idle();
            }
        }

        private FarmDecision MapTeleport(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "地图传送到 Boss";
            if (now - stepAt > 20000)
            {
                step = Step.Approach;
                stepAt = now;
                nextAction = now;
                return Idle();
            }
            if (now < nextAction)
                return Idle();
            switch (mapStep)
            {
                case 0:
                    mapStep = 1;
                    nextAction = now + 2200;
                    return Key('M', now);
                case 1:
                    if (ui.HasMapMarker)
                    {
                        mapStep = 2;
                        nextAction = now + 1200;
                        return Click(new Point(ui.MapMarker.X + ui.MapMarker.Width / 2,
                            ui.MapMarker.Y + ui.MapMarker.Height / 2), now);
                    }
                    if (now - stepAt > 6000)
                    {
                        mapStep = 4;
                        nextAction = now + 500;
                    }
                    return Idle();
                case 2:
                    mapStep = 3;
                    nextAction = now + 2000;
                    if (ui.Teleport)
                        return Click(new Point(ui.TeleportBox.X + ui.TeleportBox.Width / 2,
                            ui.TeleportBox.Y + ui.TeleportBox.Height / 2), now);
                    return Click(At(frame, .89, .92), now);
                case 3:
                    mapStep = 4;
                    nextAction = now + 2500;
                    if (ui.SkipDialog)
                        return Click(At(frame, .49, .55), now);
                    return Idle();
                default:
                    step = Step.Approach;
                    stepAt = now;
                    nextAction = now;
                    combatSeen = false;
                    return Idle();
            }
        }

        private FarmDecision Night(FarmFrame frame, long now)
        {
            long elapsed = now - nightStepAt;
            detail = "切换昼夜";
            switch (nightStep)
            {
                case 0:
                    nightStep++;
                    nightStepAt = now;
                    return Key(0x1B, now);
                case 1 when elapsed >= 900:
                    nightStep++;
                    return Click(At(frame, .71, .96), now);
                case 2 when elapsed >= 1900:
                    nightStep++;
                    return Click(At(frame, .19, .14), now);
                case 3 when elapsed >= 2900:
                    nightStep++;
                    return Click(At(frame, .82, .53), now);
                case 4 when elapsed >= 3900:
                    nightStep++;
                    return Click(At(frame, .82, .53), now);
                case 5 when elapsed >= 4900:
                    nightStep++;
                    return Click(At(frame, .82, .53), now);
                case 6 when elapsed >= 5900:
                    nightStep++;
                    return Click(At(frame, .52, .90), now);
                case 7 when elapsed >= 7400:
                    nightStep++;
                    return Key(0x1B, now);
                case 8 when elapsed >= 8900:
                    nightStep++;
                    return Key(0x1B, now);
                case 9 when elapsed >= 10000:
                    nightStep = 0;
                    step = Step.Approach;
                    stepAt = now;
                    nextAction = now;
                    return Idle();
            }
            return Idle();
        }

        private static Point At(FarmFrame frame, double x, double y)
        {
            return new Point((int)(frame.FrameSize.Width * x), (int)(frame.FrameSize.Height * y));
        }

        private FarmDecision Steer(FarmUi ui, FarmFrame frame, long now)
        {
            Rectangle target = ui.Treasure ? ui.TreasureBox : Rectangle.Empty;
            if (target.IsEmpty)
                return Idle();
            int center = frame.FrameSize.Width / 2;
            int dx = target.X - center;
            if (Math.Abs(dx) > frame.FrameSize.Width * .08)
                return Key(dx > 0 ? 'D' : 'A', now);
            if (!forwardHeld)
            {
                forwardHeld = true;
                return KeyDown('W', now);
            }
            return Idle();
        }

        private void Kill()
        {
            kills++;
            planner.Invalidate(0);
            entered = false;
        }

        private FarmDecision Fail(string reason)
        {
            if (retries < 3)
            {
                retries++;
                return Retry(reason);
            }
            step = Step.Failed;
            Failure = reason;
            detail = reason;
            return Idle();
        }

        private FarmDecision Retry(string reason)
        {
            planner.Invalidate(0);
            step = Step.WaitGame;
            stepAt = 0;
            nextAction = 0;
            teleportAttempts = 0;
            enterClicks = 0;
            bossStep = 0;
            combobulate();
            detail = "重试 " + retries + "/3：" + reason;
            return Idle();
        }

        private void combobulate()
        {
            combatSeen = false;
            absorbSeen = false;
            forwardHeld = false;
            realmExitAt = 0;
        }

        private FarmDecision Battle(BattleAction action, long now)
        {
            return new FarmDecision { Request = FarmRequest.Battle, Battle = action };
        }

        private FarmDecision Idle()
        {
            return new FarmDecision { Request = FarmRequest.None };
        }

        private FarmDecision Function(int number)
        {
            return new FarmDecision { Request = FarmRequest.Key, VirtualKey = (ushort)(0x6f + number) };
        }

        private FarmDecision Click(Point point, long now)
        {
            return new FarmDecision { Request = FarmRequest.Click, Point = point };
        }

        private FarmDecision Key(char key, long now)
        {
            return new FarmDecision { Request = FarmRequest.Key, Key = key };
        }

        private FarmDecision Key(int key, long now)
        {
            return new FarmDecision { Request = FarmRequest.Key, Key = (char)key };
        }

        private FarmDecision KeyDown(char key, long now)
        {
            return new FarmDecision { Request = FarmRequest.KeyDown, Key = key };
        }

        private FarmDecision Scroll(long now)
        {
            return new FarmDecision { Request = FarmRequest.Scroll };
        }

        private FarmDecision Middle(long now)
        {
            return new FarmDecision { Request = FarmRequest.Middle };
        }

        private void PressForwardRelease(long now)
        {
            ReleaseForward(now);
        }

        private void ReleaseForward(long now)
        {
            if (!forwardHeld) return;
            forwardHeld = false;
        }

        private string Describe()
        {
            switch (step)
            {
                case Step.WaitGame: return "等待游戏画面";
                case Step.Approach: return "寻找 Boss / 交互目标";
                case Step.Enter: return "进入挑战";
                case Step.Fight: return "战斗中";
                case Step.Loot: return "拾取声骸";
                case Step.Claim: return "战斗结算";
                case Step.Revive: return "等待复活";
                case Step.Teleport: return TeleportDetail();
                case Step.Done: return "已完成";
                default: return "已停止";
            }
        }
    }
}
