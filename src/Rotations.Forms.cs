using System;
using System.Collections.Generic;

namespace TideCombat
{
    internal static partial class Rotations
    {
        internal static IEnumerable<Move> Camellya(TurnContext c)
        {
            if (c.Intro)
            {
                yield return Move.Attacks(1200);
                yield return Move.Wait(100);
                long started = c.Now;
                yield return Move.Press(Attack);
                var charge = new CamellyaChargeProgress();
                while (c.Now - started < 4600 && !c.Full && !ChargeSpent(c.Hud))
                {
                    foreach (Move move in CamellyaCheckTarget(c, true)) yield return move;
                    if (charge.NeedsRetry(c, c.Hud.CharacterCharge))
                        foreach (Move move in CamellyaRetryHold(100, 100)) yield return move;
                    else yield return Move.Wait(50);
                }
                yield return Move.Wait(100);
                yield return Move.Release(Attack);
            }
            if (c.Concerto <= .82 && c.RReady) yield return Move.Liberation();
            bool lowConcerto = c.Concerto < .82;
            if (lowConcerto && c.EReady) yield return Move.Resonance();
            long startedAt = c.Now;
            int length = lowConcerto ? 1100 : 4600;
            while (c.Now - startedAt < length || c.Has("camellya_budding"))
            {
                if (c.Full && c.Hud.EnhancedColor)
                {
                    while (c.Hud.EnhancedColor) yield return Move.Tap(Skill, 100);
                    yield return Move.Wait(1100);
                    foreach (Move move in CamellyaCheckTarget(c, false)) yield return move;
                    foreach (Move move in CamellyaBudding(c)) yield return move;
                    yield return Move.Resonance();
                    yield return Move.Wait(100);
                    break;
                }
                yield return Move.Tap(Attack, 100);
                if (c.Concerto < .82)
                {
                    if (!c.Full)
                    {
                        yield return Move.Echo(0);
                        yield break;
                    }
                    if (length < 3100) length += 1000;
                }
                foreach (Move move in CamellyaCheckTarget(c, false)) yield return move;
            }
            yield return Move.Echo(0);
        }

        private static bool ChargeSpent(HudSnapshot hud) { return hud.CharacterCharge >= 0 && hud.CharacterCharge <= .01; }

        private static IEnumerable<Move> CamellyaBudding(TurnContext c)
        {
            long start = c.Now;
            var charge = new CamellyaChargeProgress();
            yield return Move.Press(Attack);
            while (c.Now - start < 5100 || c.Has("camellya_budding"))
            {
                if (c.RReady && c.Now - start < 1500)
                {
                    yield return Move.Liberation();
                    if (c.Succeeded)
                        foreach (Move move in CamellyaRetryHold(200, 0)) yield return move;
                }
                foreach (Move move in CamellyaCheckTarget(c, true)) yield return move;
                if (charge.NeedsRetry(c, c.Hud.BuddingCharge))
                    foreach (Move move in CamellyaRetryHold(100, 100)) yield return move;
                else yield return Move.Wait(50);
            }
            yield return Move.Release(Attack, 100);
        }

        private static IEnumerable<Move> CamellyaCheckTarget(TurnContext c, bool holding)
        {
            if (c.Hud.Locked) yield break;
            if (holding) yield return Move.Release(Attack, 50);
            else yield return Move.Wait(50);
            if (holding) yield return Move.Press(Attack);
        }

        private static IEnumerable<Move> CamellyaRetryHold(int before, int after)
        {
            yield return Move.Release(Attack, before);
            yield return Move.Press(Attack, after);
        }

        private sealed class CamellyaChargeProgress
        {
            private long stationaryAt = -1, resumeSamplingAt;
            private double previous, accumulatedDrop;

            internal bool NeedsRetry(TurnContext c, double current)
            {
                if (c.Now < resumeSamplingAt) return false;
                if (current < 0)
                {
                    resumeSamplingAt = c.Now + 200;
                    return false;
                }
                double drop = previous - current;
                previous = current;
                if (stationaryAt < 0 && drop >= 0 && drop <= .01)
                {
                    stationaryAt = c.Now;
                    accumulatedDrop = 0;
                }
                if (stationaryAt < 0) return false;
                accumulatedDrop += drop;
                if (accumulatedDrop > .01) stationaryAt = -1;
                if (stationaryAt < 0 || c.Since(stationaryAt) <= 600) return false;
                stationaryAt = -1;
                return true;
            }
        }

        internal static IEnumerable<Move> Cartethyia(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1200);
            else yield return Move.Echo(0);
            bool transformedThisTurn = false;
            c.Memory.CartethyiaTransformed = !c.Has("cartethyia_small");
            if (!c.Memory.CartethyiaTransformed)
            {
                foreach (Move action in WaitDown(c)) yield return action;
                bool sword1 = c.Has("forte_cartethyia_sword1"), sword2 = c.Has("forte_cartethyia_sword2");
                bool sword3 = c.Has("forte_cartethyia_sword3");
                if (!(sword1 && sword2 && sword3))
                {
                    if (!sword2)
                    {
                        bool emptyShape = c.Has("cartethyia_sword2_shape"), interrupted = false;
                        int limit = emptyShape ? (c.FirstEngage ? 2500 : 2000) : 3500;
                        long started = c.Now;
                        while (c.Now - started < limit && (emptyShape || !c.Has("cartethyia_sword2_shape")))
                        {
                            if (c.Air && !interrupted)
                            {
                                if (limit == 2000) limit = 2500;
                                interrupted = true;
                                yield return Move.Wait(3000, Grounded);
                                started = c.Now;
                            }
                            yield return Move.Tap(Attack, 100);
                        }
                    }
                    bool resonance = false;
                    if (!sword3)
                    {
                        yield return Move.Resonance();
                        resonance = c.Succeeded;
                    }
                    if (c.RReady)
                    {
                        if (resonance) yield return Move.Wait(200);
                    }
                    else if (!sword2 || !sword3) yield break;
                    if (!sword1) yield return Move.Hold(Attack, 1500, h => h.Shows("forte_cartethyia_sword1"), 0, 0);
                    if (!(sword1 || sword2 || sword3)) c.Memory.CartethyiaTryMidAir = true;
                    if (!c.RReady) yield break;
                }
                foreach (Move action in CartethyiaMidAir(c)) yield return action;
                yield return Move.Liberation();
                if (c.Succeeded)
                {
                    c.Memory.CartethyiaTransformed = transformedThisTurn = true;
                    c.Memory.LastSkill[0] = -1;
                }
                else c.Memory.CartethyiaTransformed = transformedThisTurn = !c.Has("cartethyia_small");
            }

            bool castResonance = false;
            long resonanceStart = c.Now, firstResonance = -1;
            while (ResonanceWithoutCooldown(c.Hud) && c.Now - (firstResonance >= 0 ? firstResonance : resonanceStart) < 8000)
            {
                double energy = c.Hud.ResonanceWhiteRatio;
                if (energy > 0 && !(firstResonance >= 0 && c.Now - firstResonance < 2500 && energy < .17))
                {
                    if (firstResonance < 0) firstResonance = c.Now;
                    yield return Move.Tap(Skill, 100);
                    castResonance |= c.Succeeded;
                }
                else if (energy > 0) yield return Move.Tap(Attack, 100);
                else yield return Move.Wait(50);
                if (!c.Has("lib_cartethyia_big")) continue;
                foreach (Move action in CartethyiaFinisher(c)) yield return action;
                if (!c.Memory.CartethyiaTransformed) yield break;
            }
            if (castResonance) c.Memory.CartethyiaResonance = c.Now;
            else
            {
                int duration = c.Memory.CartethyiaTransformed ? CartethyiaChainWindow(c, transformedThisTurn) : 1100;
                long start = c.Now;
                while (c.Now - start < duration)
                {
                    if (c.Has("lib_cartethyia_big"))
                    {
                        foreach (Move action in CartethyiaFinisher(c)) yield return action;
                        if (!c.Memory.CartethyiaTransformed) yield break;
                    }
                    yield return Move.Tap(Attack, 100);
                }
                c.Memory.CartethyiaChain = c.Now;
            }
            if (c.Has("lib_cartethyia_big"))
                foreach (Move action in CartethyiaFinisher(c)) yield return action;
        }

        private static int CartethyiaChainWindow(TurnContext c, bool justTransformed)
        {
            long elapsed = c.Now - c.Started;
            long afterResonance = c.Since(c.Memory.CartethyiaResonance, true);
            int duration = !justTransformed && c.Intro ? 3900 - (int)elapsed :
                justTransformed || c.FirstEngage || c.Since(c.Memory.CartethyiaChain, true) < 1500 ? 3250 :
                afterResonance < 2500 ? 2000 + (int)Math.Max(0, 1600 - afterResonance) : 1900 - (int)elapsed;
            c.Memory.CartethyiaChain = c.Memory.CartethyiaResonance = -1;
            return duration;
        }

        private static IEnumerable<Move> CartethyiaMidAir(TurnContext c)
        {
            bool allSwords = c.Has("forte_cartethyia_sword1") && c.Has("forte_cartethyia_sword2") && c.Has("forte_cartethyia_sword3");
            if (!c.RReady && !allSwords && !c.Memory.CartethyiaTryMidAir) yield break;
            bool airCue = c.Has("cartethyia_air_cue");
            int limit = airCue ? 2000 : c.Memory.CartethyiaTryMidAir ? 800 : 0;
            long start = c.Now;
            while (c.Now - start < limit)
            {
                yield return Move.Tap(Jump, 100);
                yield return Move.Echo(0);
                yield return Move.Tap(Attack, 100);
                if (airCue && !c.Has("cartethyia_air_cue"))
                {
                    yield return Move.Wait(400);
                    break;
                }
                if (airCue) yield return Move.Wait(100);
            }
            c.Memory.CartethyiaTryMidAir = false;
        }

        private static IEnumerable<Move> CartethyiaFinisher(TurnContext c)
        {
            yield return Move.Liberation();
            if (!c.Succeeded) yield break;
            c.Memory.CartethyiaTransformed = false;
            yield return Move.Resonance();
        }

        internal static IEnumerable<Move> Rover(TurnContext c)
        {
            c.Memory.RoverForm = c.Memory.RoverForm ?? c.Hud.RoverForm ?? "RoverSpectro";
            if (!c.Intro) yield return Move.Wait(10);
            CharacterKit form = CharacterRoster.Find(c.Memory.RoverForm);
            IEnumerable<Move> actions = form == null || form.Id == "Rover" ? RoverBasic(c) : form.Play(c);
            foreach (Move action in actions) yield return action;
        }

        private static IEnumerable<Move> RoverBasic(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(CharacterKit.IntroDuration("Rover") + 200);
            foreach (Move action in WaitDown(c)) yield return action;
            yield return Move.Echo();
            yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
            bool r = c.Succeeded;
            yield return Move.Resonance();
            if (!c.Succeeded && !r) yield return Move.Attacks(1000);
        }

        internal static IEnumerable<Move> RoverSpectro(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1000);
            foreach (Move action in WaitDown(c)) yield return action;
            yield return Heavy(600, null, 410);
            yield return Move.Attacks(700);
            yield return Move.Echo(0);
            if (c.Hud.ForteFullColor && ResonanceWithoutCooldown(c.Hud))
            {
                yield return Move.Resonance();
                if (c.Succeeded)
                {
                    yield return Move.Attacks(1400);
                    yield return Move.Wait(100);
                }
            }
            yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
            if (!c.Succeeded) yield return Move.Resonance();
        }

        internal static IEnumerable<Move> RoverHavoc(TurnContext c)
        {
            foreach (Move action in WaitDown(c)) yield return action;
            if (c.HeavyReady) yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
            yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
            yield return Move.Resonance();
            if (c.Succeeded) yield break;
            yield return Move.Echo();
            if (!c.Succeeded) yield return Move.Tap(Attack, 0);
            int remaining = 1100 - (int)c.Since(c.Memory.LastExit);
            if (remaining > 0) yield return Move.Attacks(remaining);
        }

        internal static IEnumerable<Move> RoverAero(TurnContext c)
        {
            bool special = c.With("Cartethyia") && c.With("Phoebe");
            long introStarted = c.Now;
            if (c.Intro) yield return Move.Attacks(2000, h => !AeroRoverFlying(h));
            if (!c.Intro || c.Now - introStarted < 2000)
            {
                foreach (Move action in AeroRoverLand(c, false)) yield return action;
                if (ResonanceWithoutCooldown(c.Hud) && !c.Hud.ForteFullColor)
                {
                    yield return Move.Echo(0);
                    long end = c.Now + 1000;
                    do
                    {
                        yield return Move.Tap(Skill, 0);
                        yield return Move.Tap(Attack, 100);
                    } while (!AeroRoverFlying(c.Hud) && c.Now < end);
                    if (AeroRoverFlying(c.Hud)) yield return Move.Attacks(special ? 1600 : 1740, h => !AeroRoverFlying(h));
                    if (special)
                    {
                        yield return Move.Resonance(sendClick: false);
                        if (c.Succeeded) yield return Move.Attacks(1000, h => !AeroRoverFlying(h));
                    }
                }
            }
            yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
            foreach (Move action in AeroRoverLand(c, true)) yield return action;
        }

        private static bool AeroRoverFlying(HudSnapshot h)
        {
            return h.LevitatorKnown ? h.Airborne : h.ResonanceWhiteRatio > .15;
        }

        private static IEnumerable<Move> AeroRoverLand(TurnContext c, bool final)
        {
            if (AeroRoverFlying(c.Hud))
            {
                if (c.Hud.LevitatorKnown) foreach (Move action in WaitDown(c)) yield return action;
                else yield return Move.Attacks(2500, h => h.ResonanceWhiteRatio < .15);
            }
            yield return Move.Wait(final ? 30 : 10);
            if (final && c.Hud.ForteFullColor) yield return Move.Tap(Skill, 0);
        }

        private static bool ElectroReady(HudSnapshot h) { return h.Shows("e_forte") && h.ForteFullColor; }

        internal static IEnumerable<Move> RoverElectro(TurnContext c)
        {
            foreach (Move action in WaitDown(c, false)) yield return action;
            yield return Move.Echo();
            yield return Move.Liberation(animation: AnimationInput.None);
            if (!ElectroReady(c.Hud)) yield return Move.Resonance(1000, postSleep: 100, sendClick: false);
            long fillStart = c.Now;
            int limit = c.Intro ? 8000 : 12000;
            while (!ElectroReady(c.Hud) && c.Since(fillStart) < limit)
                yield return Move.Tap(Attack, 100);
            foreach (Move action in RoverEnhancedResonance(c)) yield return action;
            if (!c.Succeeded) yield break;
            if (!c.Full) yield return Move.Liberation(animation: AnimationInput.None);
            if (!c.Full) yield return Move.Attacks(2000, Full);
        }

        private static IEnumerable<Move> RoverEnhancedResonance(TurnContext c)
        {
            c.Succeeded = false;
            for (int attempt = 0; attempt < 2 && ElectroReady(c.Hud); attempt++)
            {
                long castStarted = c.Now;
                yield return Move.Tap(Skill, 0, downMilliseconds: 50, recordSkill: false);
                if (!c.Succeeded) yield break;
                yield return Move.Wait((int)Math.Max(0, 2600 - (c.Now - castStarted)),
                    h => c.Since(castStarted) >= 1500 && !h.Shows("e_forte") && !h.ForteFullColor, 200);
                if (c.Succeeded)
                {
                    c.Memory.LastSkill[0] = c.Now;
                    yield break;
                }
            }
        }
    }
}
