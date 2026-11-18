using System;
using System.Collections.Generic;

namespace TideCombat
{
    internal static partial class Rotations
    {
        internal static IEnumerable<Move> Zani(TurnContext c)
        {
            foreach (Move action in WaitDown(c)) yield return action;
            RefreshZaniForm(c);
            if (c.Memory.ZaniLiberated)
            {
                bool end = ZaniTime(c) < 1700;
                if (!end && c.Hud.ZaniNightfallCharge <= .15)
                {
                    foreach (Move action in ZaniWaitForResonance(c, true, true)) yield return action;
                    end = ZaniTime(c) < 1700 || !c.HeavyReady;
                }
                if (end)
                    foreach (Move action in ZaniFinisher(c)) yield return action;
                else foreach (Move action in ZaniNightfall(c, false)) yield return action;
                yield break;
            }

            foreach (Move action in BreakEnemy(c)) yield return action;
            c.Memory.ZaniCrisisAt = -1;
            c.Memory.Resource = c.Hud.Blazes;
            bool enhanced = ZaniEnhanced(c.Hud);
            bool normal = c.Hud.ResonanceWhiteRatio > .05;
            if (c.Intro && c.Memory.Resource >= 1 && !c.RReady)
            {
                yield return Move.Wait(200);
                normal = c.Hud.ResonanceWhiteRatio > .05;
            }
            if (c.Memory.Resource >= 1 && c.RReady)
            {
                long previousLib = c.Memory.ZaniLiberationAt;
                foreach (Move action in ZaniLiberation(c, false)) yield return action;
                if (c.Memory.ZaniLiberationAt == previousLib)
                {
                    yield return Move.Wait(100);
                    foreach (Move action in ZaniLiberation(c, false)) yield return action;
                }
                yield break;
            }
            if (enhanced || normal)
            {
                double threshold = c.With("Phoebe") ? .60 : .40;
                bool predicted = c.Memory.Resource + .10 >= threshold;
                foreach (Move action in ZaniCrisisProtocol(c)) yield return action;
                if (c.Memory.ZaniCrisisAt >= 0 && (enhanced ? predicted : c.Memory.Resource >= threshold) && c.RReady)
                    foreach (Move action in ZaniLiberation(c, true)) yield return action;
                yield break;
            }
            yield return Move.Tap(Attack, 100);
            int remaining = 1100 - (int)c.Since(c.Started);
            if (remaining > 0) yield return Move.Attacks(remaining);
        }

        private static void RefreshZaniForm(TurnContext c)
        {
            if (c.Hud.ZaniForm != 0) c.Memory.ZaniLiberated = c.Hud.ZaniForm == 2;
        }

        private static long ZaniTime(TurnContext c) { return c.Memory.ZaniTimeLeft(c.Clock, c.Now); }
        private static bool ZaniEnhanced(HudSnapshot h) { return h.Shows("e_forte"); }
        private static bool ZaniEnhancedOrAirborne(HudSnapshot h) { return ZaniEnhanced(h) || h.Airborne; }

        private static IEnumerable<Move> ZaniCrisisProtocol(TurnContext c)
        {
            if (!ZaniEnhanced(c.Hud))
            {
                if (ResonanceWithoutCooldown(c.Hud))
                {
                    yield return Move.Resonance(sendClick: false);
                    if (c.Succeeded) c.Memory.ZaniNormalResonanceAt = c.Now;
                    yield return Move.Wait(200);
                    yield return Move.Attacks(200);
                    if (!ZaniEnhancedOrAirborne(c.Hud)) yield return Move.Wait(1200, ZaniEnhancedOrAirborne);
                }
                else
                {
                    int afterDodge = 300 - (int)(c.Now - c.Memory.ZaniDodgeAt);
                    if (afterDodge > 0) yield return Move.Wait(afterDodge, ZaniEnhancedOrAirborne);
                    if (!ZaniEnhancedOrAirborne(c.Hud)) yield return Move.Hold(Attack, 600, ZaniEnhancedOrAirborne, 0, 0);
                    if (!ZaniEnhancedOrAirborne(c.Hud)) yield return Move.Attacks(850, ZaniEnhancedOrAirborne);
                    if (!ZaniEnhancedOrAirborne(c.Hud)) yield return Move.Wait(1150, ZaniEnhancedOrAirborne);
                }
                if (!ZaniEnhancedOrAirborne(c.Hud)) yield return Move.Attacks(200);
                if (!ZaniEnhancedOrAirborne(c.Hud))
                {
                    long start = c.Now, lastCheck = c.Now;
                    double previous = -1;
                    bool dodged = false;
                    while (!ZaniEnhancedOrAirborne(c.Hud) && c.Now - start < 2200)
                    {
                        if (c.Now - start > 800 && c.Now - lastCheck >= 200)
                        {
                            double charge = c.Hud.ZaniForteCharge;
                            if (previous > 0 && charge - previous < .01)
                            {
                                yield return Move.Tap(Dodge, 50);
                                c.Memory.ZaniDodgeAt = c.Now;
                                dodged = true;
                                break;
                            }
                            previous = charge;
                            lastCheck = c.Now;
                        }
                        yield return Move.Wait(50, ZaniEnhancedOrAirborne);
                    }
                    if (!dodged && !ZaniEnhancedOrAirborne(c.Hud))
                    {
                        yield return Move.Tap(Dodge, 50);
                        c.Memory.ZaniDodgeAt = c.Now;
                    }
                }
            }
            if (!ZaniEnhanced(c.Hud)) yield break;
            long cast = c.Now;
            yield return Move.Repeat(Skill, 1000, h => !ZaniEnhanced(h));
            if (c.Now - cast >= 350) c.Memory.ZaniCrisisAt = c.Now;
        }

        private static IEnumerable<Move> ZaniWaitForResonance(TurnContext c, bool attack, bool checkLiberation)
        {
            long start = c.Now, litAt = -1;
            while (c.Now - start < 2500 && (!checkLiberation || ZaniTime(c) >= 1700))
            {
                if (c.Hud.ResonanceWhiteRatio > 0)
                {
                    if (litAt < 0) litAt = c.Now;
                    if (c.Now - litAt >= 100) yield break;
                }
                else litAt = -1;
                yield return attack ? Move.Tap(Attack, 100) : Move.Wait(50);
            }
        }

        private static IEnumerable<Move> ZaniLiberation(TurnContext c, bool waitCrisis)
        {
            if (waitCrisis)
            {
                if (c.Since(c.Memory.ZaniCrisisAt, true) < 1600)
                {
                    if (c.Since(c.Memory.ZaniNormalResonanceAt) < 5000)
                        yield return Move.Wait(2000, h => c.Since(c.Memory.ZaniCrisisAt, true) >= 1600);
                    else foreach (Move action in ZaniWaitForResonance(c, false, false)) yield return action;
                }
                c.Memory.Resource = c.Hud.Blazes;
            }
            yield return Move.Echo(0);
            yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
            if (!c.Succeeded) yield break;
            c.Memory.ZaniCrisisAt = -1;
            c.Memory.ZaniLiberated = true;
            c.Memory.ZaniLiberationAt = c.Now;
            RefreshZaniForm(c);
            yield return Move.Tap(Dodge, 50);
            yield return Move.Attacks(150);
            foreach (Move action in ZaniNightfall(c, true)) yield return action;
            yield return Move.Wait(100);
            if (c.HeavyReady) foreach (Move action in ZaniNightfall(c, false)) yield return action;
        }

        private static IEnumerable<Move> ZaniNightfall(TurnContext c, bool cancel)
        {
            if (c.Hud.ZaniNightfallCharge <= .15)
            {
                long start = c.Now;
                while (c.Hud.ZaniNightfallCharge <= .15 || c.Now - start < 1600)
                {
                    yield return Move.Tap(Attack, 100);
                    if (c.Now - start > 3500 || !c.Memory.ZaniLiberated) yield break;
                    if (ZaniTime(c) < 1700)
                    {
                        foreach (Move action in ZaniFinisher(c)) yield return action;
                        yield break;
                    }
                }
            }
            yield return Move.Attacks(500);
            if (cancel)
            {
                yield return Move.Attacks(2500, h => h.ZaniNightfallCharge <= .035);
                yield return Move.Wait(250);
                yield return Move.Repeat(Dodge, 100);
            }
            else c.Memory.ZaniNightfallAt = c.Now;
        }

        private static IEnumerable<Move> ZaniFinisher(TurnContext c)
        {
            long start = c.Now, limit = c.Now + 9000;
            bool send = true;
            c.ContinueAfterLockLossUntil = limit;
            while (c.Hud.ZaniForm != 1 && c.Now - start <= 6000 && c.Now < limit)
            {
                if (c.Hud.ResonanceWhiteRatio == 0) start = c.Now;
                else if (c.Now - start > 1500) send = false;
                if (send) yield return Move.RawLiberationPress();
                yield return Move.Wait(50);
            }
            c.ContinueAfterLockLossUntil = 0;
            if (c.Hud.ZaniForm != 1)
            {
                RefreshZaniForm(c);
                if (!c.Memory.ZaniLiberated) c.Memory.Resource = c.Hud.Blazes;
                yield break;
            }
            if (c.Now - start >= 2250) c.Clock.Record(c.Now - 2250, 2250, c.Now, baseAllowance: 0);
            c.Memory.ZaniLiberated = false;
            c.Memory.ZaniLiberationAt = -1;
            c.Memory.Resource = -1;
        }
    }
}
