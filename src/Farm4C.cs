using System;
using System.Collections.Generic;
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
        public FarmOptions() { UseLiberation = true; }

        public bool Teleport { get; set; }
        public FarmTeleportMode TeleportMode { get; set; }
        public int BossIndex { get; set; }
        public int BossChallengeIndex { get; set; }
        public int WeeklyChallengeIndex { get; set; }
        public int SpecialNightmareIndex { get; set; }

        internal int IndexFor(FarmTeleportMode category)
        {
            int saved;
            switch (category)
            {
                case FarmTeleportMode.BossChallenge: saved = BossChallengeIndex; break;
                case FarmTeleportMode.WeeklyChallenge: saved = WeeklyChallengeIndex; break;
                case FarmTeleportMode.SpecialNightmare: saved = SpecialNightmareIndex; break;
                default: return 1;
            }
            return saved != 0 ? saved : category == TeleportMode ? Math.Max(1, BossIndex) : 1;
        }

        internal static int LimitFor(FarmTeleportMode category)
        {
            return category == FarmTeleportMode.WeeklyChallenge ? 9 :
                category == FarmTeleportMode.SpecialNightmare ? 10 : 20;
        }

        public int BossLevel { get; set; }
        public int RepeatCount { get; set; }
        public int WaitSeconds { get; set; }
        public bool SwitchHealer { get; set; }
        public bool UseLiberation { get; set; }
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
                BossChallengeIndex < 0 || BossChallengeIndex > 20 || WeeklyChallengeIndex < 0 || WeeklyChallengeIndex > 9 ||
                SpecialNightmareIndex < 0 || SpecialNightmareIndex > 10 || BossIndex < 1 || BossIndex > 20 || RepeatCount < 1 || RepeatCount > 100000 ||
                WaitSeconds < 0 || WaitSeconds > 60)
                throw new System.IO.InvalidDataException("刷取配置无效。");
            BossLevel = Math.Max(50, Math.Min(90, BossLevel));
        }
    }

    internal enum FarmRequest { None, Click, AltClick, Key, KeyDown, KeyUp, Middle, AdjustDirection, Scroll, Battle, Text, RecoverRealm, DismissMonthlyCard }

    internal struct FarmDecision
    {
        internal FarmRequest Request;
        internal Point Point;
        internal char Key;
        internal ushort VirtualKey;
        internal BattleAction Battle;
        internal string Text;
        internal bool PickupKey, StartsRechallenge, RetryInput, AdvancesBossSearch, AdvancesLevelSelection, StartsWalkInChallenge;
        internal int ScrollTicks, HoldMs;

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
        internal readonly EchoPrompt PickupPrompt;
        internal readonly bool PickupReceipt;
        internal readonly string BossName;

        internal FarmFrame(HudSnapshot hud, bool hudValid, FarmUi ui, Size frameSize, Rectangle bounds,
            System.Collections.Generic.List<EchoBox> echoes = null, double navTurn = double.NaN,
            System.Collections.Generic.List<OcrLine> levelLines = null, EchoPrompt pickupPrompt = EchoPrompt.None,
            string bossName = null, bool pickupReceipt = false)
        {
            BossName = bossName;
            NavTurn = navTurn;
            LevelLines = levelLines;
            PickupPrompt = pickupPrompt;
            PickupReceipt = pickupReceipt;
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
        private const int ApproachTimeout = 15000, TargetProbeTimeout = 5000;
        private const int InitialProbeMs = 1500, UnmarkedTravelWalkMs = 3000, ManualRealmWalkMs = 1000;
        private const int CombatLostMs = 2500;
        private const int EntryMenuTimeout = 15000, LoadingTimeout = 120000;

        private enum Step { WaitGame, BossScript, Approach, Rechallenge, RechallengeMenu, Enter, Fight, Loot, Claim, Revive, RecoverRealm, BackFromWaypoint, ReturnToEncounter, Teleport, HealTeleport, MapTeleport, AimTeleport, Night, Done, Failed }
        private enum TeleportStep { OpenBook, PickCategory, PickKind, PickSpecial, PickBoss, Travel, Confirm, WaitWorld }

        private readonly FarmOptions options;
        private readonly CombatSettings settings;
        private readonly bool cloud;
        private int killsBeforeAttempt;
        private readonly ushort guidebookKey;
        private readonly BattlePlanner planner;
        private Step step = Step.WaitGame;
        private TeleportStep teleportStep = TeleportStep.OpenBook;
        private long stepAt, nextAction, lastCombatAt, lastCameraAt;
        private long lastGuideAt;
        private long reselectAt;
        private bool combatSeen, entered, inRealm;
        private bool approachAfterTravel, manualRealmApproach, usedApproachFallback;
        private char movementKey;
        private int teleportAttempts, enterClicks, kills, echoes, retries, circleStep, scrollClicks, nightStep,
            mapStep, aimStep, bossStep, healStep;
        private bool enteredFromWaypoint, returningToEncounter;
        internal bool EnteredFromWaypoint { get { return enteredFromWaypoint; } }
        private List<Rectangle> healWaypoints;
        private int nextHealWaypoint;
        private long healChoiceDeadline;
        private string aimBoss;
        private long lastNightChange, nightStepAt, lastScrollAt, nextPickupAt;
        private static readonly int[] CircleDurations = { 800, 800, 800, 1600, 1600, 2400, 2400, 2400 };
        private enum WalkSearch { Center, Backward, Forward, Finished }
        private WalkSearch walkSearch;
        private bool dropSeen, roundAbsorbed, lootSearchFinished, lootCameraCentered, centerNextYoloView;
        private long waitBeforeCombat, nextTargetAt, pickupDeadline, pickupWindowUntil, firstYoloDetection = -1;
        private EchoApproach echoApproach;
        private int pickupAttempts, pickupScrolls, yoloTurns, deathRecoveries;
        private int startClicks, rowClicks, rechallengePresses, rechallengeMenuOpens;
        private Rectangle selectedBossRow;
        private Rectangle entryConfirmation;
        private bool confirmationChecked, entryOpenedByInteraction, entryAfterRechallenge, requireBossLevel;
        private double scrollTarget = double.NaN;
        private PickupReceiptCheck pickupReceipt;
        private readonly int combatWaitMs;
        private readonly bool bypassEndWait, treatAsNotInRealm;
        private string detail = "等待游戏窗口";

        internal Farm4C(CombatSettings settings, FarmOptions options, bool cloud = false, Func<DateTime> clock = null)
        {
            this.options = options;
            this.settings = settings;
            this.clock = clock ?? (() => DateTime.UtcNow);
            startedAt = this.clock();
            this.cloud = cloud;
            guidebookKey = settings.GuidebookVirtualKey();
            planner = new BattlePlanner(settings, cloud, targetForTask: true, supportOpener: options.SwitchHealer,
                useLiberation: options.UseLiberation);
            planner.PreferMainAfterHealer = true;
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
        internal string Phase
        {
            get { return step == Step.Teleport ? step + "." + teleportStep : step.ToString(); }
        }
        internal string Failure { get; private set; }
        internal string CompletionMessage { get; private set; }
        internal bool Finished { get { return step == Step.Done || step == Step.Failed; } }
        internal int Kills { get { return kills; } }
        internal int Echoes { get { return echoes; } }
        internal double EchoesPerHour { get; private set; }
        private readonly Func<DateTime> clock;
        private DateTime startedAt;

        internal BattlePlanner Planner { get { return planner; } }
        internal char MovementKey { get { return movementKey; } }
        internal bool WantsSprint { get { return movementKey != '\0' &&
            (step == Step.Approach || step == Step.BossScript || step == Step.BackFromWaypoint || step == Step.ReturnToEncounter ||
             step == Step.Loot && options.Pickup == FarmPickupMode.Circle); } }
        internal bool Realm { get { return inRealm && !treatAsNotInRealm; } }
        internal bool SearchingForEcho { get { return step == Step.Loot && options.Pickup == FarmPickupMode.Yolo; } }
        internal bool WantsEchoDetection(long now)
        {
            return step == Step.Fight && combatSeen || SearchingForEcho && lootCameraCentered &&
                !centerNextYoloView && yoloTurns < 4 && now >= nextAction;
        }

        internal float EchoThreshold(FarmUi ui)
        {
            if (SearchingForEcho && firstYoloDetection >= 0) return .50f;
            return Realm || ui != null && ui.InRealm && options.Boss != FarmBoss.NightmareHecate ? .25f : .65f;
        }

        private bool guideKeySent;
        internal bool WantsGuideEntrance
        {
            get { return (step == Step.WaitGame && options.Teleport) ||
                (step == Step.Teleport && teleportStep == TeleportStep.OpenBook) ||
                (step == Step.AimTeleport && aimStep <= 1); }
        }
        internal bool AwaitingBossDestination { get { return step == Step.Teleport && teleportStep == TeleportStep.Travel; } }
        internal bool NeedsBossMenuMarker { get { return AwaitingBossDestination ||
            step == Step.Enter && !entered && !entryAfterRechallenge; } }
        internal bool WantsMonsterGuide { get { return step == Step.AimTeleport; } }
        internal bool WantsLevelScan { get { return step == Step.Enter && enterClicks == 0; } }
        internal bool AtStart { get { return step == Step.WaitGame; } }
        internal bool WaitingForEntryControls { get { return step == Step.Enter ||
            step == Step.WaitGame && !options.Teleport ||
            step == Step.Teleport && teleportStep == TeleportStep.Travel; } }
        internal bool NeedsMapMarker { get { return step == Step.MapTeleport && mapStep == 1; } }
        internal bool WantsWaypoints(long now)
        {
            return step == Step.HealTeleport && healStep == 1 && healWaypoints == null && now >= nextAction;
        }
        internal bool SearchingForEntry { get { return step == Step.Approach; } }
        internal bool WantsNavigation { get { return SearchingForEntry && !inRealm; } }
        internal bool CanScanInteraction { get { return SearchingForEntry || step == Step.Rechallenge ||
            (step == Step.WaitGame && !options.Teleport) || (step == Step.Claim && inRealm); } }
        internal bool WantsRechallengeChoice(FarmUi ui)
        {
            return step == Step.Rechallenge || CanScanInteraction && (ui.InRealm || inRealm || ui.Treasure);
        }
        internal bool WantsRechallengeDialog
        {
            get { return step == Step.RechallengeMenu || step == Step.Rechallenge || step == Step.Approach || step == Step.Claim ||
                step == Step.WaitGame && !options.Teleport || step == Step.Enter && entryAfterRechallenge; }
        }
        internal bool WantsPickupScan
        {
            get { return step == Step.Loot || step == Step.Claim || step == Step.Rechallenge || step == Step.Fight && !Realm ||
                step == Step.BossScript && options.Boss == FarmBoss.Fenrico ||
                step == Step.Approach && pickupReceipt != null; }
        }

        internal FarmDecision Observe(FarmFrame frame, long now)
        {
            detail = Describe();
            if (step == Step.Done || step == Step.Failed)
                return Idle();
            FarmUi ui = frame.Ui ?? new FarmUi();
            HudSnapshot hud = frame.HudValid ? frame.Hud : null;
            if (ui.InRealm || hud != null && (hud.InRealm || hud.InChallenge) && !hud.OpenWorld)
                inRealm = true;
            else if (!ui.Death && (ui.OpenWorld || hud != null && hud.OpenWorld))
                inRealm = false;
            bool realm = Realm;
            planner.SwitchTimeoutMs = realm ? 5000 : 3000;
            planner.LostCombatTimeoutMs = realm ? 3000 : 1200;
            if (!ui.CloudDisconnectBox.IsEmpty && cloud && !frame.HudValid)
                return Stop("云游戏显示断线弹窗。");
            if (ui.MonthlyCard)
            {
                ReleaseForward(now);
                detail = "领取月卡奖励";
                return new FarmDecision { Request = FarmRequest.DismissMonthlyCard };
            }

            if (step != Step.Night && options.Boss == FarmBoss.Lorelei && !ui.Death &&
                (lastNightChange == 0 || now - lastNightChange > 660000) && hud != null &&
                !hud.Locked && !hud.EnemyHealth && step == Step.Approach)
            {
                lastNightChange = now;
                nightStep = 0;
                nightStepAt = now;
                step = Step.Night;
                ReleaseForward(now);
                return Idle();
            }
            if (!string.IsNullOrEmpty(frame.BossName))
                aimBoss = MatchBoss(frame.BossName) ?? aimBoss;
            if (ui.Death && step != Step.Revive && step != Step.Teleport && step != Step.Night &&
                step != Step.RecoverRealm && step != Step.HealTeleport && step != Step.AimTeleport && step != Step.MapTeleport)
            {
                if (step == Step.Loot && !roundAbsorbed)
                    kills--;
                planner.Invalidate(now);
                if (realm && !options.Teleport)
                    return Stop("副本内角色倒下，手动进入的挑战无法自动重新传送。");
                returningToEncounter = false;
                step = realm ? Step.RecoverRealm : Step.Revive;
                stepAt = now;
                nextAction = now;
                ReleaseForward(now);
                return Idle();
            }

            switch (step)
            {
                case Step.WaitGame: return WaitGame(frame, hud, ui, now);
                case Step.BossScript: return BossScript(frame, hud, ui, now);
                case Step.Approach: return Approach(frame, hud, ui, now);
                case Step.Rechallenge: return Rechallenge(frame, hud, ui, now);
                case Step.RechallengeMenu: return RechallengeMenu(frame, hud, ui, now);
                case Step.Enter: return Enter(frame, ui, now);
                case Step.Fight: return Fight(frame, hud, ui, now);
                case Step.Loot: return Loot(frame, hud, ui, now);
                case Step.Claim: return Claim(frame, hud, ui, now);
                case Step.Revive: return Revive(frame, ui, now);
                case Step.RecoverRealm: return new FarmDecision { Request = FarmRequest.RecoverRealm };
                case Step.BackFromWaypoint: return BackFromWaypoint(frame, ui, now);
                case Step.ReturnToEncounter: return ReturnToEncounter(frame, hud, ui, now);
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
            if (step == Step.Loot) LootInputSent(decision, now);
            if (decision.StartsWalkInChallenge && step == Step.Enter)
            {
                entered = true;
                stepAt = now;
                nextAction = now + 5000;
            }
            if (decision.AdvancesLevelSelection && step == Step.Enter) enterClicks++;
            if (decision.AdvancesBossSearch && step == Step.AimTeleport) aimStep++;
            if (decision.StartsRechallenge)
            {
                rechallengePresses++;
                RunLog.Info("刷4C", "已发出重新挑战操作，尝试次数 " + rechallengePresses);
                PrepareEnter(now, fromInteraction: true, fromRechallenge: true);
                if (decision.Request == FarmRequest.Click)
                    nextAction = now + 1800;
            }
            if (decision.PickupKey)
            {
                pickupAttempts++;
                RunLog.Info("刷4C", "已发出第 " + pickupAttempts + " 次吸收操作，等待 2500 毫秒获取通知");
                pickupDeadline = now + 2500;
                pickupReceipt = new PickupReceiptCheck(alreadyVisible: false);
            }
        }

        internal void Suspend(long now)
        {
            planner.Invalidate(now);
            ReleaseForward(now);
            if (step == Step.Fight)
                nextTargetAt = now;
        }

        internal void MovementRejected() { movementKey = '\0'; }

        private FarmDecision WaitGame(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            if (options.Teleport && options.IndexFor(options.TeleportMode) > BossLimit())
                return Stop("当前图鉴分类的 Boss 序号最大为 " + BossLimit() + "。");
            if (options.Teleport && (PlayableScene(frame.Hud, ui) || ui.Book || ui.Terminal))
            {
                teleportAttempts = 0;
                return OpenBossBook(now);
            }
            if (!ui.ClaimCancelBox.IsEmpty && !frame.HudValid && !ui.SkipDialog && !ui.ClaimReward &&
                !ui.Challenge && ui.RechallengeButton.IsEmpty)
                return Stop("领奖弹窗挡住了刷取入口。");
            if (!options.Teleport)
            {
                if (!ui.RechallengeButton.IsEmpty || ui.ClaimReward)
                {
                    StartRechallengeMenu(now);
                    return RechallengeMenu(frame, hud, ui, now);
                }
                if (EntryVisible(ui) && !ui.SkipDialog)
                {
                    PrepareEnter(now);
                    return Enter(frame, ui, now);
                }
                if (PlayableScene(frame.Hud, ui))
                {
                    if (hud != null && HasBossScript)
                    {
                        step = Step.BossScript;
                        bossStep = 0;
                        stepAt = nextAction = now;
                        return Idle();
                    }
                    StartApproach(now, manualStart: true);
                    return Approach(frame, hud, ui, now);
                }
            }
            detail = ui.Book || ui.Teleport ? "请返回 Boss 或挑战入口所在场景" :
                "等待游戏画面 · " + (frame.Hud == null ? "尚未读到队伍 HUD" : frame.Hud.Detail);
            return Idle();
        }

        internal static bool PlayableScene(HudSnapshot hud, FarmUi ui)
        {
            if (ui.Terminal || ui.Book || ui.Teleport || ui.Claim || ui.Death || EntryVisible(ui))
                return false;
            return hud != null && (hud.Valid || hud.PartySize > 0 && hud.Current >= 0 && (ui.OpenWorld || ui.InRealm));
        }

        private static bool EntryVisible(FarmUi ui)
        {
            return !ui.Book && (ui.TeamStart || ui.Challenge || ui.SkipDialog);
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
            if (hud == null || ui.Book || ui.Teleport)
            {
                ReleaseForward(now);
                return now - stepAt > LoadingTimeout ? Stop("Boss 流程未确认返回游戏。") : Idle();
            }
            if (hud != null && (hud.EnemyHealth || hud.Locked))
                return BeginFight(now, visible: true);
            if (now < nextAction)
                return Idle();
            if (options.Boss == FarmBoss.Fenrico)
            {
                if (bossStep == 0)
                {
                    FarmDecision pickup;
                    if (TryPickup(frame, now, out pickup))
                        return pickup;
                    if (dropSeen && !roundAbsorbed || pickupAttempts > 0)
                        return Stop("芬里科传送前发现了声骸，但未确认吸收。");
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
                return StartApproach(now);
            }
            if (options.Boss == FarmBoss.FallacyOfNoReturn)
            {
                if (bossStep == 0)
                {
                    if (ui.FPrompt && !ui.Absorb)
                    {
                        PrepareEnter(now, fromInteraction: true);
                        return Key('F', now);
                    }
                    bossStep = 1;
                    nextAction = now + 2500;
                    return TeleportOrMap(frame, ui, now);
                }
                if (bossStep == 1) { bossStep = 2; nextAction = now + 400; return Key('D', now); }
                return StartApproach(now);
            }
            if (inRealm || ui.Treasure || ui.FPrompt)
                return StartApproach(now);
            return TeleportOrMap(frame, ui, now);
        }

        private FarmDecision TeleportOrMap(FarmFrame frame, FarmUi ui, long now)
        {
            ReleaseForward(now);
            if (aimBoss != null)
            {
                guideKeySent = false;
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
            if (!ui.RechallengeButton.IsEmpty || ui.ClaimReward)
            {
                StartRechallengeMenu(now);
                return RechallengeMenu(frame, hud, ui, now);
            }
            if (EntryVisible(ui))
            {
                PrepareEnter(now);
                return Enter(frame, ui, now);
            }
            if (ui.Claim)
            {
                ReleaseForward(now);
                if (now - stepAt >= TargetProbeTimeout)
                    return Stop("领奖界面未关闭，已停止移动。");
                if (now < nextAction)
                    return Idle();
                nextAction = now + 700;
                return Key(0x1B, now);
            }
            if (!PlayableScene(frame.Hud, ui))
            {
                ReleaseForward(now);
                detail = "等待返回游戏 · " + (frame.Hud == null ? "尚未读到队伍 HUD" : frame.Hud.Detail);
                return now - stepAt > LoadingTimeout ? Stop("未确认已返回游戏。") : Idle();
            }
            if (hud != null && (hud.EnemyHealth || hud.Locked))
                return BeginFight(now, visible: true);
            bool absorbVisible = ui.Absorb || frame.PickupPrompt != EchoPrompt.None;
            if ((inRealm && (ui.Treasure || absorbVisible)) || ui.Rechallenge != RechallengeChoice.None)
                return StartRechallenge(frame, hud, ui, now);
            if (!lootSearchFinished)
            {
                FarmDecision pickup;
                if (TryPickup(frame, now, out pickup))
                    return pickup;
            }
            if (absorbVisible)
            {
                ReleaseForward(now);
                detail = "等待确认吸收提示";
                return now - stepAt >= TargetProbeTimeout ?
                    Stop("看到吸收文字，但未确认交互键，已停止移动。") : Idle();
            }
            if (hud != null && hud.ChallengePrompt)
                return StartChallenge(hud, now);
            if (ui.FPrompt && !ui.Absorb)
            {
                PrepareEnter(now, fromInteraction: true);
                return Key('F', now);
            }

            bool marked = hud != null && !inRealm && !double.IsNaN(frame.NavTurn);
            long elapsed = now - stepAt;
            if (marked)
                lastGuideAt = now;
            if (elapsed >= ApproachTimeout || !marked && elapsed >= TargetProbeTimeout && now - lastGuideAt >= 1000)
            {
                ReleaseForward(now);
                if (marked && !approachAfterTravel && !usedApproachFallback)
                {
                    usedApproachFallback = true;
                    return TeleportOrMap(frame, ui, now);
                }
                return Stop(hud == null ? "已识别队伍，但技能栏未确认。请检查游戏 HUD 后重试。" :
                    "未锁定 Boss，也未找到挑战入口。手动模式请靠近目标或先锁敌再启动。");
            }
            bool travelWalk = approachAfterTravel && !inRealm && elapsed < InitialProbeMs + UnmarkedTravelWalkMs;
            bool realmWalk = manualRealmApproach && inRealm && elapsed < InitialProbeMs + ManualRealmWalkMs &&
                hud != null && !hud.ChallengeMarker;
            bool canWalk = hud != null && elapsed >= InitialProbeMs && (marked || travelWalk || realmWalk);
            if (!canWalk)
            {
                ReleaseForward(now);
                detail = hud == null ? "已识别队伍，正在尝试锁敌或寻找挑战入口" : "原地锁敌 / 等待挑战入口";
            }
            if (now < nextAction)
                return Idle();
            nextAction = now + 180;
            if (canWalk)
            {
                detail = marked ? "沿小地图接近 Boss" : "短距接近 Boss";
                if (marked)
                    return Navigate(frame.NavTurn, now);
                if (movementKey != 'W')
                    return KeyDown('W', now);
            }
            if (now - lastCameraAt < 500)
                return Idle();
            lastCameraAt = now;
            return canWalk ? Middle(now) : Battle(BattleAction.Target(), now);
        }

        private FarmDecision StartApproach(long now, bool afterTravel = false, bool manualStart = false)
        {
            ReleaseForward(now);
            step = Step.Approach;
            stepAt = nextAction = lastGuideAt = now;
            lastCameraAt = now - 500;
            approachAfterTravel = afterTravel;
            manualRealmApproach = manualStart && inRealm;
            return Idle();
        }

        private FarmDecision StartRechallenge(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            step = Step.Rechallenge;
            stepAt = lastGuideAt = nextAction = now;
            reselectAt = -1;
            rechallengePresses = 0;
            ReleaseForward(now);
            return Rechallenge(frame, hud, ui, now);
        }

        private FarmDecision Rechallenge(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "寻找重新挑战";
            if (EntryVisible(ui) && !ui.SkipDialog && !ui.ClaimReward)
            {
                PrepareEnter(now);
                return Enter(frame, ui, now);
            }
            if (hud != null && (hud.EnemyHealth || hud.Locked))
                return BeginFight(now, visible: true);
            bool absorbVisible = ui.Absorb || frame.PickupPrompt != EchoPrompt.None;
            if ((ui.FPrompt || absorbVisible) && reselectAt < 0)
                reselectAt = now;
            if (!lootSearchFinished)
            {
                FarmDecision pickup;
                if (TryPickup(frame, now, out pickup))
                    return pickup;
                if (pickupAttempts > 0 || frame.HudValid && frame.PickupPrompt != EchoPrompt.None)
                {
                    lootSearchFinished = true;
                    RunLog.Info("刷4C", string.Format("重开前拾取阶段结束 · 确认吸收={0} · 尝试={1}",
                        roundAbsorbed, pickupAttempts));
                }
            }
            if (!ui.RechallengeButton.IsEmpty || ui.ClaimReward)
            {
                StartRechallengeMenu(now);
                return RechallengeMenu(frame, hud, ui, now);
            }
            if (now - stepAt > ApproachTimeout + (reselectAt >= 0 || ui.FPrompt ? TargetProbeTimeout : 0))
                return Stop("未能从结算宝箱打开重新挑战。");
            if (ui.Claim)
            {
                ReleaseForward(now);
                if (now < nextAction)
                    return Idle();
                nextAction = now + 700;
                return Key(0x1B, now);
            }
            if (!PlayableScene(frame.Hud, ui))
            {
                ReleaseForward(now);
                return Idle();
            }
            if (inRealm && absorbVisible && lootSearchFinished)
            {
                StartRechallengeMenu(now);
                return OpenRechallengeMenu(now);
            }
            if (ui.FPrompt || absorbVisible || reselectAt >= 0)
            {
                ReleaseForward(now);
                detail = "已靠近结算宝箱，查找「重新挑战」";
                if (ui.FPrompt && !absorbVisible && ui.Rechallenge == RechallengeChoice.Selected)
                {
                    if (now < nextAction)
                        return Idle();
                    nextAction = now + 600;
                    FarmDecision confirm = Key('F', now);
                    confirm.StartsRechallenge = true;
                    confirm.RetryInput = rechallengePresses > 0;
                    return confirm;
                }
                bool rewardSelected = !absorbVisible && ui.Rechallenge == RechallengeChoice.RewardSelected;
                int selectionTimeout = absorbVisible ? TargetProbeTimeout : 3500;
                if (inRealm && (rewardSelected && now >= nextAction || now - reselectAt >= selectionTimeout))
                {
                    StartRechallengeMenu(now);
                    return OpenRechallengeMenu(now);
                }
                if (reselectAt >= 0 && now - reselectAt >= TargetProbeTimeout)
                    return Stop("已找到结算交互，但副本状态未确认，交互列表也未识别到「重新挑战」。");
                if (!ui.FPrompt || now < nextAction)
                    return Idle();
                switch (ui.Rechallenge)
                {
                    case RechallengeChoice.Above:
                    case RechallengeChoice.Below:
                    case RechallengeChoice.RewardSelected:
                        nextAction = now + 450;
                        return Scroll(now, ui.Rechallenge == RechallengeChoice.Above ? -1 : 1);
                    default: return Idle();
                }
            }
            if (hud != null && hud.ChallengePrompt && !ui.Treasure)
                return StartChallenge(hud, now);
            if (hud != null && ui.Treasure && !ui.TreasureBox.IsEmpty)
            {
                lastGuideAt = now;
                detail = "靠近结算宝箱";
                return Steer(ui, frame, now);
            }
            ReleaseForward(now);
            return now - lastGuideAt > 2500 ? Stop("失去结算宝箱标记，已停止移动。") : Idle();
        }

        private void StartRechallengeMenu(long now)
        {
            RunLog.Info("刷4C", "切换到重新挑战菜单");
            ReleaseForward(now);
            step = Step.RechallengeMenu;
            stepAt = nextAction = now;
            rechallengeMenuOpens = 0;
        }

        private FarmDecision OpenRechallengeMenu(long now)
        {
            detail = "按 Esc 打开副本重开菜单";
            nextAction = now + 1000;
            var escape = Key(0x1B, now);
            escape.RetryInput = rechallengeMenuOpens++ > 0;
            return escape;
        }

        private FarmDecision RechallengeMenu(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "等待副本重开菜单";
            if (now - stepAt >= 9000)
                return Stop("未识别到副本「确认离开」弹窗中的「重新挑战」。");
            if (now < nextAction)
                return Idle();
            if (ui.ClaimReward)
            {
                nextAction = now + 750;
                return Key(0x1B, now);
            }
            if (!ui.RechallengeButton.IsEmpty)
                return ClickRechallenge(ui.RechallengeButton, now);
            if (hud != null && (hud.Locked || hud.EnemyHealth))
                return BeginFight(now, visible: true);
            if (rechallengeMenuOpens < 2 && inRealm && PlayableScene(frame.Hud, ui))
                return OpenRechallengeMenu(now);
            return Idle();
        }

        private FarmDecision ClickRechallenge(Rectangle button, long now)
        {
            if (rechallengePresses >= 2)
                return Stop("已两次点击「重新挑战」，副本弹窗仍未关闭。");
            detail = "点击副本弹窗的「重新挑战」";
            nextAction = now + 900;
            var click = Click(Center(button), now);
            click.StartsRechallenge = true;
            click.RetryInput = rechallengePresses > 0;
            return click;
        }

        private FarmDecision Enter(FarmFrame frame, FarmUi ui, long now)
        {
            detail = entryAfterRechallenge ? "等待重新挑战加载" : "进入挑战";
            if (now - stepAt > (entered || entryAfterRechallenge ? LoadingTimeout : EntryMenuTimeout))
            {
                if (entryAfterRechallenge)
                    return Stop("选了「重新挑战」后，未确认下一轮加载。");
                return Stop(entered ? "没有确认挑战已加载。" : "未找到指定等级或挑战按钮。");
            }
            if (now < nextAction)
                return Idle();
            if (entryAfterRechallenge && ui.ClaimReward)
            {
                nextAction = now + 750;
                return Key(0x1B, now);
            }
            if (entryAfterRechallenge && !ui.RechallengeButton.IsEmpty)
                return ClickRechallenge(ui.RechallengeButton, now);
            if (EntryConfirmationVisible(ui))
                return ConfirmEntryDialog(ui, now);
            confirmationChecked = false;
            bool enemy = frame.HudValid && frame.Hud != null && (frame.Hud.Locked || frame.Hud.EnemyHealth);
            if (enemy || entered && frame.HudValid && !ui.Challenge && !ui.Book)
            {
                if (entered)
                    inRealm = true;
                return BeginFight(now, visible: enemy);
            }
            if (entryAfterRechallenge && !ui.Challenge && !ui.Book && !ui.Teleport)
            {
                if (ui.Claim)
                    return Idle();
                if (frame.HudValid && frame.Hud.ChallengePrompt && !ui.Treasure)
                    return StartChallenge(frame.Hud, now);
                if (ui.FPrompt && ui.Treasure && now - stepAt >= 3000)
                {
                    if (rechallengePresses >= 2)
                        return Stop("已两次选择「重新挑战」，仍停在结算宝箱旁。");
                    step = Step.Rechallenge;
                    stepAt = nextAction = lastGuideAt = now;
                    reselectAt = -1;
                    return Rechallenge(frame, frame.HudValid ? frame.Hud : null, ui, now);
                }
            }
            if (requireBossLevel && enterClicks < 2)
            {
                if (!ui.Challenge) return Idle();
                bool chooseLevel = enterClicks == 0;
                Point target = chooseLevel ? LevelPoint(frame) : At(frame, .880, .911);
                if (target.IsEmpty) return Idle();
                nextAction = now + (chooseLevel ? (entryOpenedByInteraction ? 1000 : 500) : 2000);
                var choice = Click(target, now);
                choice.AdvancesLevelSelection = true;
                return choice;
            }
            if (requireBossLevel && entryOpenedByInteraction && !entered && ui.Challenge &&
                !ui.Book && !ui.Claim && ui.ConfirmBox.IsEmpty && enterClicks == 2)
            {
                var begin = Click(At(frame, .908, .919), now);
                begin.StartsWalkInChallenge = true;
                nextAction = now + 500;
                return begin;
            }
            if (requireBossLevel && entryOpenedByInteraction && entered) return Idle();
            Rectangle start = ui.TeamStart ? ui.TeamStartBox : ui.StartBattleBox;
            if (!start.IsEmpty)
            {
                if (startClicks >= 3)
                    return Stop("已多次点击开始挑战，队伍界面仍未退出。");
                if (!entered)
                    stepAt = now;
                startClicks++;
                entered = true;
                nextAction = now + 1800;
                var click = Click(Center(start), now);
                click.RetryInput = startClicks % 2 == 0;
                return click;
            }
            if (!ui.Challenge || entered)
            {
                if (entryOpenedByInteraction && !entered && PlayableScene(frame.Hud, ui))
                {
                    detail = "已按 F，等待挑战开始或锁定 Boss";
                    nextAction = now + 500;
                    return Battle(BattleAction.Target(), now);
                }
                return Idle();
            }
            if (enterClicks == 0)
            {
                Point level = LevelPoint(frame);
                if (level.IsEmpty)
                    return Idle();
                enterClicks = 1;
                nextAction = now + 500;
                return Click(level, now);
            }
            if (!requireBossLevel && !ui.ChallengeBox.IsEmpty)
            {
                nextAction = now + 1250;
                return Click(Center(ui.ChallengeBox), now);
            }
            return Idle();
        }

        private void PrepareEnter(long now, bool fromInteraction = false, bool fromRechallenge = false, bool weekly = false)
        {
            step = Step.Enter;
            stepAt = now;
            nextAction = !fromInteraction ? now : now + (options.Teleport && !fromRechallenge ? 3000 : 700);
            enterClicks = startClicks = 0;
            entered = confirmationChecked = false;
            requireBossLevel = weekly || options.Teleport && fromInteraction && !fromRechallenge;
            entryOpenedByInteraction = fromInteraction;
            entryAfterRechallenge = fromRechallenge;
            ReleaseForward(now);
        }

        private FarmDecision StartChallenge(HudSnapshot hud, long now)
        {
            BeginFight(now, visible: false);
            planner.Observe(hud, now);
            return Key('F', now);
        }

        private bool EntryConfirmationVisible(FarmUi ui)
        {
            return !ui.ConfirmBox.IsEmpty && (ui.SkipDialog ||
                confirmationChecked && entryConfirmation.IntersectsWith(ui.ConfirmBox));
        }

        private FarmDecision ConfirmEntryDialog(FarmUi ui, long now)
        {
            nextAction = now + 500;
            if (!confirmationChecked && !ui.SkipCheckBox.IsEmpty)
            {
                confirmationChecked = true;
                entryConfirmation = ui.ConfirmBox;
                return Click(Center(ui.SkipCheckBox), now);
            }
            return Click(Center(ui.ConfirmBox), now);
        }

        private static Point Center(Rectangle box)
        {
            return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        }

        private FarmDecision Fight(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            bool visible = hud != null && (hud.EnemyHealth || hud.Locked);
            if (visible)
                lastCombatAt = now;
            if (now < waitBeforeCombat)
            {
                detail = "按配置等待开战";
                return Idle();
            }
            if ((!Realm || pickupReceipt != null) && !ui.Claim)
            {
                FarmDecision pickup;
                if (TryPickup(frame, now, out pickup))
                    return pickup;
            }
            if (combatSeen && hud != null && !visible && now - lastCombatAt >= 500 &&
                frame.Echoes != null && frame.Echoes.Count > 0)
            {
                dropSeen = true;
                return FinishBattle(now);
            }
            if (combatSeen && ui.Claim)
                return FinishBattle(now);

            BattleAction action = planner.Observe(frame.Hud ?? new HudSnapshot(), now);
            if (planner.Fighting)
            {
                combatSeen = true;
                detail = planner.Status;
            }
            if (combatSeen && !planner.Fighting && hud != null && !visible && now - lastCombatAt > CombatLostMs)
                return FinishBattle(now);
            if (!combatSeen && now - waitBeforeCombat > 5000)
                return Stop("没有检测到战斗。请确认已在 Boss 面前并开始挑战。");
            if (!combatSeen && hud != null && !hud.Locked && !hud.ChallengePrompt &&
                action.Kind == ActionKind.Wait && now >= nextTargetAt)
            {
                nextTargetAt = now + 500;
                action = BattleAction.Target();
            }
            return Battle(action, now);
        }

        private FarmDecision BeginFight(long now, bool visible)
        {
            if (returningToEncounter) return ReenterAfterRecovery(now);
            RunLog.Info("刷4C", string.Format("准备战斗 · 轮次={0} · 已见敌人={1} · 等待={2}ms", kills + 1,
                visible, combatWaitMs));
            ReleaseForward(now);
            step = Step.Fight;
            stepAt = now;
            waitBeforeCombat = now + combatWaitMs;
            lastCombatAt = visible ? now : waitBeforeCombat;
            nextTargetAt = now;
            combatSeen = false;
            dropSeen = roundAbsorbed = lootSearchFinished = false;
            pickupReceipt = null;
            pickupAttempts = pickupScrolls = 0;
            pickupDeadline = pickupWindowUntil = 0;
            nextPickupAt = now;
            return Idle();
        }

        private FarmDecision FinishBattle(long now)
        {
            ReleaseForward(now);
            Kill(now);
            step = Step.Loot;
            stepAt = now;
            nextAction = now;
            lootCameraCentered = false;
            walkSearch = WalkSearch.Center;
            centerNextYoloView = false;
            firstYoloDetection = -1;
            echoApproach = null;
            yoloTurns = circleStep = 0;
            return Idle();
        }

        private FarmDecision Loot(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "查找声骸掉落";
            if (frame.Echoes != null && frame.Echoes.Count > 0)
                dropSeen = true;
            FarmDecision pickup;
            if (TryPickup(frame, now, out pickup))
                return pickup;
            if (!roundAbsorbed && hud != null && (hud.EnemyHealth || hud.Locked) &&
                !ui.Claim && !ui.Challenge && !ui.SkipDialog)
            {
                ReleaseForward(now);
                kills--;
                step = Step.Fight;
                waitBeforeCombat = nextTargetAt = lastCombatAt = now;
                combatSeen = false;
                detail = "发现敌人仍在场，恢复战斗";
                return Idle();
            }
            if (frame.HudValid && frame.PickupPrompt != EchoPrompt.None)
                return FinishLoot(now);
            if (ui.SkipDialog || ui.Challenge && !ui.Book)
                return FinishLoot(now);
            if (ui.Claim)
            {
                ReleaseForward(now);
                if (now - stepAt > 9000)
                    return Stop("领奖弹窗未关闭，已停止刷取。");
                if (now < nextAction)
                    return Idle();
                nextAction = now + 700;
                return Key(0x1B, now);
            }
            if (roundAbsorbed && pickupAttempts == 0)
                return FinishLoot(now);
            if (hud == null)
            {
                ReleaseForward(now);
                return now - stepAt > EntryMenuTimeout ? Stop("拾取时失去游戏画面。") : Idle();
            }
            if (now < nextAction)
                return Idle();
            if (!lootCameraCentered)
                return Middle(now, 20);
            if (options.Pickup == FarmPickupMode.Yolo)
                return ChaseEcho(frame, now);
            if (options.Pickup == FarmPickupMode.Circle)
                return circleStep == CircleDurations.Length ? FinishLoot(now) : KeyDown("WASD"[circleStep % 4], now);
            switch (walkSearch)
            {
                case WalkSearch.Center: return Middle(now, 20);
                case WalkSearch.Backward: return KeyDown('S', now);
                case WalkSearch.Forward: return KeyDown('W', now);
                default: return FinishLoot(now);
            }
        }

        private void LootInputSent(FarmDecision decision, long now)
        {
            if (decision.Request == FarmRequest.Middle)
            {
                if (!lootCameraCentered)
                {
                    lootCameraCentered = true;
                    centerNextYoloView = Realm && options.Pickup == FarmPickupMode.Yolo;
                    nextAction = now + 200;
                }
                else if (options.Pickup == FarmPickupMode.Yolo)
                {
                    centerNextYoloView = false;
                    nextAction = now + 1000;
                }
                else if (options.Pickup == FarmPickupMode.Walk)
                {
                    walkSearch = WalkSearch.Backward;
                    nextAction = now + 200;
                }
            }
            else if (options.Pickup == FarmPickupMode.Yolo && decision.Request == FarmRequest.Key && decision.Key == 'A')
            {
                yoloTurns++;
                centerNextYoloView = true;
                nextAction = now + 500;
            }
            else if (decision.Request == FarmRequest.KeyDown)
            {
                if (options.Pickup == FarmPickupMode.Circle)
                    nextAction = now + CircleDurations[circleStep++];
                else if (options.Pickup == FarmPickupMode.Walk)
                {
                    walkSearch = decision.Key == 'S' ? WalkSearch.Forward : WalkSearch.Finished;
                    nextAction = now + (decision.Key == 'S' ? 1000 : 3000);
                }
            }
        }

        private bool TryPickup(FarmFrame frame, long now, out FarmDecision decision)
        {
            decision = Idle();
            if (frame.PickupPrompt != EchoPrompt.None)
                dropSeen = true;
            if (pickupReceipt != null)
            {
                ReleaseForward(now);
                if (frame.Ui != null && frame.Ui.Claim)
                {
                    RunLog.Warn("刷4C", "吸收后出现领奖弹窗，停止等待本次获取通知");
                    pickupReceipt = null;
                    return false;
                }
                if (frame.HudValid && pickupReceipt.Observe(frame.PickupReceipt))
                {
                    echoes++;
                    EchoesPerHour = Math.Round(echoes * 3600.0 / Math.Max(1, (clock() - startedAt).TotalSeconds));
                    roundAbsorbed = true;
                    RunLog.Info("刷4C", "已确认新的声骸获取通知 · 累计声骸=" + echoes);
                    pickupReceipt = null;
                    pickupDeadline = pickupWindowUntil = 0;
                    pickupAttempts = pickupScrolls = 0;
                    nextPickupAt = now + 250;
                    detail = "已确认吸收声骸";
                    return true;
                }
                if (now < pickupDeadline)
                {
                    detail = "等待声骸获取通知";
                    return true;
                }
                RunLog.Warn("刷4C", string.Format("第 {0} 次吸收未收到获取通知 · 提示={1} · HUD可用={2}",
                    pickupAttempts, frame.PickupPrompt, frame.HudValid));
                pickupReceipt = null;
                pickupDeadline = 0;
            }
            if (frame.PickupPrompt == EchoPrompt.None || !frame.HudValid || pickupAttempts >= 2)
                return false;
            if (pickupWindowUntil == 0)
                pickupWindowUntil = now + 10000;
            if (now >= pickupWindowUntil)
                return false;
            ReleaseForward(now);
            detail = "选择吸收并拾取声骸";
            if (now < nextPickupAt)
                return true;
            if (frame.PickupReceipt)
            {
                if (pickupDeadline == 0)
                {
                    pickupDeadline = now + 3500;
                    RunLog.Info("刷4C", "吸收前已有获取通知，等待旧通知消失");
                }
                return now < pickupDeadline;
            }
            if (frame.PickupPrompt == EchoPrompt.Below)
            {
                if (pickupScrolls >= 2)
                    return false;
                pickupScrolls++;
                nextPickupAt = now + 350;
                decision = Scroll(now);
                return true;
            }
            nextPickupAt = now + 500;
            decision = Key('F', now);
            decision.PickupKey = true;
            decision.RetryInput = pickupAttempts > 0;
            return true;
        }

        private FarmDecision FinishLoot(long now)
        {
            RunLog.Info("刷4C", string.Format("本轮声骸搜寻结束 · 轮次={0} · 发现掉落={1} · 确认吸收={2} · 吸收尝试={3}",
                kills, dropSeen, roundAbsorbed, pickupAttempts));
            ReleaseForward(now);
            lootSearchFinished = true;
            step = Step.Claim;
            rechallengePresses = 0;
            usedApproachFallback = false;
            stepAt = now;
            nextAction = now + 250;
            return Idle();
        }

        private FarmDecision Claim(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "关闭领奖 / 重开";
            if (now - stepAt > EntryMenuTimeout && (ui.SkipDialog || ui.Claim || ui.FPrompt))
                return Stop("结算或重开界面未推进，刷取已停止。");
            if (ui.ClaimReward)
            {
                ReleaseForward(now);
                if (now < nextAction)
                    return Idle();
                nextAction = now + 750;
                return Key(0x1B, now);
            }
            if (EntryConfirmationVisible(ui))
            {
                ReleaseForward(now);
                return now >= nextAction ? ConfirmEntryDialog(ui, now) : Idle();
            }
            confirmationChecked = false;
            if (ui.Claim)
            {
                ReleaseForward(now);
                if (kills - killsBeforeAttempt < options.RepeatCount && !ui.RechallengeButton.IsEmpty && !ui.ClaimReward)
                    return ClickRechallenge(ui.RechallengeButton, now);
                if (now >= nextAction)
                {
                    nextAction = now + 700;
                    return Key(0x1B, now);
                }
                return Idle();
            }
            if (kills - killsBeforeAttempt >= options.RepeatCount)
            {
                ReleaseForward(now);
                if (now >= nextAction)
                    step = Step.Done;
                return Idle();
            }
            if (hud != null && !ui.Challenge && (hud.Locked || hud.EnemyHealth))
                return BeginFight(now, visible: true);
            bool absorbVisible = ui.Absorb || frame.PickupPrompt != EchoPrompt.None;
            if ((hud != null && ui.Treasure && !ui.TreasureBox.IsEmpty) ||
                (inRealm && (ui.FPrompt || absorbVisible)) || ui.Rechallenge != RechallengeChoice.None)
                return StartRechallenge(frame, hud, ui, now);
            if (ui.FPrompt && !absorbVisible)
            {
                ReleaseForward(now);
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
                PrepareEnter(now);
                return Idle();
            }
            if (now - stepAt > (bypassEndWait ? 1200 : 2600))
            {
                if (inRealm && PlayableScene(frame.Hud, ui))
                {
                    StartRechallengeMenu(now);
                    return OpenRechallengeMenu(now);
                }
                if (options.Teleport && !inRealm && teleportAttempts < 3)
                {
                    teleportAttempts++;
                    step = Step.Teleport;
                    guideKeySent = false;
                    teleportStep = TeleportStep.OpenBook;
                    stepAt = now;
                    return Idle();
                }
                combatSeen = false;
                return StartApproach(now);
            }
            return Idle();
        }

        private FarmDecision Revive(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "角色倒下，等待返回大世界";
            if (now - stepAt > 10000) return Stop("死亡后未确认返回大世界。");
            if (now < nextAction) return Idle();
            if (ui.Death || !WorldReady(frame, ui))
            {
                nextAction = now + 1000;
                return Key(0x1B, now);
            }
            if (Realm) return Stop("角色仍在副本，无法从当前位置打开回血地图。");
            healStep = 0;
            healWaypoints = null;
            nextHealWaypoint = 0;
            step = Step.HealTeleport;
            stepAt = nextAction = now;
            return Idle();
        }

        internal bool RecoverRealm(WorldSession session, Action<string> report)
        {
            return step == Step.RecoverRealm &&
                new WorldRecovery(session, report).Recover(enteredFromWaypoint, requireRealmForSkip: true);
        }

        internal void RealmRecoveryFinished(bool recovered, long now)
        {
            if (!recovered)
                Stop("副本死亡后未确认返回回血信标。");
            else
                ReenterAfterRecovery(now);
        }

        private FarmDecision ReenterAfterRecovery(long now)
        {
            returningToEncounter = false;
            ReleaseForward(now);
            if (!options.Teleport) return Stop("死亡恢复已结束，当前设置为手动进入首领挑战。");
            if (++deathRecoveries >= 3)
            {
                step = Step.Done;
                detail = CompletionMessage = "死亡恢复已达 3 次，结束本次刷取。";
                RunLog.Info("刷4C", detail);
                return Idle();
            }
            enteredFromWaypoint = true;
            inRealm = false;
            return OpenBossBook(now);
        }

        private FarmDecision BackFromWaypoint(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "已到回血信标，退后寻找首领";
            if (now < nextAction) return Idle();
            if (now - stepAt > 20000) return Stop("离开信标前未确认大世界。");
            if (movementKey != 'S')
            {
                if (!WorldReady(frame, ui)) return Idle();
                nextAction = now + 1000;
                return KeyDown('S', now);
            }
            returningToEncounter = true;
            return TeleportOrMap(frame, ui, now);
        }

        private FarmDecision ReturnToEncounter(FarmFrame frame, HudSnapshot hud, FarmUi ui, long now)
        {
            detail = "返回首领位置，寻找战斗或宝箱";
            if (now < nextAction) return Idle();
            if (hud != null && (hud.Locked || hud.EnemyHealth)) return ReenterAfterRecovery(now);
            if (ui.Treasure) return StartRechallenge(frame, hud, ui, now);
            if (now - stepAt >= 12500) return ReenterAfterRecovery(now);
            if (!WorldReady(frame, ui)) return Idle();
            if (movementKey != 'W') return KeyDown('W', now);
            nextAction = now + 500;
            return Middle(now);
        }

        private FarmDecision Teleport(FarmFrame frame, FarmUi ui, long now)
        {
            detail = TeleportDetail();
            if (now - stepAt > (teleportStep == TeleportStep.WaitWorld ? LoadingTimeout : 30000))
            {
                if (teleportAttempts >= 3)
                    return Stop("自动传送失败。请手动传送到 Boss 附近，然后重新开始。");
                teleportAttempts++;
                guideKeySent = false;
                teleportStep = TeleportStep.OpenBook;
                stepAt = now;
                nextAction = now;
                scrollClicks = 0;
                return Idle();
            }
            switch (teleportStep)
            {
                case TeleportStep.OpenBook:
                    if (ui.Terminal) return OpenGuideFromScene(frame, ui, now);
                    if ((ui.Claim || ui.Challenge || ui.TeamStart) && now >= nextAction)
                    {
                        nextAction = now + 900;
                        return Key(0x1B, now);
                    }
                    if (ui.Book)
                    {
                        teleportStep = ui.BookSelected ? TeleportStep.PickKind : TeleportStep.PickCategory;
                        stepAt = now;
                        nextAction = now;
                        scrollClicks = rowClicks = 0;
                        return Idle();
                    }
                    return OpenGuideFromScene(frame, ui, now);
                case TeleportStep.PickCategory:
                    if (ui.Book)
                    {
                        if (ui.BookSelected)
                        {
                            teleportStep = TeleportStep.PickKind;
                            nextAction = now;
                            return Idle();
                        }
                        if (now >= nextAction && !ui.BookBox.IsEmpty)
                        {
                            nextAction = now + 1600;
                            return Click(Center(ui.BookBox), now);
                        }
                        return Idle();
                    }
                    guideKeySent = false;
                    teleportStep = TeleportStep.OpenBook;
                    stepAt = now;
                    return Idle();
                case TeleportStep.PickKind:
                    if (now >= nextAction)
                    {
                        nextAction = now + 2000;
                        if (options.TeleportMode == FarmTeleportMode.SpecialNightmare)
                        {
                            teleportStep = TeleportStep.PickSpecial;
                            return Click(At(frame, .356, .882), now);
                        }
                        teleportStep = TeleportStep.PickBoss;
                        return Click(At(frame, .24, KindRow()), now);
                    }
                    return Idle();
                case TeleportStep.PickSpecial:
                    if (now < nextAction)
                        return Idle();
                    nextAction = now + 2000;
                    teleportStep = TeleportStep.PickBoss;
                    return Click(At(frame, .24, .86), now);
                case TeleportStep.PickBoss:
                    if (now >= nextAction)
                    {
                        int serial = BookSerial(options.TeleportMode, options.IndexFor(options.TeleportMode));
                        int visible = ui.BossRows == null ? 0 : ui.BossRows.Count;
                        int row = scrollClicks == 0 ? serial - 1 : visible - 1;
                        bool needsFirstScroll = scrollClicks == 0 && (serial > 4 || row >= visible);
                        bool needsCorrection = scrollClicks == 1 && options.TeleportMode == FarmTeleportMode.SpecialNightmare &&
                            ((visible == 3 || visible == 4) &&
                             ui.BossRows[visible - 1].Top < frame.FrameSize.Height * .73);
                        if (needsFirstScroll || needsCorrection)
                        {
                            scrollTarget = needsFirstScroll ? ScrollY(ui, serial) :
                                Math.Min(.8806, scrollTarget + (.8806 - ui.ScrollTop - .028) / 15 * .55);
                            scrollClicks++;
                            nextAction = now + 1200;
                            return Click(At(frame, .973, scrollTarget), now);
                        }
                        if (row >= 0 && row < visible)
                        {
                            nextAction = now + 1000;
                            teleportStep = TeleportStep.Travel;
                            selectedBossRow = ui.BossRows[row];
                            rowClicks = 1;
                            return Click(Center(selectedBossRow), now);
                        }
                    }
                    return Idle();
                case TeleportStep.Travel:
                    if (now < nextAction)
                        return Idle();
                    if (!ui.Book && (ui.TeamStart || ui.Challenge || ui.SkipDialog))
                    {
                        PrepareEnter(now, weekly: options.TeleportMode == FarmTeleportMode.WeeklyChallenge);
                        return Enter(frame, ui, now);
                    }
                    if (ui.Teleport && !ui.TeleportBox.IsEmpty)
                    {
                        enteredFromWaypoint = false;
                        teleportStep = TeleportStep.Confirm;
                        return ClickTravel(ui.TeleportBox, now);
                    }
                    if (rowClicks == 1 && ui.BookSelected && ui.BossRows.Contains(selectedBossRow))
                    {
                        rowClicks++;
                        nextAction = now + 1200;
                        var retry = Click(Center(selectedBossRow), now);
                        retry.RetryInput = true;
                        return retry;
                    }
                    return Idle();
                case TeleportStep.Confirm:
                    if (now < nextAction)
                        return Idle();
                    if (!ui.ConfirmBox.IsEmpty)
                        return ConfirmTravel(ui, now);
                    if (!ui.Teleport)
                    {
                        teleportStep = TeleportStep.WaitWorld;
                        stepAt = now;
                        nextAction = now + 500;
                    }
                    return Idle();
                case TeleportStep.WaitWorld:
                    if (WorldReady(frame, ui) && now >= nextAction)
                    {
                        teleportAttempts = 0;
                        ArrivedInWorld(now);
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
                    if (line.Text == null || !System.Text.RegularExpressions.Regex.IsMatch(line.Text,
                        @"(?<![0-9])" + wanted + @"(?![0-9])"))
                        continue;
                    return new Point(line.Box.X + line.Box.Width / 2, line.Box.Y + line.Box.Height / 2);
                }
            }
            return Point.Empty;
        }

        private FarmDecision Navigate(double turn, long now)
        {
            double angle = Math.Abs(turn);
            if (movementKey == 'W' && angle >= 10 && angle <= 80)
                return new FarmDecision { Request = FarmRequest.AdjustDirection, Key = turn > 0 ? 'D' : 'A' };
            char direction = angle <= 45 ? 'W' : angle > 135 ? 'S' : turn > 0 ? 'D' : 'A';
            if (movementKey != direction)
                return KeyDown(direction, now);
            if (now - lastCameraAt > 1200)
            {
                lastCameraAt = now;
                return Middle(now);
            }
            return Idle();
        }

        internal static int BookSerial(FarmTeleportMode mode, int index)
        {
            return index + (mode == FarmTeleportMode.SpecialNightmare ? 5 : 0);
        }

        private int BossLimit()
        {
            return FarmOptions.LimitFor(options.TeleportMode);
        }

        private double ScrollY(FarmUi ui, int serial)
        {
            double top = ui.ScrollTop > 0 ? ui.ScrollTop : GuidebookList.DefaultTop;
            double bottom = .8806;
            int total = BookSerial(options.TeleportMode, BossLimit());
            int header = options.TeleportMode == FarmTeleportMode.SpecialNightmare ? 1 : 0;
            double item = (bottom - top - header * .028) / total;
            double y = top + item * serial + (header > 0 && serial > 5 ? .028 : 0);
            return Math.Min(bottom, y);
        }

        private string TeleportDetail()
        {
            switch (teleportStep)
            {
                case TeleportStep.OpenBook: return "打开图鉴";
                case TeleportStep.PickCategory: return "选择 Boss 图鉴";
                case TeleportStep.PickKind: return "选择分类";
                case TeleportStep.PickSpecial: return "打开特异梦魇列表";
                case TeleportStep.PickBoss: return "选择 Boss";
                case TeleportStep.Travel: return "传送";
                case TeleportStep.Confirm: return "确认传送";
                default: return "等待传送完成";
            }
        }

        private FarmDecision ChaseEcho(FarmFrame frame, long now)
        {
            if (firstYoloDetection >= 0 && now - firstYoloDetection >= (Realm ? 12000 : 4000))
                return FinishLoot(now);
            if (centerNextYoloView)
                return Middle(now, 200);
            if (yoloTurns == 4) return FinishLoot(now);
            if (frame.Echoes == null) return Idle();
            if (firstYoloDetection < 0)
            {
                if (frame.Echoes.Count == 0) return Realm ? Key('A', now, 50) : FinishLoot(now);
                firstYoloDetection = now;
                echoApproach = new EchoApproach();
                RunLog.Info("刷4C", "已发现声骸，下一帧按追踪阈值重新定位");
                return Idle();
            }
            char direction = echoApproach.Direction(frame.Echoes, frame.FrameSize, movementKey, now);
            if (direction == '\0') return FinishLoot(now);
            return movementKey == direction ? Idle() : KeyDown(direction, now);
        }

        internal static Rectangle BossNameBounds(Size screen)
        {
            // The game anchors the boss bar to the center on wide displays.
            int left = (screen.Width - screen.Height * 16 / 9) / 2;
            return new Rectangle(left + (int)Math.Round(screen.Height * 1269.0 / 2160),
                (int)Math.Round(screen.Height * 10.0 / 2160), (int)Math.Round(screen.Height * 1264.0 / 2160),
                (int)Math.Round(screen.Height * 130.0 / 2160));
        }

        private static string MatchBoss(string text)
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
                return Stop(healStep == 3 ? "回血点传送后未确认返回游戏。" : "未确认找到可用的回血传送点。");
            if (now < nextAction)
                return Idle();
            switch (healStep)
            {
                case 0:
                    healStep = 1;
                    nextAction = now + 2200;
                    return Key('M', now);
                case 1:
                    if (WorldReady(frame, ui))
                    {
                        healWaypoints = null;
                        nextHealWaypoint = 0;
                        nextAction = now + 2200;
                        return Key('M', now);
                    }
                    if (healWaypoints == null && ui.HealWaypoints != null && ui.HealWaypoints.Count > 0)
                        healWaypoints = ui.HealWaypoints;
                    if (healWaypoints != null && nextHealWaypoint < healWaypoints.Count)
                    {
                        healStep = 2;
                        nextAction = now + 1000;
                        healChoiceDeadline = now + 4000;
                        return Click(Center(healWaypoints[nextHealWaypoint++]), now);
                    }
                    if (healWaypoints != null || now - stepAt > 7000)
                        return Stop("未确认找到可用的回血传送点。");
                    return Idle();
                case 2:
                    if (ui.WaypointTravelBox.IsEmpty)
                    {
                        if (now < healChoiceDeadline)
                            return Idle();
                        healStep = 1;
                        nextAction = now + 1000;
                        return Click(new Point((int)(frame.FrameSize.Width * .06), frame.FrameSize.Height / 2), now);
                    }
                    healStep = 3;
                    return ClickTravel(ui.WaypointTravelBox, now, horizontalOffset: 1.5);
                case 3:
                    if (WorldReady(frame, ui))
                    {
                        step = Step.BackFromWaypoint;
                        stepAt = now;
                        nextAction = now + 2000;
                        return Idle();
                    }
                    return ConfirmTravel(ui, now);
                default:
                    return Idle();
            }
        }

        private FarmDecision BossSearchInput(FarmDecision decision, long now, int delay)
        {
            decision.AdvancesBossSearch = true;
            nextAction = now + delay;
            return decision;
        }

        private FarmDecision AimTeleport(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "按名称搜索传送";
            if (now - stepAt > 30000)
            {
                if (aimStep >= 7)
                    return Stop("按名称传送后未确认返回游戏。");
                guideKeySent = false;
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
                    goto case 1;
                case 1:
                    if (!ui.BookMonsters || ui.Terminal) return OpenGuideFromScene(frame, ui, now);
                    if (!ui.BookMonstersSelected)
                    {
                        nextAction = now + 1000;
                        return Click(Center(ui.BookMonstersBox), now);
                    }
                    aimStep = 2;
                    goto case 2;
                case 2:
                    return BossSearchInput(Click(At(frame, .13, .14), now), now, 500);
                case 3:
                    return BossSearchInput(new FarmDecision { Request = FarmRequest.Text, Text = aimBoss }, now, 0);
                case 4:
                    return BossSearchInput(Click(At(frame, .39, .13), now), now, 500);
                case 5:
                    return BossSearchInput(Click(At(frame, .13, .24), now), now, 500);
                case 6:
                    if (!ui.BookMonstersSelected) return Idle();
                    return BossSearchInput(Click(At(frame, .89, .92), now), now, 1000);
                case 7:
                    if (WorldReady(frame, ui))
                    {
                        ArrivedInWorld(now);
                        return Idle();
                    }
                    if (!ui.BookMonstersSelected && !ui.Teleport) return Idle();
                    return BossSearchInput(Click(At(frame, .89, .92), now), now, 500);
                case 8:
                    if (WorldReady(frame, ui))
                    {
                        ArrivedInWorld(now);
                        return Idle();
                    }
                    return ConfirmTravel(ui, now);
                default:
                    return Idle();
            }
        }

        private FarmDecision MapTeleport(FarmFrame frame, FarmUi ui, long now)
        {
            detail = "地图传送到 Boss";
            if (now - stepAt > (mapStep == 3 ? LoadingTimeout : 20000))
                return Stop(mapStep == 3 ? "地图传送后未确认返回游戏。" : "地图上未找到可用的 Boss 传送按钮。");
            if (now < nextAction)
                return Idle();
            switch (mapStep)
            {
                case 0:
                    mapStep = options.Boss == FarmBoss.NamelessExplorer ? 4 : 1;
                    nextAction = now + 2000;
                    return Key('M', now);
                case 4:
                    if (WorldReady(frame, ui)) return Idle();
                    mapStep = 1;
                    nextAction = now + 500;
                    return Click(At(frame, .06, .50), now);
                case 1:
                    if (ui.HasMapMarker)
                    {
                        mapStep = 2;
                        nextAction = now + 1200;
                        return Click(new Point(ui.MapMarker.X + ui.MapMarker.Width / 2,
                            ui.MapMarker.Y + ui.MapMarker.Height / 2), now);
                    }
                    return Idle();
                case 2:
                    if (!ui.Teleport || ui.TeleportBox.IsEmpty)
                        return Idle();
                    mapStep = 3;
                    return ClickTravel(ui.TeleportBox, now);
                case 3:
                    if (WorldReady(frame, ui))
                    {
                        ArrivedInWorld(now);
                        return Idle();
                    }
                    return ConfirmTravel(ui, now);
                default:
                    return Idle();
            }
        }

        private FarmDecision ClickTravel(Rectangle button, long now, double horizontalOffset = .5)
        {
            confirmationChecked = false;
            stepAt = now;
            nextAction = now + 850;
            return Click(new Point(button.X + (int)(button.Width * horizontalOffset), button.Y + button.Height / 2), now);
        }

        private FarmDecision ConfirmTravel(FarmUi ui, long now)
        {
            if (ui.ConfirmBox.IsEmpty)
                return Idle();
            if (EntryConfirmationVisible(ui))
                return ConfirmEntryDialog(ui, now);
            nextAction = now + 850;
            return Click(Center(ui.ConfirmBox), now);
        }

        private static bool WorldReady(FarmFrame frame, FarmUi ui)
        {
            return frame.HudValid && !ui.Death && !ui.Terminal && !ui.Book && !ui.BookMonsters && !ui.Teleport && !ui.Challenge && !ui.Claim &&
                ui.ConfirmBox.IsEmpty;
        }

        private void ArrivedInWorld(long now)
        {
            combatSeen = entered = inRealm = false;
            if (returningToEncounter)
            {
                step = Step.ReturnToEncounter;
                stepAt = now;
                nextAction = now + 500;
                return;
            }
            StartApproach(now, afterTravel: true);
        }

        private FarmDecision OpenGuideFromScene(FarmFrame frame, FarmUi ui, long now)
        {
            if (now < nextAction) return Idle();
            if (ui.Terminal)
            {
                guideKeySent = false;
                nextAction = now + 1800;
                return ui.TerminalGuide.IsEmpty ? Key((char)0x1B, now) : Click(Center(ui.TerminalGuide), now);
            }
            if (!PlayableScene(frame.Hud, ui)) return Idle();
            nextAction = now + 4500;
            guideKeySent = !guideKeySent;
            if (guideKeySent) return new FarmDecision { Request = FarmRequest.Key, VirtualKey = guidebookKey };
            return new FarmDecision { Request = FarmRequest.AltClick, Point = At(frame, .77, .05) };
        }

        private FarmDecision OpenBossBook(long now)
        {
            step = Step.Teleport;
            guideKeySent = false;
            teleportStep = TeleportStep.OpenBook;
            stepAt = nextAction = now;
            return Idle();
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
                case 1:
                    if (elapsed < 900) break;
                    nightStep++;
                    return Click(At(frame, .71, .96), now);
                case 2:
                    if (elapsed < 1900) break;
                    nightStep++;
                    return Click(At(frame, .19, .14), now);
                case 3:
                    if (elapsed < 2900) break;
                    nightStep++;
                    return Click(At(frame, .82, .53), now);
                case 4:
                    if (elapsed < 3900) break;
                    nightStep++;
                    return Click(At(frame, .82, .53), now);
                case 5:
                    if (elapsed < 4900) break;
                    nightStep++;
                    return Click(At(frame, .82, .53), now);
                case 6:
                    if (elapsed < 5900) break;
                    nightStep++;
                    return Click(At(frame, .52, .90), now);
                case 7:
                    if (elapsed < 7400) break;
                    nightStep++;
                    return Key(0x1B, now);
                case 8:
                    if (elapsed < 8900) break;
                    nightStep++;
                    return Key(0x1B, now);
                case 9:
                    if (elapsed < 10000) break;
                    nightStep = 0;
                    return StartApproach(now);
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
            Point center = Center(target);
            int dx = center.X - frame.FrameSize.Width / 2;
            double horizontal = movementKey == 'W' || movementKey == 'S' ? .07 : .04;
            double vertical = movementKey == 'W' ? .60 : movementKey == 'S' ? .45 : .50;
            char direction = Math.Abs(dx) > frame.FrameSize.Width * horizontal ? (dx > 0 ? 'D' : 'A') :
                center.Y - frame.FrameSize.Height * .10 > frame.FrameSize.Height * vertical ? 'S' : 'W';
            return movementKey == direction ? Idle() : KeyDown(direction, now);
        }

        private void Kill(long now)
        {
            kills++;
            planner.Invalidate(now);
            entered = false;
        }

        internal Farm4C RestartAfterPopup(Func<bool> dismiss)
        {
            if (step != Step.Failed || retries >= 3 || !dismiss()) return null;
            var next = new Farm4C(settings, options, cloud, clock);
            next.retries = retries + 1;
            next.killsBeforeAttempt = next.kills = kills;
            next.echoes = echoes;
            next.EchoesPerHour = EchoesPerHour;
            if (echoes > 0) next.startedAt = startedAt;
            RunLog.Info("刷4C", "弹窗已处理，开始第 " + next.retries + "/3 次重试 · " + Failure);
            return next;
        }

        internal FarmDecision Stop(string reason)
        {
            movementKey = '\0';
            step = Step.Failed;
            Failure = reason;
            detail = reason;
            return Idle();
        }

        private FarmDecision Battle(BattleAction action, long now)
        {
            return new FarmDecision { Request = FarmRequest.Battle, Battle = action };
        }

        private FarmDecision Idle()
        {
            return new FarmDecision { Request = FarmRequest.None };
        }

        private FarmDecision Click(Point point, long now)
        {
            return new FarmDecision { Request = FarmRequest.Click, Point = point };
        }

        private FarmDecision Key(char key, long now, int holdMs = 60)
        {
            return new FarmDecision { Request = FarmRequest.Key, Key = key, HoldMs = holdMs };
        }

        private FarmDecision Key(int key, long now)
        {
            return new FarmDecision { Request = FarmRequest.Key, Key = (char)key };
        }

        private FarmDecision KeyDown(char key, long now)
        {
            movementKey = key;
            return new FarmDecision { Request = FarmRequest.KeyDown, Key = key };
        }

        private FarmDecision Scroll(long now, int ticks = 1)
        {
            return new FarmDecision { Request = FarmRequest.Scroll, ScrollTicks = ticks };
        }

        private FarmDecision Middle(long now, int holdMs = 40)
        {
            return new FarmDecision { Request = FarmRequest.Middle, HoldMs = holdMs };
        }

        private void ReleaseForward(long now)
        {
            movementKey = '\0';
        }

        private string Describe()
        {
            switch (step)
            {
                case Step.WaitGame: return "等待游戏画面";
                case Step.Approach: return "寻找 Boss / 交互目标";
                case Step.Rechallenge: return "靠近结算宝箱 / 重新挑战";
                case Step.RechallengeMenu: return "等待副本重开菜单";
                case Step.Enter: return "进入挑战";
                case Step.Fight: return "战斗中";
                case Step.Loot: return "拾取声骸";
                case Step.Claim: return "战斗结算";
                case Step.Revive: return "等待复活";
                case Step.RecoverRealm: return "退本并传送到回血信标";
                case Step.BackFromWaypoint: return "从回血信标退后";
                case Step.ReturnToEncounter: return "返回首领位置";
                case Step.HealTeleport: return "地图寻找回血信标";
                case Step.Teleport: return TeleportDetail();
                case Step.Done: return CompletionMessage ?? "已完成";
                default: return "已停止";
            }
        }
    }
}
