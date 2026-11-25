using System;
using System.Collections.Generic;
using System.Linq;

namespace TideCombat
{
    internal enum ActionKind { Wait, Attack, Target, Cast, Switch, Button, Hold, Release }

    internal struct BattleAction
    {
        internal ActionKind Kind;
        internal Skill Skill;
        internal char Key;
        internal int Slot;
        internal bool FollowWithAttack, DuringAnimation, AttackBefore, ConsumesExecution, ReassertHold, HelperSwitch;
        internal BattleButton Button;
        internal int NativeKey, DownMilliseconds;

        internal static BattleAction Attack() { return new BattleAction { Kind = ActionKind.Attack }; }
        internal static BattleAction Target() { return new BattleAction { Kind = ActionKind.Target }; }
        internal static BattleAction Cast(Skill skill, char key)
        {
            return new BattleAction { Kind = ActionKind.Cast, Skill = skill, Key = key };
        }
        internal static BattleAction Switch(int slot, bool followWithAttack = false)
        {
            return new BattleAction { Kind = ActionKind.Switch, Slot = slot, FollowWithAttack = followWithAttack };
        }
        internal static BattleAction Press(BattleButton button, int key, bool hold)
        {
            return new BattleAction { Kind = hold ? ActionKind.Hold : ActionKind.Button, Button = button, NativeKey = key };
        }
        internal static BattleAction AnimationPulse(bool interact, bool attack)
        {
            BattleAction action = Press(interact ? BattleButton.Interact : BattleButton.Attack, interact ? 'F' : 0, false);
            action.DuringAnimation = true;
            action.AttackBefore = interact && attack;
            return action;
        }
        internal static BattleAction Release(BattleButton button, int key = 0)
        {
            return new BattleAction { Kind = ActionKind.Release, Button = button, NativeKey = key };
        }

        internal string Label
        {
            get
            {
                switch (Kind)
                {
                    case ActionKind.Attack: return "普攻";
                    case ActionKind.Target: return "尝试锁敌";
                    case ActionKind.Cast:
                        switch (Skill)
                        {
                            case Skill.Resonance: return "共鸣技能";
                            case Skill.Echo: return "声骸技能";
                            default: return "共鸣解放";
                        }
                    case ActionKind.Switch: return "切换 " + (Slot + 1) + " 号位";
                    case ActionKind.Hold: return "长按" + ButtonLabel(Button);
                    case ActionKind.Release: return "松开" + ButtonLabel(Button);
                    case ActionKind.Button: return ButtonLabel(Button);
                    default: return "等待";
                }
            }
        }

        private static string ButtonLabel(BattleButton button)
        {
            switch (button)
            {
                case BattleButton.Resonance: return "共鸣技能";
                case BattleButton.Echo: return "声骸技能";
                case BattleButton.Liberation: return "共鸣解放";
                case BattleButton.Jump: return "跳跃";
                case BattleButton.Dodge: return "闪避";
                case BattleButton.Interact: return "处决";
                case BattleButton.Forward: return "向前";
                case BattleButton.Left: return "向左";
                case BattleButton.Tool: return "探索工具";
                case BattleButton.Menu: return "修复锁敌图标位置";
                case BattleButton.MiddleClick: return "中键锁敌";
                default: return "普攻";
            }
        }
    }

    internal sealed class ChallengeStartObservation
    {
        private long seenAt = -1, promptGoneAt = -1;

        internal void Clear() { seenAt = promptGoneAt = -1; }

        internal long Observe(HudSnapshot hud, long now, bool consume)
        {
            if (hud == null || !hud.Valid || hud.OpenWorld)
                Clear();
            else if (hud.ChallengePrompt)
            {
                seenAt = now;
                promptGoneAt = -1;
            }
            else if (hud.ChallengeInteractVisible) Clear();
            else if (seenAt >= 0)
            {
                if (promptGoneAt < 0) promptGoneAt = now;
                if (now - seenAt >= 5000 || now - promptGoneAt > 500) Clear();
                else if (!hud.ChallengeMarker && consume)
                {
                    long remaining = seenAt + 5000 - now;
                    Clear();
                    return remaining;
                }
            }
            return 0;
        }
    }

    internal sealed class BattlePlanner
    {
        private const int LostCombatMs = 1700;
        private const int VisiblePartySwitchTimeoutMs = 10000;
        internal const int SwitchBlocked = 0, SwitchLow = 100, SwitchNormal = 200, SwitchHigh = 300, SwitchRequired = 400;
        private readonly CombatSettings settings;
        private readonly bool targetForTask;
        private readonly bool? supportOpener;
        private readonly bool? useLiberation;
        private readonly bool? openWorldAutoCombat;
        private readonly bool cloud;
        private readonly bool standalone;
        private readonly bool farmEchoTask;
        private readonly ChallengeStartObservation challengeObservation;
        private readonly Func<long> challengeClock;
        private readonly FreezeClock clock = new FreezeClock();
        private readonly int[] readyFrames = new int[3];
        private readonly char[] lastSeenKey = new char[3];
        private readonly long[,] nextCast = new long[3, 3];
        private readonly long[] blockedSwitch = new long[3];
        private readonly FighterMemory[] roster = { new FighterMemory(), new FighterMemory(), new FighterMemory() };
        private CharacterTurn turn;
        private CharacterTurn switchExecution;
        private bool planningRoutineSwitch, routineSwitchRequested, helperSwitchRequested, executionPending;
        private FighterMemory delayedSwitchExit;
        private long lastBreakCheck = -1001;
        private long recheckConcertoAt;
        private HudSnapshot previous;
        private bool releaseRequested, openerPending;
        private bool pendingIntro, pendingFull, entryIntro, entrySubDpsIntro;
        private BattleRole pendingOutroRole;
        private string pendingOutro, entryOutro;
        private int lockedFrames, healthFrames, sameSlotFrames;
        private int observedSlot = -1, activeSlot = -1, requestedSlot = -1;
        private long lastLock, fieldStart, battleStart, nextAction, switchDeadline, nextTarget, restartTurn;
        private long missingPartySwitchDeadline;
        private long retrySwitchAt, noSwitchAttackUntil;
        private long challengeStartUntil, lastChallengeTimer = -1;
        private double abyssTimerSeconds = double.NaN;
        private long cloudHealthLostAt = -1;
        private bool challengeActive;
        private bool knownParty;
        private bool battleLiberation;
        private int recoverySlot = -1, recoveryOrigin = -1;
        private long recoveryDeadline, recoverySentAt = -1;
        private bool recoveryRepeats, recoveryNeedsHealer;
        private long retargetDeadline, retargetHealthLostAt = -1;
        private long acquireUntil, acquireRetryAt;
        private bool retargetWithHealth;
        private long shiftedTargetSince = -1, layoutStepAt;
        private int layoutKeysSent;

        internal string Status { get; private set; }
        internal int SwitchTimeoutMs { get; set; }
        internal int LostCombatTimeoutMs { get; set; }
        internal bool PreferMainAfterHealer { get; set; }
        private long liberationGraceUntil;
        internal bool Fighting { get { return activeSlot >= 0; } }
        internal bool CombatAborted { get; private set; }
        internal bool RecoveringAfterBattle { get { return recoverySlot >= 0 || recoveryNeedsHealer; } }
        internal bool ProtectingAnimation(long now)
        {
            return turn != null && (turn.AnimationInProgress(now) || turn.CanContinueWithoutLock(now));
        }

        internal bool BlocksFarmEchoEnd(HudSnapshot hud, long now)
        {
            if (turn != null && turn.ReferenceLiberationInProgress(now)) return true;
            if (activeSlot < 0 || hud.Current != activeSlot) return false;
            FighterMemory actor = roster[activeSlot];
            if (HasCharacterGrace(hud, now)) return true;
            if (actor.Id == "Lily") return hud.Shows("lily_slumberland");
            return actor.Id == "Hsin" && turn != null && now < turn.Context.ContinueAfterLockLossUntil;
        }
        internal bool ShouldScanExecution(long now) { return !executionPending && now - lastBreakCheck > 1000; }
        internal int LucillaTransformSlot
        {
            get { return turn != null && turn.Context.LucillaTransformPending ? activeSlot : -1; }
        }

        internal BattlePlanner(CombatSettings settings, bool cloud = false, bool targetForTask = false,
            bool? supportOpener = null, bool? useLiberation = null,
            ChallengeStartObservation challengeObservation = null, Func<long> challengeClock = null, bool standalone = false,
            bool? openWorldAutoCombat = null, bool farmEchoTask = false)
        {
            this.settings = settings;
            this.cloud = cloud;
            this.standalone = standalone;
            this.farmEchoTask = farmEchoTask;
            this.targetForTask = targetForTask;
            this.supportOpener = supportOpener;
            this.useLiberation = useLiberation;
            this.openWorldAutoCombat = openWorldAutoCombat;
            this.challengeObservation = challengeObservation ?? new ChallengeStartObservation();
            this.challengeClock = challengeClock;
            SwitchTimeoutMs = 5000;
            LostCombatTimeoutMs = LostCombatMs;
        }

        internal BattleAction Observe(HudSnapshot hud, long now, Func<double> readAbyssTimer = null)
        {
            if (hud.Dead)
            {
                Invalidate(now);
                ExpireBattle(now + LostCombatMs);
                Status = "角色倒下，等待手动恢复";
                return default(BattleAction);
            }
            if (CombatAborted)
            {
                if (!targetForTask)
                    CombatAborted = false; // standalone may recognize a fresh encounter
                else
                {
                    Status = "共鸣解放超时，结束本次战斗";
                    return ObserveTaskAfterBattle(hud, now);
                }
            }
            BattleAction layoutAction;
            if (!cloud && TryRecoverTargetLayout(hud, now, out layoutAction))
            {
                Status = "正在恢复锁敌界面";
                return layoutAction;
            }
            if (!hud.Valid)
            {
                CancelChallengePrompt();
                BattleAction animation = turn == null ? default(BattleAction) : turn.ObserveUnavailable(now, hud.TeamHudHidden);
                if (turn != null && turn.Context.CombatExitReason != null)
                    return AbortBattle(previous, now, turn.Context.CombatExitReason);
                ResetLockConfirmation();
                healthFrames = 0;
                acquireUntil = 0;
                bool partyVisible = hud.PartySize >= 2;
                if (routineSwitchRequested && requestedSlot >= 0 &&
                    now >= (partyVisible ? switchDeadline : missingPartySwitchDeadline))
                {
                    FinishBattle(now);
                    Status = partyVisible ? "切换超时，重新确认战斗" : "切换后队伍画面未恢复";
                    return default(BattleAction);
                }
                if ((requestedSlot < 0 || now >= switchDeadline) &&
                    (turn == null || !turn.AnimationInProgress(now) && !turn.CanContinueWithoutLock(now)))
                    ExpireBattle(now, recover: true);
                if (activeSlot < 0) ExpireRecovery(now);
                if (activeSlot < 0 && knownParty && hud.PartySize == 0 && !hud.TeamHudHidden)
                {
                    for (int slot = 0; slot < roster.Length; slot++) roster[slot] = new FighterMemory();
                    clock.Clear();
                    knownParty = false;
                }
                Status = animation.DuringAnimation ? "技能动画中" : hud.Detail;
                return animation;
            }

            IdentifyTeam(hud, now);
            FighterMemory seen = roster[hud.Current];
            seen.ObserveCooldowns(hud, now);
            if (seen.Id == "Carlotta" && hud.Skills[0] != SkillState.Unknown)
            {
                seen.CarlottaResonanceAt = now;
                seen.CarlottaResonanceCooldown = hud.Skills[0] == SkillState.Cooling ?
                    (double.IsNaN(hud.Cooldowns[0]) ? 100 : (int)(1000 * hud.Cooldowns[0])) : 0;
            }
            if (activeSlot >= 0) ObserveBreakPrompt(hud, now);
            if (turn != null && hud.Current == activeSlot) turn.ObserveAvailable(hud, now);
            if (hud.Locked || hud.EnemyHealth) ResetRecovery();
            bool canObserveStart = !standalone || activeSlot < 0 && settings.WorldAssist != null && settings.WorldAssist.PickUp;
            long openingRemaining = canObserveStart ?
                challengeObservation.Observe(hud, challengeClock == null ? now : challengeClock(), consume: true) : 0;
            if (!canObserveStart) challengeObservation.Clear();
            if (openingRemaining > 0)
            {
                challengeStartUntil = now + openingRemaining;
                challengeActive = true;
            }
            if (hud.OpenWorld) ResetChallenge();
            bool opening = !hud.ChallengePrompt && now < challengeStartUntil;
            if (challengeActive && !opening && (lastChallengeTimer < 0 || now - lastChallengeTimer >= 400))
            {
                abyssTimerSeconds = readAbyssTimer == null ? double.NaN : readAbyssTimer();
                lastChallengeTimer = now;
            }
            if (hud.Locked || hud.EnemyHealth) cloudHealthLostAt = -1;
            else if (cloud && activeSlot >= 0 && cloudHealthLostAt < 0) cloudHealthLostAt = now;
            bool cloudCombat = cloud && (activeSlot < 0 ? (settings.AutoTarget || targetForTask) && hud.EnemyHealth :
                hud.EnemyHealth || cloudHealthLostAt >= 0 && now - cloudHealthLostAt < 3000);
            bool timedCombat = activeSlot >= 0 && ChallengeClockActive(now);
            if (opening || timedCombat) hud.InChallenge = true;
            bool characterGrace = HasCharacterGrace(hud, now) || now < liberationGraceUntil;
            bool switching = requestedSlot >= 0 && now < switchDeadline;
            if (!hud.Locked && !cloudCombat && !timedCombat && !characterGrace && !switching && !opening)
            {
                ResetLockConfirmation();
                if (activeSlot == hud.Current && activeSlot >= 0 && now - lastLock < 4000 &&
                    roster[activeSlot].Id == "Lily" && hud.Shows("lily_slumberland") && settings.RotateParty)
                {
                    int next = NextSlot(hud, now);
                    if (next >= 0 && requestedSlot < 0)
                    {
                        EndTurn();
                        Status = "梦乡已开始，切换队友";
                        return BattleAction.Switch(next);
                    }
                }
                if (turn != null && hud.Current == activeSlot && turn.CanContinueWithoutLock(now))
                {
                    Status = "战斗中";
                    return ObserveTurn(hud, now);
                }
                if (turn != null)
                {
                    if (turn.AnimationInProgress(now))
                    {
                        Status = "等待技能动画结束";
                        return default(BattleAction);
                    }
                    releaseRequested = true;
                    turn.ReleasedByWindow(now);
                }
                if (activeSlot >= 0)
                {
                    if (retargetDeadline == 0)
                    {
                        retargetWithHealth = hud.EnemyHealth;
                        retargetDeadline = now + (retargetWithHealth ? 30000 : 3000);
                    }
                    if (hud.EnemyHealth) retargetHealthLostAt = -1;
                    else if (retargetHealthLostAt < 0) retargetHealthLostAt = now;
                    bool lostHealth = retargetWithHealth && retargetHealthLostAt >= 0 && now - retargetHealthLostAt > 1200;
                    if (now < retargetDeadline && !lostHealth)
                    {
                        Status = "战斗中，重新锁敌";
                        return now >= nextTarget ? BattleAction.Target() : default(BattleAction);
                    }
                    if (!hud.EnemyHealth && settings.RotateParty)
                        BeginRecovery(hud, now);
                    FinishBattle(now);
                }
                if (recoverySlot >= 0 || recoveryNeedsHealer)
                {
                    BattleAction recovery;
                    if (ObserveRecovery(hud, now, out recovery))
                    {
                        Status = "战斗结束，切换队友";
                        return recovery;
                    }
                }
                return AcquireTarget(hud, now);
            }

            if (hud.Locked || cloudCombat && hud.EnemyHealth)
            {
                lastLock = now;
                ResetRetarget();
            }
            lockedFrames = Math.Min(2, lockedFrames + 1);
            healthFrames = 0;
            acquireUntil = 0;
            if (observedSlot != hud.Current)
            {
                observedSlot = hud.Current;
                sameSlotFrames = 0;
                Array.Clear(readyFrames, 0, readyFrames.Length);
            }
            sameSlotFrames = Math.Min(2, sameSlotFrames + 1);
            for (int index = 0; index < readyFrames.Length; index++)
            {
                if (lastSeenKey[index] != hud.Keys[index])
                {
                    lastSeenKey[index] = hud.Keys[index];
                    readyFrames[index] = 0;
                }
                readyFrames[index] = hud.Keys[index] != '\0' && hud.Skills[index] == SkillState.Ready
                    ? Math.Min(readyFrames[index] + 1, 2) : 0;
            }
            if (lockedFrames < 2 || sameSlotFrames < 2)
            {
                Status = "确认目标与当前角色";
                return default(BattleAction);
            }

            if (hud.Current != activeSlot)
            {
                ClearSwitchExecution(now);
                bool first = activeSlot < 0;
                bool hadFull = requestedSlot == hud.Current ? pendingFull : previous != null && previous.ConcertoFull;
                entryIntro = !first && (requestedSlot == hud.Current ? pendingIntro : hadFull);
                entryOutro = !entryIntro ? null : requestedSlot == hud.Current ? pendingOutro : roster[activeSlot].Id;
                entrySubDpsIntro = entryIntro && (requestedSlot == hud.Current ? pendingOutroRole :
                    RoleAt(previous ?? hud, activeSlot)) == BattleRole.Secondary;
                if (!first)
                {
                    FighterMemory leaving = roster[activeSlot];
                    CharacterKit leavingKit = CharacterRoster.Find(leaving.Id);
                    if (routineSwitchRequested && requestedSlot == hud.Current && entryIntro && leaving.Id == "Roccia")
                        roster[hud.Current].HasToolBox = true;
                    bool executingAfterSwitch = routineSwitchRequested && requestedSlot == hud.Current && !entryIntro &&
                        executionPending && BeginSwitchExecution(leaving, now, true);
                    leaving.FreeIntro = false;
                    if (leaving.Id == "Encore") leaving.EncoreStageAt = -1;
                    if (!executingAfterSwitch)
                        leaving.LastExit = now;
                    if (entryIntro)
                    {
                        BattleRole leavingRole = RoleAt(previous ?? hud, activeSlot);
                        leaving.LastOutro = now;
                        leaving.BuffStarted = now;
                        leaving.BuffDuration = leavingKit == null ? CharacterKit.DefaultBuffDuration(leavingRole) :
                            leavingKit.BuffDuration(settings);
                        if (leavingRole == BattleRole.Support)
                            leaving.LastFullHealerSwitch = now;
                    }
                }
                EndTurn();
                activeSlot = hud.Current;
                FighterMemory entering = roster[activeSlot];
                if (entering.Id == "Rover" && entering.RoverForm == null)
                    entering.RoverForm = hud.RoverForm ?? "RoverSpectro";
                if (first)
                {
                    battleStart = now;
                    battleLiberation = useLiberation ?? (settings.UseLiberation || hud.InChallenge || !hud.OpenWorld);
                    ObserveBreakPrompt(hud, now);
                }
                roster[activeSlot].LastEntry = now;
                if (entryIntro)
                {
                    FighterMemory incoming = roster[activeSlot];
                    string form = incoming.Id == "Rover" ? incoming.RoverForm : incoming.Id;
                    int introDuration = form == "Cartethyia" && incoming.CartethyiaTransformed ? 780 : CharacterKit.IntroDuration(form);
                    clock.Record(now, introDuration, now, true);
                }
                fieldStart = now;
                requestedSlot = -1;
                routineSwitchRequested = planningRoutineSwitch = helperSwitchRequested = false;
                openerPending = first;
                noSwitchAttackUntil = 0;
                if (!first && roster[activeSlot].Id == null) nextAction = Math.Max(nextAction, now + 270);
            }
            previous = hud;
            if (switchExecution != null)
            {
                BattleAction executionAction = switchExecution.Observe(hud, now);
                if (!switchExecution.Finished)
                {
                    Status = "处决中";
                    return executionAction;
                }
                ClearSwitchExecution(now);
            }
            if (requestedSlot >= 0)
            {
                Status = "等待 " + (requestedSlot + 1) + " 号位切换确认";
                if (now < switchDeadline)
                {
                    if (routineSwitchRequested && !pendingIntro && hud.ConcertoFull)
                    {
                        pendingIntro = pendingFull = true;
                        requestedSlot = NextSlot(hud, now);
                        retrySwitchAt = now;
                        if (requestedSlot < 0)
                        {
                            routineSwitchRequested = false;
                            return AttackWhenSwitchUnavailable(now);
                        }
                    }
                    if ((hud.Locked || cloud && hud.EnemyHealth || timedCombat || opening || characterGrace) &&
                        now >= retrySwitchAt)
                        return routineSwitchRequested ? RoutineSwitch(hud, now, requestedSlot) : BattleAction.Switch(requestedSlot);
                    return default(BattleAction);
                }
                if (routineSwitchRequested)
                {
                    FinishBattle(now);
                    Status = "切换超时，重新确认战斗";
                    return default(BattleAction);
                }
                blockedSwitch[requestedSlot] = now + 4000;
                requestedSlot = -1;
                routineSwitchRequested = false;
            }

            Status = "战斗中";
            if (now < nextAction)
                return default(BattleAction);

            CharacterKit kit = KitAt(hud, activeSlot);
            double health = hud.Health[activeSlot];
            bool healerOpener = openerPending && (supportOpener ?? settings.SupportOpener);
            if (settings.RotateParty && (settings.SwitchAtLowHealth && health < .32 || healerOpener) &&
                RoleAt(hud, activeSlot) != BattleRole.Support)
            {
                int support = FindSupport(hud, activeSlot, now, !healerOpener);
                if (support >= 0)
                {
                    openerPending = false;
                    EndTurn();
                    BattleAction switchAction = BattleAction.Switch(support);
                    switchAction.HelperSwitch = true;
                    return switchAction;
                }
            }
            openerPending = false;

            if (kit != null)
            {
                if (now < noSwitchAttackUntil) return BattleAction.Attack();
                if (planningRoutineSwitch)
                    return ChooseRoutineSwitch(hud, now);
                if (turn == null && now >= restartTurn)
                {
                    FighterMemory memory = roster[activeSlot];
                    memory.LastTurn = now;
                    turn = new CharacterTurn(kit, new TurnContext(memory, roster, settings, entryIntro, entryOutro,
                        now, entrySubDpsIntro, MustMakeRoomForTeammate, clock, () => executionPending, now - battleStart < 400,
                        battleLiberation, openWorldAutoCombat, farmEchoTask));
                }
                return turn == null ? default(BattleAction) : ObserveTurn(hud, now);
            }

            Rotation current = GetRotation(hud, activeSlot);
            long onField = now - fieldStart;
            if (settings.RotateParty && onField >= current.FieldSeconds * 1000 &&
                (!settings.PreferConcerto || hud.ConcertoFull || onField >= current.FieldSeconds * 1000 + 2000))
            {
                if (roster[activeSlot].HasToolBox) return BattleAction.Press(BattleButton.Tool, settings.ToolKey[0], false);
                int next = NextSlot(hud, now);
                if (next >= 0)
                    return BattleAction.Switch(next);
            }

            foreach (Skill skill in current.Priority())
            {
                if (skill == Skill.Liberation && !battleLiberation)
                    continue;
                if (readyFrames[(int)skill] >= 2 && now >= nextCast[activeSlot, (int)skill])
                    return BattleAction.Cast(skill, hud.Keys[(int)skill]);
            }
            return BattleAction.Attack();
        }

        internal BattleAction ObserveTaskAfterBattle(HudSnapshot hud, long now)
        {
            if (!hud.Valid)
            {
                ExpireRecovery(now);
                return default(BattleAction);
            }
            IdentifyTeam(hud, now);
            BattleAction cleanup;
            return ObserveRecovery(hud, now, out cleanup) ? cleanup : default(BattleAction);
        }

        internal void Sent(BattleAction action, long now)
        {
            if (action.Kind == ActionKind.Button && action.Button == BattleButton.Menu)
            {
                layoutKeysSent++;
                layoutStepAt = now + (layoutKeysSent == 1 ? 2000 : 1500);
                return;
            }
            if (action.ConsumesExecution) executionPending = false;
            if (action.DuringAnimation)
            {
                if (turn != null) turn.AnimationInputSent(action, now);
                return;
            }
            if (action.Kind == ActionKind.Button && action.Button == BattleButton.Tool)
            {
                if (activeSlot >= 0) roster[activeSlot].HasToolBox = false;
                nextAction = now + 100;
                return;
            }
            if (action.Kind == ActionKind.Button || action.Kind == ActionKind.Hold || action.Kind == ActionKind.Release)
            {
                CharacterTurn activeTurn = switchExecution ?? turn;
                if (activeTurn != null) activeTurn.Sent(action, now);
                return;
            }
            switch (action.Kind)
            {
                case ActionKind.Target:
                    nextTarget = now + (cloud ? 800 : 200);
                    break;
                case ActionKind.Attack:
                    nextAction = now + (now < noSwitchAttackUntil || planningRoutineSwitch || routineSwitchRequested ? 100 : 155);
                    break;
                case ActionKind.Cast:
                    nextCast[activeSlot, (int)action.Skill] = now + 1550;
                    if (action.Skill == Skill.Liberation)
                        liberationGraceUntil = now + 2600;
                    switch (action.Skill)
                    {
                        case Skill.Liberation: nextAction = now + 950; break;
                        case Skill.Echo: nextAction = now + 440; break;
                        default: nextAction = now + 260; break;
                    }
                    break;
                case ActionKind.Switch:
                    if (activeSlot < 0 && recoverySlot == action.Slot)
                    {
                        recoverySentAt = now;
                        nextAction = now + 200;
                        break;
                    }
                    if (requestedSlot == action.Slot)
                    {
                        retrySwitchAt = now + (routineSwitchRequested ? 100 : helperSwitchRequested ? 200 : 270);
                        break;
                    }
                    pendingIntro = false;
                    pendingFull = false;
                    pendingOutro = null;
                    if (activeSlot >= 0 && previous != null)
                    {
                        FighterMemory leaving = roster[activeSlot];
                        pendingFull = previous.ConcertoFull;
                        pendingIntro = pendingFull || leaving.FreeIntro;
                        pendingOutro = leaving.Id;
                        pendingOutroRole = RoleAt(previous, activeSlot);
                    }
                    requestedSlot = action.Slot;
                    routineSwitchRequested = action.FollowWithAttack;
                    helperSwitchRequested = action.HelperSwitch;
                    planningRoutineSwitch = false;
                    bool known = activeSlot >= 0 && roster[activeSlot].Id != null;
                    missingPartySwitchDeadline = now + (helperSwitchRequested ? 6000 : SwitchTimeoutMs);
                    switchDeadline = now + (routineSwitchRequested ? VisiblePartySwitchTimeoutMs :
                        helperSwitchRequested ? 6000 : known ? SwitchTimeoutMs : 1050);
                    retrySwitchAt = now + (routineSwitchRequested ? 100 : helperSwitchRequested ? 200 : known ? 270 : 1050);
                    nextAction = now + (routineSwitchRequested ? 0 : helperSwitchRequested ? 200 : 220);
                    EndTurn();
                    break;
            }
        }

        internal bool KeepAnimationHold(HudSnapshot hud, long now)
        {
            return hud.TeamHudHidden && turn != null && turn.KeepAnimationHold(now);
        }

        internal void Invalidate(long now)
        {
            CombatAborted = false;
            EndTurn();
            ClearSwitchExecution(now);
            planningRoutineSwitch = routineSwitchRequested = helperSwitchRequested = false;
            requestedSlot = -1;
            ResetRecovery();
            ResetLockConfirmation();
            healthFrames = 0;
            acquireUntil = acquireRetryAt = 0;
            ResetChallenge();
            cloudHealthLostAt = -1;
            ResetRetarget();
            ResetTargetLayout();
            ExpireBattle(now);
        }

        private void ResetLockConfirmation()
        {
            lockedFrames = 0;
            sameSlotFrames = 0;
            Array.Clear(readyFrames, 0, readyFrames.Length);
        }

        private void ExpireBattle(long now, bool recover = false)
        {
            if (activeSlot >= 0 && now - lastLock >= LostCombatTimeoutMs)
            {
                if (recover && targetForTask && settings.RotateParty && previous != null)
                    BeginRecovery(previous, now);
                FinishBattle(now);
            }
        }

        private void ResetRetarget()
        {
            retargetDeadline = 0;
            retargetHealthLostAt = -1;
        }

        private BattleAction AcquireTarget(HudSnapshot hud, long now)
        {
            if (!settings.AutoTarget && !targetForTask)
            {
                acquireUntil = 0;
                healthFrames = 0;
                Status = "等待锁定敌人";
                return default(BattleAction);
            }
            healthFrames = hud.EnemyHealth ? Math.Min(healthFrames + 1, 2) : 0;
            if (acquireUntil > 0 && now >= acquireUntil)
            {
                acquireUntil = 0;
                acquireRetryAt = now + 100;
                Status = "锁敌超时，等待下一次检测";
                return default(BattleAction);
            }
            if (acquireUntil == 0 && healthFrames >= 2 && now >= acquireRetryAt) acquireUntil = now + 3000;
            Status = acquireUntil > 0 ? "发现敌人，等待锁定" : "等待锁定敌人";
            return acquireUntil > 0 && now >= nextTarget && now >= nextAction ? BattleAction.Target() : default(BattleAction);
        }

        internal void CancelChallengePrompt() { challengeObservation.Clear(); }

        private void ResetChallenge()
        {
            challengeObservation.Clear();
            lastChallengeTimer = -1;
            abyssTimerSeconds = double.NaN;
            challengeStartUntil = 0;
            challengeActive = false;
        }

        private bool ChallengeClockActive(long now)
        {
            return challengeActive && lastChallengeTimer >= 0 && now - lastChallengeTimer < 400 &&
                abyssTimerSeconds > 0;
        }

        private bool TryRecoverTargetLayout(HudSnapshot hud, long now, out BattleAction action)
        {
            action = default(BattleAction);
            if (layoutKeysSent == 2) return now < layoutStepAt;
            if (layoutKeysSent == 1)
            {
                if (now >= layoutStepAt) action = BattleAction.Press(BattleButton.Menu, 0x1b, false);
                return true;
            }
            if (!hud.Valid || !hud.BearTargetShifted)
            {
                shiftedTargetSince = -1;
                return false;
            }
            if (shiftedTargetSince < 0) shiftedTargetSince = now;
            if (now - shiftedTargetSince >= 1000) action = BattleAction.Press(BattleButton.Menu, 0x1b, false);
            return true;
        }

        private void ResetTargetLayout()
        {
            shiftedTargetSince = -1;
            layoutStepAt = 0;
            layoutKeysSent = 0;
        }

        private void FinishBattle(long now)
        {
            EndTurn();
            ClearSwitchExecution(now);
            planningRoutineSwitch = routineSwitchRequested = helperSwitchRequested = false;
            activeSlot = -1;
            requestedSlot = -1;
            previous = null;
            executionPending = false;
            restartTurn = 0;
            noSwitchAttackUntil = 0;
            ResetRetarget();
            ResetChallenge();
            ResetTargetLayout();
            foreach (FighterMemory fighter in roster) fighter.ResetForNextBattle();
            Array.Clear(nextCast, 0, nextCast.Length);
            Array.Clear(blockedSwitch, 0, blockedSwitch.Length);
        }

        private BattleAction ObserveTurn(HudSnapshot hud, long now)
        {
            BattleAction action = turn.Observe(hud, now);
            if (turn.Context.CombatExitReason != null)
                return AbortBattle(hud, now, turn.Context.CombatExitReason);
            if (!turn.Finished)
                return action;
            bool combatVisible = hud.Locked || cloud && hud.EnemyHealth || now < challengeStartUntil ||
                ChallengeClockActive(now) || HasCharacterGrace(hud, now);
            EndTurn();
            restartTurn = now;
            if (settings.RotateParty && combatVisible)
            {
                planningRoutineSwitch = true;
                recheckConcertoAt = !hud.ConcertoFull && !roster[activeSlot].FreeIntro && hud.Concerto > .8 ? now + 50 : now;
                return ChooseRoutineSwitch(hud, now);
            }
            roster[activeSlot].FreeIntro = false;
            restartTurn = now + 370;
            return combatVisible ? BattleAction.Attack() : default(BattleAction);
        }

        private BattleAction AbortBattle(HudSnapshot hud, long now, string reason)
        {
            RunLog.Warn("Battle", reason);
            if (hud != null && hud.Valid && settings.RotateParty) BeginRecovery(hud, now);
            FinishBattle(now);
            CombatAborted = true;
            ResetLockConfirmation();
            Status = reason;
            return default(BattleAction);
        }

        private bool HasCharacterGrace(HudSnapshot hud, long now)
        {
            if (activeSlot < 0 || hud.Current != activeSlot) return false;
            FighterMemory actor = roster[activeSlot];
            if (actor.Id == "Phrolova" || actor.Id == "Ciaccona") return clock.Elapsed(actor.LastLiberation, now) < 2000;
            return actor.Id == "ShoreKeeper" && (entryIntro || turn != null && turn.Context.Intro || hud.Airborne);
        }

        private BattleAction ChooseRoutineSwitch(HudSnapshot hud, long now)
        {
            if (hud.LucillaTransform && hud.Health[0] <= 0 && hud.Health[1] <= 0 && hud.Health[2] <= 0)
                return default(BattleAction);
            if (roster[activeSlot].HasToolBox) return BattleAction.Press(BattleButton.Tool, settings.ToolKey[0], false);
            if (now < recheckConcertoAt) return default(BattleAction);
            int next = NextSlot(hud, now);
            if (next >= 0) return RoutineSwitch(hud, now, next);
            return AttackWhenSwitchUnavailable(now);
        }

        private BattleAction AttackWhenSwitchUnavailable(long now)
        {
            planningRoutineSwitch = false;
            roster[activeSlot].FreeIntro = false;
            noSwitchAttackUntil = now + 200;
            return BattleAction.Attack();
        }

        private BattleAction RoutineSwitch(HudSnapshot hud, long now, int next)
        {
            FighterMemory leaving = roster[activeSlot];
            bool intro = hud.ConcertoFull || leaving.FreeIntro || routineSwitchRequested && pendingIntro;
            if (intro &&
                executionPending && BeginSwitchExecution(leaving, now, false))
            {
                Status = "换人前处决";
                return switchExecution.Observe(hud, now);
            }
            FighterMemory incoming = roster[next];
            if (intro && (incoming.Id == "Zani" && incoming.ZaniTimeLeft(clock, now) >= 2000 &&
                clock.Elapsed(incoming.ZaniNightfallAt, now, true) < 2200 || incoming.Id == "Carlotta" &&
                clock.Elapsed(incoming.CarlottaSwitchAt, now, true) < 2500))
                return now >= nextAction ? BattleAction.Attack() : default(BattleAction);
            return BattleAction.Switch(next, true);
        }

        private bool BeginSwitchExecution(FighterMemory actor, long now, bool afterSwitch)
        {
            int slot = Array.IndexOf(roster, actor);
            CharacterKit kit = slot < 0 || previous == null ? CharacterRoster.Find(actor.Id) : KitAt(previous, slot);
            if (!Rotations.AllowsSwitchExecution(kit, actor, settings, clock, now, afterSwitch)) return false;
            ClearSwitchExecution(now);
            if (afterSwitch) delayedSwitchExit = actor;
            var context = new TurnContext(actor, roster, settings, false, null, now, clock: clock,
                pendingExecution: () => executionPending, openWorldAutoCombat: openWorldAutoCombat,
                farmEchoTask: farmEchoTask);
            switchExecution = new CharacterTurn(Rotations.BreakEnemy(context), context);
            return true;
        }

        private void ObserveBreakPrompt(HudSnapshot hud, long now)
        {
            if (executionPending) return;
            if (hud.Shows("f_break_full")) executionPending = true;
            else if (ShouldScanExecution(now) && (hud.OrdinaryBreakSampled || hud.BreakPrompt))
            {
                lastBreakCheck = now;
                executionPending = hud.BreakPrompt;
            }
        }

        private void ClearSwitchExecution(long now)
        {
            if (switchExecution == null) return;
            switchExecution.Dispose();
            switchExecution = null;
            if (delayedSwitchExit != null)
            {
                delayedSwitchExit.LastExit = now;
                delayedSwitchExit = null;
            }
        }

        private void EndTurn()
        {
            if (turn == null) return;
            turn.Dispose();
            turn = null;
            releaseRequested = true;
        }

        internal bool TakeReleaseRequest()
        {
            bool previousRequest = releaseRequested;
            releaseRequested = false;
            return previousRequest;
        }

        internal void HoldsCanceled(long now)
        {
            if (turn != null) turn.ReleasedByWindow(now);
        }

        internal void CancelBlockedSwitch(long now)
        {
            if (requestedSlot < 0) return;
            blockedSwitch[requestedSlot] = now + 4000;
            requestedSlot = -1;
            planningRoutineSwitch = routineSwitchRequested = helperSwitchRequested = false;
            ClearSwitchExecution(now);
        }

        private void IdentifyTeam(HudSnapshot hud, long now)
        {
            bool partyChanged = !knownParty;
            knownParty = true;
            for (int slot = 0; slot < roster.Length; slot++)
            {
                if (hud.PartySize > 0 && slot >= hud.PartySize)
                {
                    if (roster[slot].Id != null)
                    {
                        roster[slot] = new FighterMemory();
                        partyChanged = true;
                    }
                    continue;
                }
                string id = hud.Fighters[slot];
                if (id == null) continue;
                if (roster[slot].Id != id)
                {
                    roster[slot] = new FighterMemory { Id = id };
                    partyChanged = true;
                    if (slot == activeSlot)
                    {
                        EndTurn();
                        ClearSwitchExecution(now);
                        planningRoutineSwitch = false;
                    }
                }
            }
            if (hud.Current >= 0 && roster[hud.Current].Id == "Rover" && hud.RoverForm != null)
            {
                FighterMemory rover = roster[hud.Current];
                // Exploration can change the selected form. Lock it only when
                // combat actually starts, then keep it for the whole battle.
                if (activeSlot >= 0 && rover.RoverForm == null) rover.RoverForm = hud.RoverForm;
                else if (activeSlot < 0 && rover.RoverForm == null) rover.RoverLastKnownForm = hud.RoverForm;
            }
            if (partyChanged)
                RunLog.Info("Battle", "队伍识别 · " + string.Join("、", roster.Take(PartySlots(hud))
                    .Select((fighter, slot) => hud.Profiles[slot] != null ? hud.Profiles[slot].Name :
                        (CharacterRoster.Find(fighter.Id) ?? CharacterRoster.Unrecognized).Name)));
        }

        private int FindSupport(HudSnapshot hud, int origin, long now, bool obeyLock)
        {
            for (int candidate = 0; candidate < PartySlots(hud); candidate++)
                if (candidate != origin && hud.Health[candidate] > (obeyLock ? .20 : 0) && RoleAt(hud, candidate) == BattleRole.Support &&
                    (!obeyLock || now >= blockedSwitch[candidate] &&
                        HealerLockRemaining(candidate, now) <= 0))
                    return candidate;
            return -1;
        }

        private void ResetRecovery()
        {
            recoverySlot = recoveryOrigin = -1;
            recoverySentAt = -1;
            recoveryNeedsHealer = recoveryRepeats = false;
        }

        private void ExpireRecovery(long now)
        {
            if (!RecoveringAfterBattle || now < recoveryDeadline) return;
            if (recoverySlot >= 0 && recoveryNeedsHealer)
            {
                recoverySlot = -1;
                recoverySentAt = -1;
                recoveryDeadline = now + 6000;
            }
            else ResetRecovery();
        }

        private void BeginRecovery(HudSnapshot hud, long now)
        {
            ResetRecovery();
            recoveryDeadline = now + 6000;
            recoveryNeedsHealer = supportOpener ?? settings.SupportOpener;
            FighterMemory current = roster[hud.Current];
            recoveryOrigin = hud.Current;
            bool helperHandoff = targetForTask && (current.Id == "Camellya" || current.Id == "Iuno" ||
                current.Id == "Linnai" || current.Id == "Mornye" || current.Id == "Aemeath" ||
                current.Id == "Ciaccona" && current.InRecital);
            recoveryRepeats = helperHandoff || current.Id == "Cartethyia" && current.CartethyiaTransformed;
            if (!recoveryRepeats && current.Id != "Augusta" && current.Id != "Cantarella") return;
            if (helperHandoff)
            {
                recoverySlot = FindSupport(hud, hud.Current, now, false);
                if (recoverySlot >= 0) return;
            }
            for (int offset = 1; offset < roster.Length; offset++)
            {
                int slot = (hud.Current + offset) % roster.Length;
                if (slot >= PartySlots(hud) || hud.Health[slot] <= 0) continue;
                recoverySlot = slot;
                return;
            }
        }

        private bool ObserveRecovery(HudSnapshot hud, long now, out BattleAction action)
        {
            action = default(BattleAction);
            bool switched = hud.Current != recoveryOrigin;
            if (recoverySlot >= 0 && !switched && now < recoveryDeadline &&
                (recoveryRepeats || recoverySentAt < 0 || targetForTask))
            {
                if (now >= nextAction && (recoveryRepeats || recoverySentAt < 0))
                    action = BattleAction.Switch(recoverySlot);
                return true;
            }
            if (recoverySentAt >= 0 && now < nextAction) return true;
            bool chooseHealer = recoveryNeedsHealer && RoleAt(hud, hud.Current) != BattleRole.Support;
            ResetRecovery();
            if (!chooseHealer) return false;
            recoverySlot = FindSupport(hud, hud.Current, now, false);
            if (recoverySlot < 0) return false;
            recoveryOrigin = hud.Current;
            recoveryRepeats = true;
            recoveryDeadline = now + 6000;
            if (now >= nextAction) action = BattleAction.Switch(recoverySlot);
            return true;
        }

        private BattleRole RoleAt(HudSnapshot hud, int slot)
        {
            CharacterKit kit = KitAt(hud, slot);
            if (kit != null)
            {
                if (kit.Id == "Rover")
                {
                    FighterMemory rover = roster[slot];
                    kit = CharacterRoster.Find(rover.RoverForm ?? rover.RoverLastKnownForm) ??
                        CharacterRoster.Find("RoverElectro");
                }
                return kit.RoleFor(settings);
            }
            return GetRotation(hud, slot).Role;
        }

        private CharacterKit KitAt(HudSnapshot hud, int slot)
        {
            CharacterKit kit = CharacterRoster.Find(hud.Fighters[slot] ?? roster[slot].Id);
            if (kit != null || hud.Profiles[slot] != null) return kit;
            Rotation assigned = settings.Slots[slot];
            Rotation defaults = Rotation.ForRole((BattleRole)slot);
            return assigned.Role == defaults.Role && assigned.FieldSeconds == defaults.FieldSeconds && assigned.Order == defaults.Order ?
                CharacterRoster.Unrecognized : null;
        }

        private Rotation GetRotation(HudSnapshot hud, int slot)
        {
            CharacterProfile profile = hud.Profiles[slot];
            return profile != null ? profile.Rotation : settings.Slots[slot];
        }

        private static int PartySlots(HudSnapshot hud)
        {
            return hud.PartySize > 0 ? Math.Min(hud.Health.Length, hud.PartySize) : hud.Health.Length;
        }

        private int NextSlot(HudSnapshot hud, long now)
        {
            bool intro = hud.ConcertoFull || roster[activeSlot].FreeIntro;
            int best = int.MinValue;
            var eligible = new List<int>(2);
            var healerLocked = new List<int>(2);
            for (int slot = 0; slot < PartySlots(hud); slot++)
            {
                if (slot == activeSlot || hud.Health[slot] <= 0 && !hud.HealthUnknown[slot] ||
                    roster[slot].Id == null && now < blockedSwitch[slot])
                    continue;
                int priority = SwitchingPriority(hud, slot, now, intro);
                if (priority <= SwitchBlocked) continue;
                if (RoleAt(hud, slot) == BattleRole.Support && HealerLockRemaining(slot, now) > 0)
                {
                    healerLocked.Add(slot);
                    continue;
                }
                AddPriorityCandidate(eligible, ref best, slot, priority);
            }
            if (eligible.Count == 0)
                return healerLocked.Count > 0 && (intro || RoleAt(hud, activeSlot) != BattleRole.Support) ?
                    healerLocked.OrderBy(s => HealerLockRemaining(s, now)).ThenBy(s => roster[s].LastEntry).First() : -1;
            if (eligible.Count == 1) return eligible[0];
            if (KitAt(hud, activeSlot) == null && eligible.All(s => KitAt(hud, s) == null))
            {
                int origin = SlotOrder(hud, activeSlot);
                return eligible.OrderBy(s => (SlotOrder(hud, s) - origin + 9) % 9).First();
            }
            if (intro && best >= SwitchRequired) return eligible.OrderBy(s => roster[s].LastEntry).First();
            if (!intro && eligible.Exists(s => clock.Elapsed(roster[s].LastExit, now) > 1000))
                eligible.RemoveAll(s => clock.Elapsed(roster[s].LastExit, now) <= 1000);
            if (!intro && RoleAt(hud, activeSlot) == BattleRole.Main)
            {
                var buffers = eligible.Where(s => RoleAt(hud, s) != BattleRole.Main)
                    .OrderBy(s => roster[s].BuffRemaining(clock, now)).ThenBy(s => roster[s].LastEntry);
                foreach (int buffer in buffers) return buffer;
            }
            var unbuffed = eligible.Where(s => RoleAt(hud, s) != BattleRole.Main && roster[s].BuffRemaining(clock, now) <= 0);
            if (PreferMainAfterHealer && !intro && RoleAt(hud, activeSlot) == BattleRole.Support)
                unbuffed = unbuffed.Where(s => RoleAt(hud, s) != BattleRole.Support);
            if (intro)
            {
                foreach (int buffer in unbuffed.OrderByDescending(s => RoleAt(hud, s)).ThenBy(s => roster[s].LastEntry)) return buffer;
                return eligible.OrderBy(s => RoleAt(hud, s)).ThenBy(s => roster[s].LastEntry).First();
            }
            if (RoleAt(hud, activeSlot) != BattleRole.Main)
                foreach (int buffer in unbuffed.OrderBy(s => roster[s].LastEntry)) return buffer;
            return eligible.OrderBy(s => RoleAt(hud, s) == BattleRole.Main ? 0 : 1).ThenBy(s => roster[s].LastEntry).First();
        }

        internal static void AddPriorityCandidate(List<int> eligible, ref int best, int slot, int priority)
        {
            if (priority <= SwitchBlocked || priority < best) return;
            if (priority > best) eligible.Clear();
            best = priority;
            eligible.Add(slot);
        }

        private long HealerLockRemaining(int slot, long now)
        {
            return 16000 - clock.Elapsed(roster[slot].LastFullHealerSwitch, now);
        }

        private int SwitchingPriority(HudSnapshot hud, int slot, long now, bool intro)
        {
            FighterMemory candidate = roster[slot];
            string from = roster[activeSlot].Id;
            string id = candidate.Id;
            if (id == null) return SwitchNormal;
            FighterMemory cartethyia = null;
            FighterMemory carlotta = null;
            bool linnai = false;
            foreach (FighterMemory fighter in roster)
            {
                if (fighter.Id == "Cartethyia") cartethyia = fighter;
                if (fighter.Id == "Carlotta") carlotta = fighter;
                linnai |= fighter.Id == "Linnai";
            }
            switch (id)
            {
                case "Jinhsi": return intro || candidate.Phase > 0 ? SwitchRequired : SwitchBlocked;
                case "Ciaccona":
                    if (candidate.InRecital)
                    {
                        string partner = Rotations.CiacconaPartner(roster);
                        long recitalTime = clock.Elapsed(candidate.LastLiberation, now);
                        if (partner == "Cartethyia" && (recitalTime < 8000 || cartethyia.CartethyiaTransformed) ||
                            (partner == "Phoebe" || partner == "Zani") && recitalTime < 20000)
                            return SwitchBlocked;
                    }
                    break;
                case "Denia":
                    bool buffed = candidate.BuffRemaining(clock, now) > 0;
                    if (intro && (buffed || from != "Aemeath" && from != "Qingxiao")) return SwitchBlocked + 1;
                    if (!intro && buffed) return SwitchLow;
                    break;
                case "Douling":
                    if (clock.Elapsed(candidate.LastTurn, now) < 8000) return SwitchBlocked;
                    break;
                case "Encore":
                    if (clock.Elapsed(candidate.LastHeavy, now, true) < 4600) return SwitchBlocked;
                    if (clock.Elapsed(candidate.LastLiberation, now) < 9500 || clock.Elapsed(candidate.EncoreStageAt, now, true) < 2000)
                        return SwitchRequired;
                    break;
                case "Brant":
                    if (clock.Elapsed(candidate.BrantAnchor, now, true) < 4000) return SwitchBlocked;
                    if (clock.Elapsed(candidate.LastLiberation, now) < 12000) return SwitchRequired;
                    break;
                case "Cartethyia":
                    if (candidate.CartethyiaTransformed) return SwitchRequired;
                    break;
                case "Lupa":
                    if (clock.Elapsed(candidate.LastLiberation, now) < 12000) return SwitchRequired;
                    break;
                case "Zani":
                    if (candidate.ZaniLiberated) return SwitchRequired;
                    if (intro && clock.Elapsed(candidate.ZaniCrisisAt, now, true) < 1600) return SwitchBlocked;
                    break;
                case "Linnai":
                    if (from == "Mornye") return SwitchRequired;
                    break;
                case "Phoebe":
                    if (!intro && clock.Elapsed(candidate.LastOutro, now, true) < 4500) return SwitchBlocked;
                    break;
                case "Phrolova":
                    long sinceLiberation = clock.Elapsed(candidate.LastLiberation, now);
                    if (intro && from == "Cantarella" && sinceLiberation > 14000) return SwitchRequired;
                    if (sinceLiberation < 24000) return SwitchBlocked;
                    break;
                case "Suisui":
                    long sinceForte = clock.Elapsed(candidate.LastFullHealerSwitch, now);
                    if (sinceForte > 40000) return SwitchRequired;
                    if (sinceForte < 16000) return SwitchBlocked;
                    break;
                case "Zhezhi":
                    if (!farmEchoTask && carlotta != null && carlotta.CarlottaReady(clock, now)) return SwitchRequired;
                    break;
            }
            if (intro)
            {
                if (id == "Brant" && from == "Lupa" || id == "Lupa" && from == "Changli" ||
                    id == "Changli" && from == "Brant" || id == "Camellya" ||
                    id == "Hiyuki" && (from == "Linnai" || from == "Lucilla") ||
                    id == "Verina" && from == "Hiyuki" || id == "Carlotta" && from == "Zhezhi" ||
                    id == "Cantarella" && (from == "Roccia" || from == "Sanhua") ||
                    id == "Lucy" && from == "Rebecca" || id == "Luhesi" && now - candidate.LastIntro > 24000 ||
                    id == "Mornye" && (from == "Aemeath" || from == "Qingxiao" || linnai && from != "Linnai") ||
                    id == "ShoreKeeper" && from == "Augusta" || id == "Suisui" && RoleAt(hud, activeSlot) == BattleRole.Main ||
                    id == "Lucilla" && (from == "Verina" || from == "ShoreKeeper"))
                    return SwitchRequired;
            }
            return SwitchNormal;
        }

        private bool MustMakeRoomForTeammate(HudSnapshot hud, long now)
        {
            if (!settings.RotateParty) return false;
            for (int slot = 0; slot < PartySlots(hud); slot++)
                if (slot != activeSlot && (hud.Health[slot] > 0 || hud.HealthUnknown[slot]) &&
                    SwitchingPriority(hud, slot, now, false) >= SwitchRequired)
                    return true;
            return false;
        }

        private int SlotOrder(HudSnapshot hud, int slot)
        {
            switch (RoleAt(hud, slot))
            {
                case BattleRole.Support: return 3 + slot;
                case BattleRole.Secondary: return 6 + slot;
                default: return slot;
            }
        }
    }
}
