using System;
using System.Collections.Generic;

namespace TideCombat
{
    internal static partial class Rotations
    {
        private static bool HasTwoBuffs(TurnContext c)
        {
            int active = 0;
            foreach (FighterMemory ally in c.Team)
                if (ally != c.Memory && ally.BuffRemaining(c.Clock, c.Now) > 0) active++;
            return c.Intro && active == 2;
        }

        internal static IEnumerable<Move> Aemeath(TurnContext c)
        {
            bool mustFinish = HasTwoBuffs(c);
            int partners = 0;
            bool hasSub = false;
            foreach (FighterMemory member in c.Team)
            {
                CharacterKit kit = CharacterRoster.Find(member.Id);
                if (member == c.Memory || kit == null) continue;
                partners++;
                hasSub |= kit.RoleFor(c.Settings) == BattleRole.Secondary;
            }
            if (!mustFinish && (partners != 2 || hasSub))
            {
                while (c.Long)
                {
                    yield return Move.Hold(Attack, 1200, h => h.LongActionLayout != 1, 10, 0);
                    if (c.Succeeded) yield return Move.Wait(300);
                }
                yield break;
            }
            if (c.Intro) yield return Move.Attacks(2100);
            bool enhanced = false;
            long start = c.Now;
            while (c.Since(start) < 12000)
            {
                if (c.Long)
                {
                    yield return Move.Hold(Attack, 1200, h => h.LongActionLayout != 1, 10, 0);
                    if (c.Succeeded)
                    {
                        start = c.Now;
                        if (c.ExecutionAvailable) foreach (Move action in BreakEnemy(c)) yield return action;
                        else yield return Move.Wait(100);
                        continue;
                    }
                }
                bool second = c.Has("aemeath_lib2");
                yield return Move.Liberation(0);
                bool acted = c.Succeeded;
                if (acted && second) yield break;
                if (!acted && (c.Has("aemeath_e1") || c.Has("aemeath_e2")))
                {
                    yield return Move.Resonance(1500, hasAnimation: true, animationDelay: 500);
                    if (c.Succeeded)
                    {
                        enhanced = true;
                        yield return Move.Echo(0);
                    }
                    second = c.Has("aemeath_lib2");
                    yield return Move.Liberation(0);
                    if (c.Succeeded && second) yield break;
                    acted = true;
                }
                if (!acted) yield return Move.Tap(Attack, 100);
                else if (c.Long) start = c.Now;
                else if (!mustFinish && (!c.Intro || enhanced)) yield break;
                yield return Move.Wait(10);
            }
        }

        internal static IEnumerable<Move> Augusta(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1130);
            if (c.Air) foreach (Move move in WaitDown(c)) yield return move;
            int field = c.SubDpsIntro && c.Outro == "Iuno" ? 14000 : 3000;
            long start = c.Now;
            while (c.Now - start < field + 3000)
            {
                if (AugustaLiberationVisible(c.Hud, "Augusta_lib2"))
                {
                    yield return Move.HoldLiberation(600);
                    if (c.Succeeded)
                    {
                        yield return Move.Tap(Echo, 0);
                        yield break;
                    }
                }
                if (c.Air) foreach (Move move in ShoreKeeperDodge(c)) yield return move;
                if (c.Has("target_enemy_long_inner"))
                {
                    yield return Move.Hold(Attack, 2000, h => !h.Shows("target_enemy_long_inner"), 50, 0);
                    if (c.Succeeded)
                    {
                        yield return Move.Attacks(300);
                        if (c.Now - start > field) yield break;
                    }
                }
                if (ResonanceWithoutCooldown(c.Hud))
                {
                    long resonance = c.Now;
                    yield return Move.Resonance();
                    if (c.Now - resonance < 1400)
                    {
                        if (c.Air) continue;
                        yield return Move.Wait(1000, h => h.Shows("target_enemy_long_inner"));
                        if (c.Succeeded)
                        {
                            yield return Move.Hold(Attack, 2000, h => !h.Shows("target_enemy_long_inner"), 50, 0);
                            if (c.Succeeded)
                            {
                                yield return Move.Attacks(300);
                                if (c.Now - start > field && !c.Air) yield break;
                            }
                        }
                    }
                    else if (AugustaLiberationVisible(c.Hud, "Augusta_lib2"))
                    {
                        foreach (Move move in WaitDown(c)) yield return move;
                        yield return Move.HoldLiberation(600);
                        if (c.Succeeded) yield return Move.Tap(Echo, 0);
                        yield break;
                    }
                }
                if (AugustaLiberationVisible(c.Hud, "Augusta_lib1"))
                {
                    yield return Move.RawLiberationPress(2000, h => !AugustaLiberationVisible(h, "Augusta_lib1"));
                    if (!AugustaLiberationVisible(c.Hud, "Augusta_lib1"))
                    {
                        c.Memory.LastLiberation = c.Now;
                        if (field < 14000) yield break;
                    }
                }
                yield return Move.Tap(Attack, 100);
            }
            yield return Move.Tap(Echo, 0);
        }

        private static bool AugustaLiberationVisible(HudSnapshot h, string indicator)
        {
            return h.Shows(indicator) && (h.SkillLit[2] || h.Skills[2] == SkillState.Ready);
        }

        internal static IEnumerable<Move> Chisa(TurnContext c)
        {
            bool main = c.Settings.ChisaDps;
            c.Memory.ExecuteWhenSwitching = main;
            if (!main)
            {
                int duration = c.Intro && !c.WasBuffed ? 10000 : 1200;
                if (c.Intro) yield return Move.Attacks(2000);
                if (c.Air && !ResonanceWithoutCooldown(c.Hud) && !c.RReady)
                    foreach (Move action in WaitDown(c)) yield return action;
                yield return Move.Echo(0);
                long supportStart = c.Now;
                while (c.Since(supportStart) < duration && !c.Full)
                {
                    long cycleStart = c.Now;
                    if (c.RReady) yield return Move.Liberation(0);
                    else if (c.Hud.ForteFullColor)
                    {
                        if (c.Air) foreach (Move action in WaitDown(c)) yield return action;
                        yield return Move.Hold(Skill, 1200, after: 0);
                        if (!c.Hud.ForteFullColor)
                        {
                            yield return Heavy(3500, null, 0);
                            yield break;
                        }
                    }
                    else
                    {
                        yield return Move.Resonance(0);
                        if (!c.Succeeded) yield return Move.Tap(Attack, 100);
                    }
                    if (c.Now - cycleStart < 100) yield return Move.Wait((int)(100 - (c.Now - cycleStart)));
                }
                yield break;
            }
            int timeOut = c.Intro ? 2300 : 2500;
            if (c.Intro) yield return Move.Attacks(800);
            if (c.Air && !ResonanceWithoutCooldown(c.Hud) && !c.RReady)
                foreach (Move action in WaitDown(c)) yield return action;
            yield return Move.Echo();
            long start = c.Now;
            while (c.Now - start < timeOut)
            {
                if (c.Now - start < 500)
                {
                    yield return Move.Liberation();
                    if (c.Succeeded)
                    {
                        start = c.Now;
                        timeOut = 10000;
                        yield return Move.Wait(200);
                    }
                }
                if (c.Now - start < 500 && !c.Hud.ForteFullColor)
                {
                    yield return Move.Resonance();
                    if (c.Succeeded && !c.CastTimedOut)
                    {
                        start = c.Now;
                        if (timeOut != 10000) timeOut = 1700;
                    }
                }
                if (c.Hud.ForteFullColor)
                {
                    if (c.Air) foreach (Move action in WaitDown(c)) yield return action;
                    yield return Move.Hold(Skill, 1200, after: 0);
                    if (!c.Hud.ForteFullColor)
                    {
                        yield return Heavy(3500, null, 0);
                        c.Memory.ExecuteWhenSwitching = false;
                        yield break;
                    }
                }
                yield return Move.Tap(Attack, 100);
            }
        }

        internal static IEnumerable<Move> Denia(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(2000);
            else if (c.Memory.Phase == 1) yield return Move.Attacks(1300);
            int duration = c.Memory.Phase == 1 ? 4400 : 1200;
            long start = c.Now;
            while (c.Since(start) < duration)
            {
                if (c.Memory.Phase == 0 && c.Since(c.Memory.LastLiberation) < 10000 && c.Full) yield break;
                yield return Move.Resonance();
                if (c.Succeeded) { yield return Move.Wait(10); continue; }
                if (c.RReady)
                {
                    bool finishing = c.Has("denia_end_lib");
                    yield return Move.Liberation(0);
                    if (c.Succeeded)
                    {
                        c.Memory.Phase = finishing ? 0 : 1;
                        if (finishing)
                        {
                            yield return Move.Echo(0);
                        }
                        else yield return Move.Attacks(1900);
                        yield break;
                    }
                }
                else yield return Move.Tap(Attack, 100);
                yield return Move.Wait(10);
            }
        }

        internal static IEnumerable<Move> Douling(TurnContext c)
        {
            if (c.Memory.Phase == 0)
            {
                foreach (Move move in DoulingAttacks(c, 1200)) yield return move;
                if (c.Air) foreach (Move move in WaitDown(c)) yield return move;
                yield return Move.Resonance(500);
                if (c.Succeeded)
                {
                    yield return Move.Wait(200);
                    if (c.Air) foreach (Move move in WaitDown(c)) yield return move;
                    foreach (Move move in DoulingAttacks(c, 1000)) yield return move;
                    c.Memory.Phase = 1;
                    yield break;
                }
            }
            else
            {
                yield return Move.Tap(Jump, 60);
                if (c.Air) yield return Move.Tap(Attack, 50);
                else foreach (Move move in WaitDown(c)) yield return move;
                for (int attempt = 0; attempt < 3 && c.Hud.GuaCount != 1; attempt++)
                {
                    bool readable = c.Hud.GuaCount >= 2, interrupted = false;
                    int spentFrames = 0;
                    long started = c.Now;
                    yield return Move.Press(Attack);
                    while (c.Now - started < 2500)
                    {
                        if (c.Air) { interrupted = true; break; }
                        if (readable)
                        {
                            spentFrames = c.Hud.GuaCount < 2 ? spentFrames + 1 : 0;
                            if (spentFrames >= 2) break;
                        }
                        int remaining = (int)(2500 - (c.Now - started));
                        yield return Move.Wait(Math.Min(readable ? 50 : 100, remaining));
                    }
                    yield return Move.Release(Attack, 10);
                    if (!interrupted) break;
                    foreach (Move move in WaitDown(c)) yield return move;
                }
            }
            yield return Move.Echo(0);
            yield return Move.Liberation();
            c.Memory.Phase = 0;
        }

        private static IEnumerable<Move> DoulingAttacks(TurnContext c, int duration)
        {
            yield return Move.Attacks(duration, h => h.Airborne);
            if (c.Air) foreach (Move move in WaitDown(c)) yield return move;
        }

        internal static IEnumerable<Move> Lily(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(800);
            if (c.Has("lily_slumberland")) yield break;
            yield return Move.Echo(0);
            foreach (Move action in LilyFill(c, 10000, LilyForte)) yield return action;
            if (LilyForte(c.Hud))
            {
                yield return Move.Press(Attack);
                yield return Move.Wait(2000, LilyChanneling);
                if (c.Succeeded)
                    yield return Move.Wait(6000, h => !LilyChanneling(h), 150);
                bool finished = c.Succeeded;
                yield return Move.Release(Attack);
                if (finished) yield return Move.Wait(700);
            }
            bool canLiberate = c.RReady && LilyLiberation(c.Hud);
            double target = canLiberate ? .8 : 1.0;
            foreach (Move action in LilyFill(c, 3000, h => h.Concerto >= target || h.ConcertoFull)) yield return action;
            yield return Move.Liberation(0, AnimationInput.None, available: LilyLiberation);
            if (canLiberate && !c.Succeeded)
                foreach (Move action in LilyFill(c, 3000, Full)) yield return action;
        }

        private static bool LilyForte(HudSnapshot h) { return HeavyHint(h) || h.ForteFullColor; }
        private static bool LilyChanneling(HudSnapshot h) { return h.Shows("lily_heavy") && !HeavyHint(h); }
        private static bool LilyLiberation(HudSnapshot h) { return h.Shows("lily_liberation") && !h.Shows("lily_slumberland"); }

        private static IEnumerable<Move> LilyFill(TurnContext c, int limit, Func<HudSnapshot, bool> filled)
        {
            long start = c.Now;
            while (c.Since(start) < limit && !filled(c.Hud))
            {
                yield return Move.Resonance(800, sendClick: false);
                if (!c.Succeeded) yield return Move.Tap(Attack, 100);
                yield return Move.Wait(10);
            }
        }

        internal static IEnumerable<Move> Lucy(TurnContext c)
        {
            foreach (Move action in BreakEnemy(c)) yield return action;
            if (!c.Hud.ForteFullColor)
            {
                long start = c.Now;
                while (!c.Hud.ForteFullColor && c.Now - start <= 8500)
                {
                    if (c.EReady) yield return Move.Resonance();
                    yield return Heavy(400, null, 10);
                    if (c.EReady) yield return Move.Resonance();
                }
                yield return Move.Attacks(100);
            }
            if (c.EReady) yield return Move.Resonance();
            foreach (Move action in BreakEnemy(c)) yield return action;
            if (c.HeavyReady)
            {
                yield return Heavy(400, null, 210);
                yield return Heavy(400, null, 10);
            }
            if (!c.HeavyReady) yield return Move.Attacks(6000, HeavyHint);
            if (c.HeavyReady)
            {
                yield return Heavy(2500, null, 100);
                yield return Move.Wait(2000, h => h.Skills[2] == SkillState.Ready);
            }
            if (c.QReady) yield return Move.Echo(0);
            if (c.RReady)
            {
                yield return Move.Liberation(1500, AnimationInput.InteractAndAttack);
                c.Memory.LastLiberation = c.Now;
                yield return Move.Attacks(1100);
            }
        }

        internal static IEnumerable<Move> Lucilla(TurnContext c)
        {
            const int liberationIndex = (int)TideCombat.Skill.Liberation;
            long chargeEnd = c.Now + 7200;
            yield return Move.Wait(500);
            while (!c.RReady && c.Now < chargeEnd)
            {
                double cooldown = c.Hud.Cooldowns[liberationIndex];
                if (c.Hud.SkillLit[liberationIndex] ||
                    c.Hud.Skills[liberationIndex] == SkillState.Cooling && (double.IsNaN(cooldown) || cooldown > 1.5))
                    yield break;
                yield return Move.Hold(ResonanceWithoutCooldown(c.Hud) ? Skill : Attack, 1400, after: 10);
            }
            if (!c.RReady) yield break;
            if (EchoWithoutCooldown(c.Hud)) yield return Move.Echo(0);
            c.LucillaTransformPending = true;
            c.ContinueAfterLockLossUntil = c.Now + 19500;
            yield return Move.Repeat(Liberation, 1500, h => h.Skills[liberationIndex] != SkillState.Ready, 100);
            c.Memory.LastLiberation = c.Now;
            yield return Move.Wait(3000);
            long burst = c.Now + 15000;
            bool seen = false;
            while (c.Now < burst)
            {
                yield return Heavy((int)Math.Min(600, burst - c.Now), null, 20);
                if (c.Concerto > .1) seen = true;
                else if (seen && c.Concerto < .05) break;
            }
            c.ContinueAfterLockLossUntil = 0;
            c.LucillaTransformPending = false;
        }

        internal static IEnumerable<Move> Qingxiao(TurnContext c)
        {
            if (!HasTwoBuffs(c))
            {
                if (c.Has("qingxiao_e")) yield return Move.Resonance(1500, hasAnimation: true, animationDelay: 500);
                yield break;
            }
            long start = c.Now;
            while (c.Since(start) < 18000)
            {
                long cycleStart = c.Now;
                bool handled = false;
                if (!QingxiaoHeavyDark(c.Hud))
                {
                    bool second = !c.Has("qingxiao_h1") && c.Has("qingxiao_h2");
                    if (second) c.ContinueAfterLockLossUntil = c.Now + 4500;
                    yield return Move.Hold(Attack, 2000, QingxiaoHeavyDark, 10, 250);
                    handled = c.Succeeded;
                    if (handled)
                    {
                        foreach (Move action in BreakEnemy(c)) yield return action;
                        if (second)
                        {
                            if (!c.RReady) yield return Move.Wait(1500, h => h.Skills[2] == SkillState.Ready);
                            yield return Move.Liberation();
                            yield break;
                        }
                    }
                    c.ContinueAfterLockLossUntil = 0;
                }
                if (!handled && c.Has("qingxiao_e"))
                {
                    yield return Move.Resonance(1500, hasAnimation: true, animationDelay: 500);
                    handled = c.Succeeded;
                }
                if (!handled) yield return Move.Tap(Attack, 0);
                int remain = 100 - (int)(c.Now - cycleStart);
                if (remain > 0) yield return Move.Wait(remain);
            }
            if (!QingxiaoHeavyDark(c.Hud)) yield return Move.Hold(Attack, 2000, QingxiaoHeavyDark, 10, 250);
        }

        private static bool QingxiaoHeavyDark(HudSnapshot h) { return !h.Shows("qingxiao_h1") && !h.Shows("qingxiao_h2"); }

        internal static IEnumerable<Move> Hsin(TurnContext c)
        {
            long phaseStart = c.Now;
            int phaseDuration = c.Intro ? 12000 : 6000;
            bool finisher = false, secondHeavyComplete = false;
            while (c.Since(phaseStart) < phaseDuration)
            {
                long cycleStart = c.Now;
                if ((secondHeavyComplete || c.Has("hsin_lib2")) && c.RReady)
                {
                    yield return Move.Liberation(0);
                    if (c.Succeeded) yield break;
                }
                if (secondHeavyComplete)
                {
                    yield return Move.Wait(100);
                    continue;
                }
                if (c.Has("hsin_h2"))
                {
                    yield return HsinHeavy(true);
                    secondHeavyComplete = c.Succeeded;
                    if (secondHeavyComplete)
                    {
                        long waitStart = c.Now;
                        while (c.Now - waitStart < 1500)
                        {
                            if (c.RReady)
                            {
                                yield return Move.Liberation(0);
                                if (c.Succeeded) yield break;
                            }
                            yield return Move.Wait(50);
                        }
                    }
                    else c.ContinueAfterLockLossUntil = 0;
                    continue;
                }
                if (finisher)
                {
                    if (c.Has("e_forte"))
                        foreach (Move action in HsinEnhancedResonance(c)) yield return action;
                    else yield return Move.Tap(Attack, 100);
                    continue;
                }
                bool acted = false;
                if (c.EReady)
                {
                    yield return Move.Resonance(0);
                    acted = c.Succeeded;
                }
                if (!acted && (c.Has("hsin_lib1") || c.Has("hsin_lib2")) && c.RReady)
                {
                    bool second = c.Has("hsin_lib2");
                    yield return Move.Liberation(0);
                    if (c.Succeeded)
                    {
                        if (second) yield break;
                        finisher = true;
                        phaseStart = c.Now;
                        phaseDuration = 18000;
                        acted = true;
                    }
                }
                if (!acted)
                {
                    if (c.Has("hsin_h1")) yield return HsinHeavy();
                    else
                    {
                        yield return Move.Echo(0);
                        if (!c.Succeeded) yield return Move.Tap(Attack, 0);
                    }
                }
                int remainder = 100 - (int)(c.Now - cycleStart);
                if (remainder > 0) yield return Move.Wait(remainder);
            }
        }

        private static Move HsinHeavy(bool second = false)
        {
            Move move = Move.Hold(Attack, 1200, h => !h.Shows("hsin_h1") && !h.Shows("hsin_h2"), 10, 250);
            move.DetectHiddenRelease = second;
            move.LockLossProtection = second ? 4000 : 0;
            return move;
        }

        private static IEnumerable<Move> HsinEnhancedResonance(TurnContext c)
        {
            c.Succeeded = false;
            long end = c.Now + 1500, sentAt = -1, goneAt = -1;
            while (c.Now < end)
            {
                if (c.Has("e_forte"))
                {
                    goneAt = -1;
                    if (sentAt < 0 || c.Now - sentAt >= 250)
                    {
                        long pressedAt = c.Now;
                        yield return Move.Tap(Skill, 0, recordSkill: false);
                        if (!c.Succeeded) yield break;
                        sentAt = pressedAt;
                    }
                }
                else if (sentAt >= 0)
                {
                    if (goneAt < 0) goneAt = c.Now;
                    if (c.Now - goneAt >= 250)
                    {
                        c.Memory.LastSkill[0] = c.Now;
                        c.Succeeded = true;
                        yield break;
                    }
                }
                yield return Move.Wait(50);
            }
            c.Succeeded = false;
        }

        internal static IEnumerable<Move> Hiyuki(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1000);
            int duration = c.Intro && c.Outro == "Linnai" ? 18000 : 16000;
            if (c.Long && (c.Hud.Skills[2] == SkillState.Ready || c.Hud.Cooldowns[2] <= 3.5))
            {
                while (c.Long && c.Since(c.Started) < duration)
                {
                    yield return Move.Echo(0);
                    yield return Move.Resonance(sendClick: false);
                    if (c.RReady)
                    {
                        yield return Move.Liberation();
                        if (c.Succeeded) break;
                    }
                    if (c.HeavyReady)
                    {
                        yield return Move.Tap(Dodge, 0);
                        yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                        yield return Move.Attacks(16000, h => h.Skills[2] == SkillState.Ready);
                        yield return Move.Liberation();
                        if (c.Succeeded) break;
                    }
                    else yield return Move.Tap(Attack, 100);
                    yield return Move.Wait(50);
                }
            }
            if (!c.Longer) yield break;
            long start = c.Now;
            int strikes = 0;
            while (c.Longer && c.Since(start) < duration)
            {
                foreach (Move action in BreakEnemy(c)) yield return action;
                yield return Move.Echo(0);
                bool finishing = c.Since(start) >= duration - 500;
                if (c.RReady && (strikes >= 4 || finishing))
                {
                    yield return HiyukiFinisher();
                    if (c.Succeeded) { yield return Move.Wait(500); yield break; }
                }
                yield return Move.Resonance(sendClick: false);
                if (c.Succeeded)
                {
                    if (finishing) break;
                    yield return Move.Attacks(300);
                }
                else if (c.Has("hiyuki_lib_forte"))
                {
                    if (finishing) break;
                    foreach (Move action in HiyukiWaitForLock(c)) yield return action;
                    if (c.Longer && c.Has("hiyuki_lib_forte"))
                        yield return Move.Hold(Attack, 2000, h => !h.Shows("hiyuki_lib_forte"), 50, 0);
                    yield return Move.Attacks(500, h => h.Skills[2] == SkillState.Ready);
                    if (c.RReady)
                    {
                        foreach (Move action in HiyukiWaitForLock(c)) yield return action;
                        if (c.Longer)
                        {
                            yield return HiyukiFinisher();
                            if (c.Succeeded) { yield return Move.Wait(500); yield break; }
                        }
                    }
                    strikes++;
                    yield return Move.Wait(100);
                }
                else if (c.Has("hiyuki_left"))
                {
                    yield return Move.Attacks(3000, h => !h.Shows("hiyuki_left"));
                    if (finishing) break;
                    yield return Move.Wait(100);
                }
                else if (c.Has("hiyuki_right"))
                {
                    if (c.Now - c.Memory.LastDodge >= 1000) yield return Move.Tap(Dodge, 100);
                    else yield return Move.Wait(100);
                }
                else yield return Move.Tap(Attack, 100);
                yield return Move.Wait(50);
            }
            if (c.RReady)
            {
                yield return HiyukiFinisher();
                if (c.Succeeded) yield return Move.Wait(500);
            }
        }

        private static Move HiyukiFinisher()
        {
            return Move.RepeatHeldLiberation(8000, 3000, 200, h => h.Skills[2] != SkillState.Ready && !(h.Cooldowns[2] <= 1.5));
        }

        private static IEnumerable<Move> HiyukiWaitForLock(TurnContext c)
        {
            c.ContinueAfterLockLossUntil = c.Now + 2000;
            yield return Move.Attacks(2000, h => h.Locked && h.LongActionLayout == 2);
            c.ContinueAfterLockLossUntil = 0;
        }

        internal static IEnumerable<Move> Roccia(TurnContext c)
        {
            if (c.Intro)
            {
                yield return Heavy(1600, null, 110);
                foreach (Move action in RocciaAir(c)) yield return action;
                if (!c.RReady && !c.EReady) yield break;
            }
            yield return Move.Liberation();
            bool r = c.Succeeded;
            yield return Move.Resonance();
            if (c.Succeeded || !r)
                foreach (Move action in RocciaAir(c)) yield return action;
            else yield return Move.Echo();
        }

        internal static IEnumerable<Move> RocciaAir(TurnContext c)
        {
            if (c.NeedFastPerform)
            {
                yield return Move.Tap(Attack, 0);
                int remain = 1100 - (int)c.Since(c.Started);
                if (remain > 0) yield return Move.Attacks(remain);
                yield break;
            }
            long end = c.Now + 8000, lastAirSeen = c.Now, lastReady = c.Now;
            int spare = 0;
            yield return Move.Press(BattleButton.Forward);
            while (c.Now < end)
            {
                bool air = c.Hud.AirCharges > 0;
                if (air)
                {
                    lastAirSeen = c.Now;
                    spare = c.Hud.AirCharges - 1;
                }
                else if (spare > 0 && c.HeavyReady && c.Now - lastAirSeen < 900)
                {
                    spare--;
                    air = true;
                }
                if (air || c.HeavyReady)
                {
                    lastReady = c.Now;
                    yield return Heavy(air ? 400 : 1600, null, 80);
                }
                else if (c.Now - lastReady > 450 && (spare == 0 || c.Now - lastAirSeen >= 900)) break;
                else yield return Move.Wait(50);
            }
            yield return Move.Release(BattleButton.Forward);
        }

        internal static IEnumerable<Move> Suisui(TurnContext c)
        {
            long start = c.Now;
            if (c.Intro)
            {
                yield return Heavy(1500, null, 0);
                yield return Move.Resonance();
            }
            bool reached = false;
            while (c.Since(start) < 26000)
            {
                if (c.Has("suisui_forte3")) { reached = true; break; }
                if (c.Has("suisui_forte2"))
                {
                    for (int click = 0; click < 7 && !c.Has("suisui_forte3"); click++)
                        yield return Move.Tap(Attack, 100);
                    reached = true;
                    break;
                }
                if (!c.Intro && c.Has("suisui_e1"))
                {
                    long skillStart = c.Now;
                    while (c.Has("suisui_e1") && c.Since(skillStart) < 2000)
                        yield return Move.Tap(Skill, 50);
                    yield break;
                }
                yield return Move.Tap(Attack, 100);
            }
            if (!reached) yield break;
            start = c.Now;
            bool liberated = false;
            while (c.Since(start) < 12000)
            {
                if (!liberated)
                {
                    yield return Move.Liberation(0);
                    liberated = c.Succeeded;
                    if (liberated)
                    {
                        yield return Move.Wait(10);
                        continue;
                    }
                }
                if (c.Full && c.Has("suisui_forte3")) break;
                yield return Move.Tap(Attack, 100);
            }
        }

        internal static IEnumerable<Move> Suoming(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1600);
            yield return Move.Liberation(1000);
            bool liberated = c.Succeeded;
            if (liberated && !c.Settings.SuomingDps)
            {
                yield return Move.Echo(0);
                c.Memory.FreeIntro = true;
                yield break;
            }
            if (!liberated && !c.Intro)
            {
                yield return Move.Attacks(1100);
                yield break;
            }
            if (!liberated) yield return Move.Echo(0);
            if (c.Settings.SuomingDps) yield return Move.Resonance(1000, sendClick: false);
            yield return Move.Attacks(15000, h => h.Shows("suoming_forte_e"));
            long holdStart = c.Now;
            bool held = false;
            while (c.Since(holdStart) < 6000 && c.Has("suoming_forte_e"))
            {
                if (held)
                {
                    yield return Move.Release(Skill);
                    yield return Move.Wait(100);
                }
                yield return Move.Press(Skill);
                held = true;
                yield return Move.Wait(2000);
            }
            yield return Move.Wait(3000);
            yield return Move.Wait(8000, ResonanceWithoutCooldown);
            if (held) yield return Move.Release(Skill);
            yield return Move.Echo();
            yield return Move.Wait(10);
        }
    }
}
