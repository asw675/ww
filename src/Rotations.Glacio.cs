using System.Collections.Generic;

namespace TideCombat
{
    internal static partial class Rotations
    {
        internal static IEnumerable<Move> Carlotta(TurnContext c)
        {
            FighterMemory zhezhi = c.FarmEchoTask ? null : c.Ally("Zhezhi");
            if (c.Intro) yield return Move.Attacks(1300);
            if (zhezhi != null && c.Intro && c.Outro == "Zhezhi")
            {
                foreach (Move action in CarlottaZhezhiOutro(c, zhezhi)) yield return action;
                yield break;
            }

            if (zhezhi == null)
            {
                if (c.HeavyReady)
                {
                    yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                    if (c.Succeeded) yield break;
                }
                if (CarlottaLiberationReady(c.Hud) && !c.NeedFastPerform)
                {
                    foreach (Move action in CarlottaBurst(c, false)) yield return action;
                    yield return Move.Echo();
                    c.Memory.CarlottaSwitchAt = c.Now;
                    yield break;
                }
                if (ResonanceWithoutCooldown(c.Hud))
                {
                    if (!c.Intro) yield return Heavy(600, null, 0);
                    yield return CarlottaResonance();
                    if (c.Succeeded) yield break;
                }
                yield return Move.Echo();
                if (!c.Succeeded) yield return Move.Attacks(310);
                yield break;
            }

            if (ReadCarlottaForte(c) < 4 && ResonanceWithoutCooldown(c.Hud) && !c.Memory.LiberationPrimed)
            {
                if (!c.Intro) yield return Heavy(600, null, 0);
                yield return CarlottaResonance();
                if (c.Succeeded)
                {
                    c.Memory.Resource += 2;
                    c.Memory.CarlottaSwitchAt = c.Now;
                    yield break;
                }
            }
            if (c.Memory.CarlottaReady(c.Clock, c.Now)) c.Memory.ResumeLiberation = false;
            if (c.HeavyReady)
            {
                yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                if (c.Succeeded)
                {
                    c.Memory.LiberationPrimed = true;
                    yield break;
                }
            }
            if (CarlottaLiberationReady(c.Hud) && c.Memory.ResumeLiberation)
                foreach (Move action in CarlottaBurst(c, true)) yield return action;
            if (EchoWithoutCooldown(c.Hud)) yield return Move.Echo();
            yield return Move.Attacks(310);
        }

        private static int ReadCarlottaForte(TurnContext c)
        {
            if (c.HeavyReady) return 4;
            c.Memory.Resource = c.Hud.ForteSegments;
            return c.Hud.ForteSegments;
        }

        private static Move CarlottaResonance() { return Move.Resonance(10000, requireLit: true); }
        private static bool CarlottaLiberationReady(HudSnapshot h) { return h.Skills[2] == SkillState.Ready && h.Keys[2] != '\0'; }

        private static IEnumerable<Move> CarlottaBurst(TurnContext c, bool consumePriming)
        {
            long lastSuccessful = -1;
            while (CarlottaLiberationReady(c.Hud))
            {
                if (lastSuccessful >= 0 && c.Now - lastSuccessful > 500 && c.Air)
                    foreach (Move move in ShoreKeeperDodge(c)) yield return move;
                if (c.FarmEchoTask) yield return Move.Press(BattleButton.Forward);
                yield return Move.Liberation(0, readyTimeout: 400, attackWhileDimmed: true, raw: true);
                bool succeeded = c.Succeeded;
                if (c.FarmEchoTask) yield return Move.Release(BattleButton.Forward);
                if (succeeded)
                {
                    lastSuccessful = c.Now;
                    if (consumePriming)
                    {
                        c.Memory.ResumeLiberation = c.Memory.LiberationPrimed = false;
                        c.Memory.Resource = 0;
                    }
                }
                else yield return Move.Wait(50);
            }
        }

        private static IEnumerable<Move> CarlottaZhezhiOutro(TurnContext c, FighterMemory zhezhi)
        {
            zhezhi.Resource = 0;
            ReadCarlottaForte(c);
            if (!c.Memory.LiberationPrimed)
            {
                while (!c.HeavyReady && c.Since(c.Started) <= 6000)
                {
                    if (ResonanceWithoutCooldown(c.Hud)) yield return CarlottaResonance();
                    else yield return Move.Tap(Attack, 100);
                }
            }
            if (c.HeavyReady)
            {
                yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                if (c.Succeeded)
                {
                    c.Memory.LiberationPrimed = true;
                    c.Memory.Resource = 0;
                }
            }
            bool liberated = false, usedResonance = false;
            if (c.Memory.LiberationPrimed)
            {
                while (c.Since(c.Started) < 14000)
                {
                    if (!liberated && CarlottaLiberationReady(c.Hud))
                    {
                        foreach (Move action in CarlottaBurst(c, true)) yield return action;
                        liberated = !c.Memory.LiberationPrimed;
                        if (liberated) yield return Move.Wait(200);
                    }
                    yield return CarlottaResonance();
                    if (c.Succeeded)
                    {
                        yield return Move.Attacks(800);
                        c.Memory.Resource++;
                        usedResonance = true;
                    }
                    yield return Move.Tap(Attack, 100);
                    if (!CarlottaLiberationReady(c.Hud) && !ResonanceWithoutCooldown(c.Hud) && usedResonance) break;
                }
            }
            yield return Move.Echo(2000);
            if (c.Succeeded) c.Memory.CarlottaSwitchAt = c.Now;
            c.Memory.ResumeLiberation = !liberated;
        }

        internal static IEnumerable<Move> Zhezhi(TurnContext c)
        {
            FighterMemory carlotta = c.FarmEchoTask ? null : c.Ally("Carlotta");
            if (carlotta == null)
            {
                if (c.Intro) yield return Move.Attacks(1500);
                yield return Move.Liberation();
                if ((c.Memory.Phase == 1 || c.Hud.EnhancedColor) && ZhezhiResonanceAvailable(c.Hud))
                {
                    c.Memory.Phase = 0;
                    foreach (Move action in ZhezhiBlueChain(c, null)) yield return action;
                    yield break;
                }
                if (ZhezhiResonanceAvailable(c.Hud) && c.Hud.ForteFullColor)
                {
                    yield return Move.Resonance();
                    yield return Move.Attacks(800);
                    c.Memory.Phase = 1;
                    yield break;
                }
                yield return Move.Echo();
                if (!c.Succeeded) yield return Move.Attacks(100);
                yield break;
            }
            if (c.Intro) yield return Move.Attacks(1300);
            if (c.Air)
            {
                yield return Move.Attacks(1200, Grounded);
                yield return Move.Tap(Dodge, 50);
            }
            if (!c.Hud.EnhancedColor && ReadZhezhiForte(c) < 3 && !(c.Full && carlotta.CarlottaReady(c.Clock, c.Now)))
            {
                yield return Move.Attacks(1400);
                if (!carlotta.CarlottaReady(c.Clock, c.Now)) yield break;
            }
            if (!c.Hud.EnhancedColor && ZhezhiResonanceAvailable(c.Hud) && ReadZhezhiForte(c) > 1 &&
                !(c.Full && carlotta.CarlottaReady(c.Clock, c.Now)))
            {
                if (ZhezhiConcertoAllows(c, carlotta))
                {
                    yield return Move.Liberation();
                    if (c.Succeeded) yield return Move.Wait(200);
                }
                yield return Move.Resonance();
                yield return Move.Attacks(800);
            }
            if (ZhezhiConcertoAllows(c, carlotta) && c.Hud.EnhancedColor && ZhezhiResonanceAvailable(c.Hud))
            {
                foreach (Move action in ZhezhiBlueChain(c, carlotta)) yield return action;
                bool liberated = false;
                if (ZhezhiConcertoAllows(c, carlotta))
                {
                    yield return Move.Liberation(500);
                    liberated = c.Succeeded;
                }
                if (liberated) yield return Move.Wait(200);
                else if (EchoWithoutCooldown(c.Hud)) yield return Move.Tap(Dodge, 50);
            }
            yield return Move.Echo(2000);
        }

        private static int ReadZhezhiForte(TurnContext c)
        {
            c.Memory.Resource = c.Hud.ForteSegments;
            return c.Hud.ForteSegments;
        }

        private static bool ZhezhiResonanceAvailable(HudSnapshot h) { return h.EnhancedColor || ResonanceWithoutCooldown(h); }

        private static bool ZhezhiConcertoAllows(TurnContext c, FighterMemory carlotta)
        {
            return carlotta.CarlottaReady(c.Clock, c.Now) || c.Concerto < .6 || c.Full;
        }

        private static IEnumerable<Move> ZhezhiBlueChain(TurnContext c, FighterMemory carlotta)
        {
            long start = c.Now;
            yield return Move.Wait(300, h => h.EnhancedColor);
            long nextJump = c.Now + 400;
            yield return Move.Press(Attack);
            while (c.Hud.EnhancedColor)
            {
                yield return Move.Tap(Skill, 50);
                if (c.NeedFastPerform && c.Now - start > 1100 || c.Now - start > 4000 ||
                    c.Full && (carlotta == null || ZhezhiConcertoAllows(c, carlotta))) break;
                if (c.Now > nextJump)
                {
                    yield return Move.Tap(Jump, 10);
                    nextJump += 400;
                }
            }
            yield return Move.Release(Attack);
        }
    }
}
