using System;
using System.Collections.Generic;

namespace TideCombat
{
    internal enum BattleButton { Attack, Resonance, Echo, Liberation, Jump, Dodge, Interact, Forward, Left, Tool, Menu, MiddleClick }
    internal enum MoveType { Tap, Repeat, Hold, Press, Release, Wait, CastLiberation, HoldLiberation, CastResonance, CastEcho, Incarnation }
    [Flags]
    internal enum AnimationInput { None = 0, Interact = 1, Attack = 2, HoldAttack = 4, InteractAndAttack = Interact | Attack }

    internal sealed class Move
    {
        internal MoveType Type;
        internal BattleButton Button;
        internal int Duration, Settle, Interval, UntilStable, DownMilliseconds;
        internal bool VerifyCast, RequireAnimation;
        internal bool RawLiberation;
        internal bool AttackWhileDimmed, AttackBetweenCasts, RequiresLitResonance;
        internal bool CountHeldLiberationTime;
        internal bool ConsumesExecution, SkipSkillRecord;
        internal bool DetectHiddenRelease;
        internal AnimationInput AnimationActions;
        internal int AnimationDelay, CastTimeout, HeldAnimationTimeout, LockLossProtection;
        internal Func<HudSnapshot, bool> Until;
        internal Func<HudSnapshot, bool> LiberationAvailable;

        internal static Move Tap(BattleButton button, int settle = 100, int downMilliseconds = 0, bool recordSkill = true)
        {
            return new Move { Type = MoveType.Tap, Button = button, Settle = settle,
                DownMilliseconds = downMilliseconds, SkipSkillRecord = !recordSkill };
        }

        internal static Move ExecutionAttack()
        {
            return new Move { Type = MoveType.Tap, Button = BattleButton.Attack, Settle = 100, ConsumesExecution = true };
        }

        internal static Move Liberation(int waitIfDim = 100, AnimationInput animation = AnimationInput.Interact,
            int readyTimeout = 15000, bool attackWhileDimmed = false, bool raw = false,
            Func<HudSnapshot, bool> available = null, int animationDelay = 0)
        {
            return new Move { Type = MoveType.CastLiberation, Button = BattleButton.Liberation,
                Duration = waitIfDim, VerifyCast = true, RequireAnimation = true, AnimationActions = animation,
                CastTimeout = readyTimeout, AttackWhileDimmed = attackWhileDimmed, RawLiberation = raw,
                LiberationAvailable = available, AnimationDelay = animationDelay };
        }

        internal static Move HoldLiberation(int holdLimit)
        {
            return new Move { Type = MoveType.HoldLiberation, Button = BattleButton.Liberation, Duration = holdLimit,
                VerifyCast = true, RequireAnimation = true, AnimationActions = AnimationInput.Attack, RawLiberation = true,
                HeldAnimationTimeout = 10000 };
        }

        internal static Move RepeatHeldLiberation(int limit, int waitForHud, int interval, Func<HudSnapshot, bool> until)
        {
            return new Move { Type = MoveType.HoldLiberation, Button = BattleButton.Liberation, Duration = limit, Until = until,
                Interval = interval, HeldAnimationTimeout = waitForHud, CountHeldLiberationTime = true };
        }

        internal static Move Resonance(int timeOut = 15000, int postSleep = 0, bool sendClick = true,
            bool hasAnimation = false, int animationDelay = 0, bool clickF = true, bool requireLit = false)
        {
            return new Move { Type = MoveType.CastResonance, Button = BattleButton.Resonance, Duration = timeOut,
                Settle = postSleep, AttackBetweenCasts = sendClick, RequiresLitResonance = requireLit,
                VerifyCast = hasAnimation, AnimationDelay = animationDelay,
                AnimationActions = hasAnimation && clickF ? AnimationInput.Interact : AnimationInput.None };
        }

        internal static Move Echo(int timeOut = 1000, int repeatFor = 0)
        {
            return new Move { Type = MoveType.CastEcho, Button = BattleButton.Echo, CastTimeout = timeOut, Duration = repeatFor };
        }

        internal static Move Incarnation()
        {
            return new Move { Type = MoveType.Incarnation, Button = BattleButton.Resonance, Duration = 6000 };
        }

        internal static Move Attacks(int time, Func<HudSnapshot, bool> until = null, int interval = 100)
        {
            return Repeat(BattleButton.Attack, time, until, interval);
        }

        internal static Move Repeat(BattleButton button, int time, Func<HudSnapshot, bool> until = null, int interval = 100)
        {
            return new Move { Type = MoveType.Repeat, Button = button, Duration = time, Until = until, Interval = interval };
        }

        internal static Move RawLiberationPress(int repeatFor = 0, Func<HudSnapshot, bool> until = null)
        {
            Move move = repeatFor > 0 ? Repeat(BattleButton.Liberation, repeatFor, until, 100) : Tap(BattleButton.Liberation, 0);
            move.RawLiberation = true;
            return move;
        }

        internal static Move Hold(BattleButton button, int time, Func<HudSnapshot, bool> until = null, int after = 100,
            int untilStable = 160)
        {
            return new Move { Type = MoveType.Hold, Button = button, Duration = time, Until = until, Settle = after, UntilStable = untilStable };
        }

        internal static Move Press(BattleButton button, int settle = 0)
        {
            return new Move { Type = MoveType.Press, Button = button, Settle = settle };
        }

        internal static Move Release(BattleButton button, int settle = 0)
        {
            return new Move { Type = MoveType.Release, Button = button, Settle = settle };
        }

        internal static Move Wait(int time, Func<HudSnapshot, bool> until = null, int untilStable = 0)
        {
            return new Move { Type = MoveType.Wait, Duration = time, Until = until, UntilStable = untilStable };
        }
    }

    internal sealed class FighterMemory
    {
        internal string Id;
        internal string RoverForm, RoverLastKnownForm;
        internal int Phase;
        internal double Resource;
        internal long FormUntil, LastTurn = -60000, LastHeavy = -60000;
        internal long LastEntry = -60000, LastExit = -60000, LastFullHealerSwitch = -1;
        internal long LastIntro = -60000, LastOutro = -60000, LastDodge = -60000;
        internal long BuffStarted = -1, LastLiberation = -1;
        internal int BuffDuration;
        internal readonly long[] LastSkill = { -60000, -60000, -60000 };
        internal bool FreeIntro, HasToolBox, InRecital;
        internal bool CartethyiaTransformed, CartethyiaTryMidAir;
        internal PhoebeMemory Phoebe;
        internal long CartethyiaResonance = -1, CartethyiaChain = -1;
        internal long EncoreStageAt = -1, BrantAnchor = -1;
        internal long CarlottaSwitchAt = -1, CarlottaResonanceAt = -1;
        internal int CarlottaResonanceCooldown;
        internal long ShoreKeeperOutro = -1;
        internal int ShoreKeeperDodges;
        internal long ZaniLiberationAt = -1, ZaniCrisisAt = -1, ZaniNightfallAt = -1, ZaniDodgeAt = -1, ZaniNormalResonanceAt = -1;
        internal bool ZaniLiberated;
        internal bool LiberationPrimed, ResumeLiberation;
        internal bool? ExecuteWhenSwitching;

        internal void ResetForNextBattle()
        {
            FreeIntro = HasToolBox = false;
            LastFullHealerSwitch = -1;
            switch (Id)
            {
                case "Rover":
                    RoverLastKnownForm = RoverForm ?? RoverLastKnownForm;
                    RoverForm = null;
                    break;
                case "Denia":
                case "Jinhsi":
                case "Changli":
                case "Lupa":
                    Phase = 0;
                    break;
                case "Ciaccona":
                    InRecital = false;
                    break;
                case "Carlotta":
                    CarlottaSwitchAt = -1;
                    Resource = 0;
                    ResumeLiberation = LiberationPrimed = false;
                    break;
                case "Zhezhi":
                    Phase = 0;
                    Resource = 0;
                    break;
                case "Phoebe":
                    if (Phoebe != null)
                    {
                        Phoebe.Mode = 0;
                        Phoebe.Star = Phoebe.FirstRotationDone = false;
                    }
                    break;
                case "Suisui":
                    Resource = 0;
                    break;
            }
        }

        internal bool CarlottaReady(FreezeClock clock, long now)
        {
            return Resource > 2 || Resource > 0 && clock.Elapsed(CarlottaResonanceAt, now) >= CarlottaResonanceCooldown || LiberationPrimed;
        }

        internal long BuffRemaining(FreezeClock clock, long now)
        {
            return Math.Max(0, BuffDuration - clock.Elapsed(BuffStarted, now));
        }

        internal long ZaniTimeLeft(FreezeClock clock, long now)
        {
            return ZaniLiberated ? Math.Max(0, 20000 - clock.Elapsed(ZaniLiberationAt, now)) : 0;
        }
    }

    internal sealed class TurnContext
    {
        internal readonly FighterMemory Memory;
        internal readonly FighterMemory[] Team;
        internal readonly CombatSettings Settings;
        internal readonly bool Intro;
        internal readonly bool SubDpsIntro;
        internal readonly bool FirstEngage;
        internal readonly string Outro;
        internal readonly long Started;
        private readonly Func<HudSnapshot, long, bool> fastPerform;
        private readonly Func<bool> pendingExecution;
        private readonly bool? battleLiberation;
        internal readonly FreezeClock Clock;
        internal HudSnapshot Hud;
        internal long Now;
        internal bool Succeeded;
        internal bool CastTimedOut;
        internal bool AnimationSeen;
        internal bool TeamHiddenDuringMove;
        internal bool AnimationAttackHeld;
        internal long IncarnationFreezeAt = -1;
        internal bool LucillaTransformPending;
        internal long ContinueAfterLockLossUntil;
        internal double Concerto { get { return Hud.ConcertoFull ? 1 : Hud.Concerto; } }
        internal bool Full { get { return Hud.ConcertoFull; } }
        internal bool Forte { get { return Hud.ForteFullColor || Hud.Forte >= 0.96 || Hud.Shows("e_forte") || Hud.Shows("mouse_forte"); } }
        internal bool Long { get { return Hud.LongActionLayout == 1; } }
        internal bool Longer { get { return Hud.LongActionLayout == 2; } }
        internal bool Air { get { return Hud.Airborne; } }
        internal bool HeavyReady { get { return Hud.Shows("mouse_forte"); } }
        internal bool EReady { get { return UsableWithoutCooldown(Skill.Resonance); } }
        internal bool QReady { get { return UsableWithoutCooldown(Skill.Echo); } }
        internal bool RReady { get { return MayUseLiberation && Hud.Keys[2] != '\0' && Hud.Skills[2] == SkillState.Ready; } }
        internal bool MayUseLiberation { get { return battleLiberation ?? (Settings.UseLiberation || Hud.InChallenge || !Hud.OpenWorld); } }
        internal bool WasBuffed { get { return Memory.BuffRemaining(Clock, Now) > 0; } }
        internal bool NeedFastPerform { get { return fastPerform != null && fastPerform(Hud, Now); } }
        internal bool ExecutionAvailable { get { return pendingExecution != null ? pendingExecution() : Hud.BreakPrompt; } }

        internal TurnContext(FighterMemory actor, FighterMemory[] team, CombatSettings settings, bool intro,
            string outro, long start, bool subDpsIntro = false, Func<HudSnapshot, long, bool> fastPerform = null,
            FreezeClock clock = null, Func<bool> pendingExecution = null, bool firstEngage = false, bool? battleLiberation = null)
        {
            Memory = actor;
            Team = team;
            Settings = settings;
            Intro = intro;
            SubDpsIntro = intro && subDpsIntro;
            FirstEngage = firstEngage;
            Outro = outro;
            Started = start;
            this.fastPerform = fastPerform;
            this.pendingExecution = pendingExecution;
            this.battleLiberation = battleLiberation;
            Clock = clock ?? new FreezeClock();
        }

        internal long Since(long timestamp, bool includeIntro = false) { return Clock.Elapsed(timestamp, Now, includeIntro); }

        internal bool Has(string id) { return Hud.Shows(id); }

        internal bool With(string id)
        {
            return Ally(id) != null;
        }

        internal FighterMemory Ally(string id)
        {
            foreach (FighterMemory teammate in Team)
                if (teammate != Memory && teammate.Id == id)
                    return teammate;
            return null;
        }

        internal bool HasRover()
        {
            foreach (FighterMemory teammate in Team)
                if (teammate != Memory && teammate.Id != null && teammate.Id.StartsWith("Rover", StringComparison.Ordinal))
                    return true;
            return false;
        }

        private bool UsableWithoutCooldown(Skill skill)
        {
            int index = (int)skill;
            return Hud.Keys[index] != '\0' && (Hud.Skills[index] == SkillState.Ready || Hud.Skills[index] == SkillState.Unavailable);
        }

        internal static int SkillIndex(BattleButton button)
        {
            switch (button)
            {
                case BattleButton.Resonance: return (int)Skill.Resonance;
                case BattleButton.Echo: return (int)Skill.Echo;
                case BattleButton.Liberation: return (int)Skill.Liberation;
                default: return -1;
            }
        }

        internal int Key(BattleButton button, bool rawLiberation = false)
        {
            int skill = SkillIndex(button);
            if (skill >= 0)
                return skill == (int)Skill.Liberation && !rawLiberation && !MayUseLiberation ? 0 : Hud.Keys[skill];
            switch (button)
            {
                case BattleButton.Jump: return 0x20;
                case BattleButton.MiddleClick: return 0x04;
                case BattleButton.Interact: return 'F';
                case BattleButton.Forward: return 'W';
                case BattleButton.Left: return 'A';
                default: return 0;
            }
        }
    }

    internal sealed class CharacterTurn : IDisposable
    {
        private readonly IEnumerator<Move> sequence;
        internal readonly TurnContext Context;
        private Move move;
        private long moveStart, sentAt, next, untilSince;
        private long holdDeadline, hiddenSince = -1;
        private long possibleAnimationAt = -1, animationStartedAt = -1;
        private long nextAnimationInteract, nextAnimationAttack;
        private long liberationDimmedAt;
        private long firstResonancePress = -1;
        private long nextHoldRepeat;
        private bool incarnationAttack;
        private int possibleAnimationSkill = -1;
        private bool holdLiberationAnimation;
        private int heldAnimationTimeout;
        private AnimationInput animationActions;
        private int animationDelay;
        private bool activeHold, sent, confirmation, holdSucceeded;
        private bool possibleAnimationRequired, cooldownRecorded, liberationWasReady;
        private int holdKey;
        private readonly Dictionary<BattleButton, int> latched = new Dictionary<BattleButton, int>();
        private readonly Queue<BattleAction> restore = new Queue<BattleAction>();

        internal bool Finished { get; private set; }
        internal bool CanContinueWithoutLock(long now)
        {
            return now < Context.ContinueAfterLockLossUntil ||
                activeHold && move != null && move.Type == MoveType.HoldLiberation && move.CountHeldLiberationTime ||
                move != null && move.Type == MoveType.Incarnation && now - moveStart < move.Duration;
        }

        internal bool AnimationInProgress(long now)
        {
            return animationStartedAt >= 0 && now - animationStartedAt < AnimationTimeout() || confirmation && now - sentAt < 950;
        }

        internal CharacterTurn(CharacterKit fighter, TurnContext context)
            : this(fighter.Play(context), context) { }

        internal CharacterTurn(IEnumerable<Move> moves, TurnContext context)
        {
            Context = context;
            sequence = moves.GetEnumerator();
        }

        internal BattleAction ObserveUnavailable(long now, bool teamHudHidden)
        {
            if (!teamHudHidden) return default(BattleAction);
            if (activeHold && move != null && move.DetectHiddenRelease)
            {
                if (untilSince < 0) untilSince = now;
                holdSucceeded = now - untilSince >= move.UntilStable;
                if (holdSucceeded || now >= holdDeadline) return BattleAction.Release(move.Button, holdKey);
                return default(BattleAction);
            }
            if (move != null && move.Type == MoveType.Wait && now >= moveStart + move.Duration)
                Context.TeamHiddenDuringMove = true;
            if (move != null && move.Type == MoveType.Incarnation)
            {
                if (Context.IncarnationFreezeAt < 0) Context.IncarnationFreezeAt = now;
                return default(BattleAction);
            }
            if (hiddenSince < 0)
                hiddenSince = now;
            bool holdingLiberation = activeHold && move != null && move.Type == MoveType.HoldLiberation;
            if (animationStartedAt < 0 && possibleAnimationAt >= 0 && (now - possibleAnimationAt < 750 || holdingLiberation))
                animationStartedAt = now;
            if (animationStartedAt >= 0 && holdingLiberation)
            {
                Context.AnimationSeen = true;
                return BattleAction.Release(BattleButton.Liberation, holdKey);
            }
            if (sent && move != null && move.VerifyCast && animationStartedAt >= 0)
                Context.AnimationSeen |= move.Type == MoveType.CastResonance || now - hiddenSince >= 75;
            if (animationStartedAt < 0 || now - animationStartedAt >= AnimationTimeout()) return default(BattleAction);
            if ((animationActions & AnimationInput.HoldAttack) != 0 && now - animationStartedAt >= animationDelay &&
                !latched.ContainsKey(BattleButton.Attack))
            {
                BattleAction hold = BattleAction.Press(BattleButton.Attack, 0, true);
                hold.DuringAnimation = true;
                return hold;
            }
            bool interact = (animationActions & AnimationInput.Interact) != 0 &&
                now - animationStartedAt >= animationDelay && now >= nextAnimationInteract;
            bool attack = (animationActions & AnimationInput.Attack) != 0 && now >= nextAnimationAttack;
            return interact || attack ? BattleAction.AnimationPulse(interact, attack) : default(BattleAction);
        }

        internal void AnimationInputSent(BattleAction action, long now)
        {
            if (action.Kind == ActionKind.Hold && action.Button == BattleButton.Attack)
            {
                latched[BattleButton.Attack] = 0;
                Context.AnimationAttackHeld = true;
                return;
            }
            if (action.Button == BattleButton.Interact) nextAnimationInteract = now + 100;
            if (action.Button == BattleButton.Attack || action.AttackBefore) nextAnimationAttack = now + 100;
        }

        internal bool KeepAnimationHold(long now)
        {
            if (activeHold && move != null && move.DetectHiddenRelease) return true;
            return animationStartedAt >= 0 && now - animationStartedAt < AnimationTimeout() &&
                (activeHold && move != null && move.Type == MoveType.HoldLiberation ||
                latched.ContainsKey(BattleButton.Attack) && (Context.Memory.Id == "Camellya" || Context.Memory.Id == "Sanhua" ||
                    Context.Memory.Id == "YangYangSp" || Context.AnimationAttackHeld));
        }

        private int AnimationTimeout()
        {
            return holdLiberationAnimation ? heldAnimationTimeout : possibleAnimationSkill == (int)Skill.Resonance ? 6000 : 7000;
        }

        internal void ObserveAvailable(HudSnapshot hud, long now)
        {
            if (possibleAnimationAt < 0) return;
            if (activeHold && holdLiberationAnimation) return;
            bool sawAnimation = animationStartedAt >= 0 && now - animationStartedAt > 100 && now - animationStartedAt < AnimationTimeout();
            if (sawAnimation)
            {
                bool countHold = move != null && move.Type == MoveType.HoldLiberation && move.CountHeldLiberationTime;
                long freezeStart = countHold ? sentAt : firstResonancePress >= 0 ? firstResonancePress : animationStartedAt;
                Context.Clock.Record(freezeStart, now - (countHold ? sentAt : animationStartedAt), now);
            }
            bool cooldown = !cooldownRecorded && !possibleAnimationRequired &&
                hud.Skills[possibleAnimationSkill] == SkillState.Cooling && now - possibleAnimationAt <= 950;
            if ((sawAnimation || cooldown) && possibleAnimationSkill == (int)Skill.Liberation && !holdLiberationAnimation &&
                (animationActions & AnimationInput.HoldAttack) == 0)
                Context.Memory.LastLiberation = now;
            cooldownRecorded |= cooldown;
            if (sawAnimation || now - possibleAnimationAt > 950)
                ClearAnimationObservation();
            else animationStartedAt = -1;
        }

        private void ClearAnimationObservation()
        {
            possibleAnimationAt = animationStartedAt = -1;
            possibleAnimationSkill = -1;
            firstResonancePress = -1;
        }

        internal void ReleasedByWindow(long now)
        {
            if (activeHold)
            {
                if (move != null && move.Type == MoveType.HoldLiberation)
                {
                    ClearAnimationObservation();
                    FinishMove(false);
                }
                next = Math.Max(next, now + 100);
                untilSince = -1;
            }
            activeHold = false;
            restore.Clear();
            foreach (KeyValuePair<BattleButton, int> button in latched)
                restore.Enqueue(BattleAction.Press(button.Key, button.Value, true));
        }

        internal BattleAction Observe(HudSnapshot hud, long now)
        {
            ObserveAvailable(hud, now);
            Context.Hud = hud;
            Context.Now = now;
            hiddenSince = -1;
            if (Finished || now < next)
                return default(BattleAction);
            if (restore.Count > 0)
                return restore.Peek();

            // A partially visible frame never runs non-release commands.
            for (int skipped = 0; skipped < 32; skipped++)
            {
                if (move == null)
                {
                    if (!sequence.MoveNext())
                    {
                        Finished = true;
                        Context.ContinueAfterLockLossUntil = 0;
                        return default(BattleAction);
                    }
                    move = sequence.Current;
                    moveStart = now;
                    Context.TeamHiddenDuringMove = false;
                    holdDeadline = -1;
                    untilSince = -1;
                    liberationDimmedAt = -1;
                    liberationWasReady = false;
                    Context.AnimationSeen = false;
                    Context.CastTimedOut = false;
                    if (move.Type == MoveType.Incarnation)
                    {
                        Context.IncarnationFreezeAt = -1;
                        incarnationAttack = true;
                    }
                }

                int key = Context.Key(move.Button, move.RawLiberation);
                if (move.Type == MoveType.Incarnation)
                {
                    if (now - moveStart >= move.Duration)
                    {
                        FinishMove(sent);
                        continue;
                    }
                    if (incarnationAttack) return BattleAction.Press(BattleButton.Attack, 0, false);
                    return key != 0 ? BattleAction.Press(move.Button, key, false) : default(BattleAction);
                }
                if (move.Type == MoveType.HoldLiberation)
                {
                    if (activeHold)
                    {
                        if (now >= holdDeadline || move.Until != null && move.Until(hud))
                            return BattleAction.Release(move.Button, holdKey);
                        if (move.Interval > 0 && now >= nextHoldRepeat)
                        {
                            BattleAction reassert = BattleAction.Press(move.Button, holdKey, true);
                            reassert.ReassertHold = true;
                            return reassert;
                        }
                        return default(BattleAction);
                    }
                    if (!sent && key != 0 && (move.Until == null || !move.Until(hud)))
                        return BattleAction.Press(move.Button, key, true);
                    FinishMove(Context.AnimationSeen);
                    continue;
                }
                if (move.Type == MoveType.CastLiberation)
                {
                    BattleAction cast = ObserveLiberation(hud, key, now);
                    if (move == null) continue;
                    return cast;
                }
                if (move.Type == MoveType.CastResonance)
                {
                    BattleAction cast = ObserveResonance(key, now);
                    if (move != null || now < next) return cast;
                    continue;
                }
                if (move.Type == MoveType.CastEcho)
                {
                    BattleAction cast = ObserveEcho(key, now);
                    if (move != null) return cast;
                    continue;
                }
                bool stop = ConditionMet(hud, now);
                bool expired = now - moveStart >= move.Duration;
                if (move.Type == MoveType.Wait)
                {
                    if (!stop && !expired) return default(BattleAction);
                    FinishMove(stop || move.Until == null);
                    continue;
                }
                if (move.Type == MoveType.Release)
                    return BattleAction.Release(move.Button);
                if (move.Type == MoveType.Hold && activeHold)
                {
                    bool holdExpired = now >= holdDeadline;
                    if (!stop && !holdExpired) return default(BattleAction);
                    holdSucceeded = stop || move.Until == null;
                    return BattleAction.Release(move.Button, holdKey);
                }
                if (move.Type == MoveType.Repeat)
                {
                    if (!stop && !expired && CanSend(key))
                        return BattleAction.Press(move.Button, key, false);
                    FinishMove(stop || sent);
                    continue;
                }
                if (!CanSend(key))
                {
                    FinishMove(false);
                    continue;
                }
                if (move.Type == MoveType.Hold && (stop || holdDeadline >= 0 && now >= holdDeadline))
                {
                    FinishMove(holdDeadline >= 0 && (stop || move.Until == null));
                    continue;
                }
                BattleAction action = BattleAction.Press(move.Button, key, move.Type == MoveType.Press || move.Type == MoveType.Hold);
                action.ConsumesExecution = move.ConsumesExecution;
                action.DownMilliseconds = move.DownMilliseconds;
                return action;
            }
            return default(BattleAction);
        }

        private bool CanSend(int key)
        {
            return key != 0 || move.Button == BattleButton.Attack || move.Button == BattleButton.Dodge;
        }

        private BattleAction ObserveResonance(int key, long now)
        {
            SkillState state = Context.Hud.Skills[(int)Skill.Resonance];
            bool ready = key != 0 && (state == SkillState.Ready || state == SkillState.Unavailable);
            long elapsed = now - moveStart;
            bool timedOut = move.Duration > 0 && elapsed >= move.Duration;
            if (Context.AnimationSeen || !ready && (!move.VerifyCast || elapsed >= move.AnimationDelay) ||
                timedOut || move.Duration == 0 && sent)
            {
                Context.CastTimedOut = timedOut && ready && !Context.AnimationSeen;
                if (sent) next = now + move.Settle;
                FinishMove(sent);
                return default(BattleAction);
            }
            if (!ready) return default(BattleAction);
            if (move.RequiresLitResonance && Context.Hud.ResonanceWhiteRatio <= 0)
                return move.AttackBetweenCasts && now >= nextAnimationAttack ? BattleAction.AnimationPulse(false, true) : default(BattleAction);
            BattleAction action = BattleAction.Press(BattleButton.Resonance, key, false);
            action.AttackBefore = sent && move.AttackBetweenCasts;
            return action;
        }

        private BattleAction ObserveEcho(int key, long now)
        {
            SkillState state = Context.Hud.Skills[(int)Skill.Echo];
            bool ready = key != 0 && (state == SkillState.Ready || state == SkillState.Unavailable);
            if (move.CastTimeout == 0 && ready)
                return BattleAction.Press(BattleButton.Echo, key, false);
            if (key == 0 || !Context.Hud.InRealm && CharacterKit.HasFireRing(Context.Memory.Id) ||
                now - moveStart >= move.CastTimeout + move.Duration)
            {
                FinishMove(false);
                return default(BattleAction);
            }
            if (!ready && (!sent || move.Duration == 0) || move.Duration > 0 && now - moveStart >= move.Duration)
            {
                FinishMove(sent);
                return default(BattleAction);
            }
            return BattleAction.Press(BattleButton.Echo, key, false);
        }

        private BattleAction ObserveLiberation(HudSnapshot hud, int key, long now)
        {
            long elapsed = now - moveStart;
            if (Context.AnimationSeen)
            {
                FinishMove(true);
                return default(BattleAction);
            }
            if ((!move.RawLiberation && !Context.MayUseLiberation) || key == 0)
            {
                FinishMove(false);
                return default(BattleAction);
            }

            SkillState current = hud.Skills[(int)Skill.Liberation];
            bool sendClick = (move.AnimationActions & AnimationInput.Attack) != 0;
            if (current == SkillState.Ready && (move.LiberationAvailable == null || move.LiberationAvailable(hud)))
            {
                if (elapsed >= move.CastTimeout)
                {
                    FinishMove(false);
                    return default(BattleAction);
                }
                liberationWasReady = true;
                liberationDimmedAt = -1;
                BattleAction action = BattleAction.Press(BattleButton.Liberation, key, false);
                action.AttackBefore = sendClick && now >= nextAnimationAttack;
                return action;
            }
            if (!liberationWasReady && elapsed < move.Duration && current == SkillState.Unavailable)
                return BattleAction.Press(BattleButton.Liberation, key, false);

            if (sent && liberationDimmedAt < 0) liberationDimmedAt = now;
            if (!sent || !liberationWasReady && now - sentAt >= 100 ||
                liberationWasReady && now - liberationDimmedAt >= 400)
            {
                FinishMove(false);
                return default(BattleAction);
            }
            if (liberationWasReady && (sendClick || move.AttackWhileDimmed) && now >= nextAnimationAttack)
                return BattleAction.AnimationPulse(false, true);
            return default(BattleAction);
        }

        private bool ConditionMet(HudSnapshot hud, long now)
        {
            if (move.Until == null || !move.Until(hud))
            {
                untilSince = -1;
                return false;
            }
            if (move.UntilStable == 0 || move.Type == MoveType.Hold && !activeHold) return true;
            if (untilSince < 0)
                untilSince = now;
            return now - untilSince >= move.UntilStable;
        }

        internal void Sent(BattleAction action, long now)
        {
            if (restore.Count > 0)
            {
                restore.Dequeue();
                return;
            }
            if (move == null)
                return;
            if (move.Type == MoveType.Incarnation)
            {
                sent = true;
                incarnationAttack = !incarnationAttack;
                if (action.Button == BattleButton.Resonance) Context.Memory.LastSkill[0] = now;
                next = now + (action.Button == BattleButton.Attack ? 100 : 50);
                if (Context.IncarnationFreezeAt >= 0) FinishMove(true);
                return;
            }
            if (action.ReassertHold)
            {
                nextHoldRepeat = now + move.Interval;
                return;
            }
            bool first = !sent;
            sent = true;
            if (action.Button == BattleButton.Dodge && action.Kind != ActionKind.Release) Context.Memory.LastDodge = now;
            int skill = TurnContext.SkillIndex(move.Button);
            if (skill >= 0 && !move.SkipSkillRecord && action.Kind != ActionKind.Release &&
                (first || move.Type != MoveType.CastResonance))
                Context.Memory.LastSkill[skill] = now;
            if (action.Kind == ActionKind.Button && (skill == (int)Skill.Liberation ||
                skill == (int)Skill.Resonance && (move.VerifyCast || move.AnimationActions != AnimationInput.None)) ||
                action.Kind == ActionKind.Hold && move.Type == MoveType.HoldLiberation)
            {
                possibleAnimationAt = now;
                possibleAnimationSkill = skill;
                if (first || move.Type != MoveType.CastResonance)
                    firstResonancePress = move.Type == MoveType.CastResonance ? now : -1;
                holdLiberationAnimation = move.Type == MoveType.HoldLiberation;
                heldAnimationTimeout = move.HeldAnimationTimeout;
                possibleAnimationRequired = move.RequireAnimation;
                animationActions = move.AnimationActions;
                animationDelay = move.AnimationDelay;
                cooldownRecorded = false;
                nextAnimationInteract = now;
                nextAnimationAttack = action.AttackBefore ? now + 100 : now;
                animationStartedAt = -1;
            }
            if (move.Type == MoveType.HoldLiberation)
            {
                if (action.Kind == ActionKind.Hold)
                {
                    activeHold = true;
                    holdKey = action.NativeKey;
                    holdDeadline = now + move.Duration;
                    nextHoldRepeat = now + move.Interval;
                    sentAt = now;
                    confirmation = true;
                }
                else
                {
                    activeHold = false;
                    if (!Context.AnimationSeen)
                    {
                        if (move.CountHeldLiberationTime) Context.Clock.Record(sentAt, now - sentAt, now);
                        ClearAnimationObservation();
                        FinishMove(move.CountHeldLiberationTime);
                    }
                }
                return;
            }
            if (move.Type == MoveType.CastLiberation)
            {
                if (first && (move.AnimationActions & AnimationInput.HoldAttack) != 0)
                    Context.Memory.LastLiberation = now;
                confirmation = true;
                sentAt = now;
                next = now + 100;
                return;
            }
            if (move.Type == MoveType.CastResonance)
            {
                sentAt = now;
                confirmation = move.VerifyCast;
                next = now + (move.VerifyCast ? 200 : 100);
                return;
            }
            if (move.Type == MoveType.CastEcho)
            {
                next = now + 100;
                if (move.CastTimeout == 0) FinishMove(true);
                return;
            }
            if (action.Kind == ActionKind.Release && (activeHold || move.Type == MoveType.Release))
            {
                activeHold = false;
                if (move.Type == MoveType.Release)
                {
                    latched.Remove(move.Button);
                    if (move.Button == BattleButton.Attack) Context.AnimationAttackHeld = false;
                }
                next = now + move.Settle;
                FinishMove(move.Type == MoveType.Release || holdSucceeded);
                return;
            }
            if (move.Type == MoveType.Hold)
            {
                activeHold = true;
                holdKey = action.NativeKey;
                if (holdDeadline < 0) holdDeadline = now + move.Duration;
                if (move.LockLossProtection > 0)
                    Context.ContinueAfterLockLossUntil = now + move.LockLossProtection;
                return;
            }
            if (move.Type == MoveType.Press)
                latched[move.Button] = action.NativeKey;
            if (move.Type == MoveType.Repeat)
            {
                next = now + move.Interval;
                return;
            }
            next = now + move.Settle;
            FinishMove(true);
        }

        private void FinishMove(bool success)
        {
            Context.Succeeded = success;
            confirmation = false;
            sent = false;
            move = null;
        }

        public void Dispose()
        {
            Context.ContinueAfterLockLossUntil = 0;
            sequence.Dispose();
        }
    }
}
