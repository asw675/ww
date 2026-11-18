using System;
using System.Collections.Generic;

namespace TideCombat
{
    internal enum PhoebePrayerResult { Unavailable, Entered, TimedOut }

    internal sealed class PhoebeMemory
    {
        internal int Mode, StarflashCount, Liberations;
        internal bool Star, FirstRotationDone, PriorityLiberation;
        internal PhoebePrayerResult Prayer;
    }

    internal static partial class Rotations
    {
        internal static IEnumerable<Move> Phoebe(TurnContext c)
        {
            c.Memory.LastOutro = -1;
            FighterMemory zani = c.Ally("Zani");
            PhoebeMemory state = c.Memory.Phoebe;
            if (state == null)
            {
                state = new PhoebeMemory();
                c.Memory.Phoebe = state;
            }
            if (state.Mode == 0) state.Mode = zani != null || c.With("Cartethyia") && c.HasRover() ? 2 : 1;
            long start = c.Now;
            if (c.Intro) yield return Move.Attacks(1500);
            else yield return Move.Wait(10);
            foreach (Move action in PhoebePriorityLiberation(c, state)) yield return action;
            if (state.Mode == 1) yield return Move.Echo(0);
            if (c.Air)
            {
                yield return Move.Attacks(100);
                foreach (Move action in PhoebeBeforeSwitch(c, state)) yield return action;
                yield break;
            }

            int observedMode = c.Hud.ChargeMode;
            bool mismatch = observedMode != 0 && observedMode != state.Mode;
            if (observedMode == 0) state.Star = false;
            if (mismatch)
            {
                int original = state.Mode;
                state.Mode = observedMode;
                foreach (Move action in PhoebeRemainingSkills(c, state, false)) yield return action;
                state.Mode = original;
            }
            if (state.Mode == 2 && zani != null)
            {
                if (!state.Star) foreach (Move action in PhoebePrayer(c, state)) yield return action;
                long zaniTime = PhoebeZaniTime(c, zani);
                if (zani.Resource >= .90)
                {
                    if (!ResonanceWithoutCooldown(c.Hud))
                    {
                        if (zaniTime == 0 || zaniTime > 3000) yield return Move.Attacks(1000, interval: 150);
                    }
                    else if (state.FirstRotationDone && !c.Hud.PhoebeConfessionReady)
                        yield return Move.Resonance(sendClick: false);
                    foreach (Move action in PhoebeBeforeSwitch(c, state)) yield return action;
                    yield break;
                }
                if (zaniTime > 0)
                {
                    foreach (Move action in PhoebeRemainingSkills(c, state, true)) yield return action;
                    foreach (Move action in PhoebeBeforeSwitch(c, state)) yield return action;
                    yield break;
                }
            }

            int uiWait = 350 - (int)(c.Now - start);
            if (uiWait > 0 && state.Star && PhoebeForteSegments(c.Hud, state) == 0) yield return Move.Attacks(uiWait);
            foreach (Move action in PhoebePrayer(c, state)) yield return action;
            bool entered = state.Prayer == PhoebePrayerResult.Entered;
            if ((!mismatch || entered) && state.Star)
            {
                yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
                if (c.Succeeded) { state.Liberations++; state.PriorityLiberation = true; }
            }
            if (entered || PhoebeForteSegments(c.Hud, state) > 0)
            {
                foreach (Move action in PhoebeStarflash(c, state)) yield return action;
                foreach (Move action in PhoebePriorityLiberation(c, state)) yield return action;
                if (state.Mode == 2 && state.StarflashCount < 2 && PhoebeZaniTime(c, zani) == 0)
                    foreach (Move action in PhoebeStarflash(c, state)) yield return action;
            }
            foreach (Move action in PhoebePriorityLiberation(c, state)) yield return action;
            if (ResonanceWithoutCooldown(c.Hud))
            {
                if (state.Mode == 1) yield return Move.Resonance();
                else if (state.FirstRotationDone && !c.Hud.PhoebeConfessionReady)
                    yield return Move.Repeat(Skill, 500, h => !ResonanceWithoutCooldown(h));
            }
            else yield return Move.Attacks(100);
            if (!state.FirstRotationDone)
            {
                state.FirstRotationDone = true;
                if (!c.Full) yield return Move.Attacks(5000, Full);
            }
            foreach (Move action in PhoebeBeforeSwitch(c, state)) yield return action;
        }

        private static int PhoebeForteSegments(HudSnapshot h, PhoebeMemory state)
        {
            return state.Mode == 1 ? h.PhoebeLightForte : h.PhoebeBlueForte;
        }

        private static bool PhoebeHeavyReady(HudSnapshot h, PhoebeMemory state)
        {
            return state.Star ? HeavyHint(h) : h.ForteFullColor;
        }

        private static long PhoebeZaniTime(TurnContext c, FighterMemory zani)
        {
            if (zani == null || !zani.ZaniLiberated) return 0;
            long duration = zani.ZaniTimeLeft(c.Clock, c.Now);
            if (duration > 0) return duration;
            zani.Resource = -1;
            return 0;
        }

        private static IEnumerable<Move> PhoebePriorityLiberation(TurnContext c, PhoebeMemory state)
        {
            if (!state.Star || c.Air || !c.RReady) yield break;
            yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
            if (c.Succeeded) { state.Liberations++; state.PriorityLiberation = true; }
        }

        private static IEnumerable<Move> PhoebeBeforeSwitch(TurnContext c, PhoebeMemory state)
        {
            if (state.Mode != 2) yield break;
            if (!state.PriorityLiberation)
                foreach (Move action in PhoebePriorityLiberation(c, state)) yield return action;
            if (c.Full) yield return Move.Echo();
        }

        private static IEnumerable<Move> PhoebeRemainingSkills(TurnContext c, PhoebeMemory state, bool liberation)
        {
            int count = state.Mode == 1 ? 4 : 2;
            for (int attempt = 0; attempt < count; attempt++)
            {
                if (liberation && state.Liberations < 1 && c.RReady)
                {
                    yield return Move.Liberation();
                    if (c.Succeeded) state.Liberations++;
                }
                if (PhoebeForteSegments(c.Hud, state) > 0)
                {
                    foreach (Move action in PhoebeStarflash(c, state)) yield return action;
                    yield return Move.Wait(10);
                }
            }
        }

        private static Func<HudSnapshot, bool> PhoebePrayerCondition(TurnContext c, PhoebeMemory state)
        {
            if (!state.Star && c.Hud.ChargeMode == state.Mode) state.Star = true;
            if (!state.Star) return h => PhoebeHeavyReady(h, state);
            return c.Hud.PhoebeConfessionReady ? (Func<HudSnapshot, bool>)(h => h.PhoebeConfessionReady) : h => false;
        }

        private static IEnumerable<Move> PhoebePrayer(TurnContext c, PhoebeMemory state)
        {
            Func<HudSnapshot, bool> condition = PhoebePrayerCondition(c, state);
            state.Prayer = PhoebePrayerResult.Unavailable;
            if (!condition(c.Hud)) yield break;
            BattleButton button = state.Mode == 2 ? Skill : Attack;
            long prayerStart = c.Now;
            while (condition(c.Hud))
            {
                if (c.Now - prayerStart > 2000)
                {
                    state.Prayer = PhoebePrayerResult.TimedOut;
                    yield break;
                }
                long down = c.Now;
                yield return Move.Press(button);
                yield return Move.Wait(400);
                int remaining = 1000 - (int)(c.Now - down);
                if (remaining > 0) yield return Move.Wait(remaining, h => !condition(h));
                yield return Move.Release(button);
                if (c.Air)
                {
                    yield return Move.Attacks(2000, Grounded);
                    prayerStart = c.Now;
                }
                yield return Move.Wait(10);
            }
            yield return Move.Tap(Dodge, 50);
            state.Star = true;
            if (state.Mode == 2)
            {
                state.Liberations = state.StarflashCount = 0;
                state.PriorityLiberation = false;
            }
            state.Prayer = PhoebePrayerResult.Entered;
        }

        private static IEnumerable<Move> PhoebeStarflash(TurnContext c, PhoebeMemory state)
        {
            long start = c.Now, checkForte = c.Now;
            Func<HudSnapshot, bool> prayer = PhoebePrayerCondition(c, state);
            if (!prayer(c.Hud) && !PhoebeHeavyReady(c.Hud, state))
            {
                while (!PhoebeHeavyReady(c.Hud, state))
                {
                    if (c.Air) foreach (Move move in ShoreKeeperDodge(c)) yield return move;
                    yield return Move.Tap(Attack, 100);
                    if (c.Now - start > 5000) yield break;
                    if (c.Now - checkForte > 1000)
                    {
                        if (prayer(c.Hud) || PhoebeForteSegments(c.Hud, state) == 0) yield break;
                    }
                    else checkForte = c.Now;
                }
                yield return Move.Tap(Dodge, 50);
            }
            foreach (Move action in PhoebePrayer(c, state)) yield return action;
            if (state.Prayer != PhoebePrayerResult.Unavailable) yield break;
            start = c.Now;
            while (PhoebeHeavyReady(c.Hud, state))
            {
                if (c.Now - start > 2000) yield break;
                yield return Move.Hold(Attack, 500, h => !PhoebeHeavyReady(h, state) || h.Airborne, 0, 0);
                if (!PhoebeHeavyReady(c.Hud, state)) break;
                if (c.Air)
                {
                    yield return Move.Attacks(2000, Grounded);
                    start = c.Now;
                }
                yield return Move.Wait(10);
            }
            state.StarflashCount++;
        }
    }
}
