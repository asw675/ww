using System;
using System.Collections.Generic;

namespace TideCombat
{
    internal static partial class Rotations
    {
        internal static IEnumerable<Move> Brant(TurnContext c)
        {
            if (c.Intro)
            {
                yield return Move.Attacks(1300);
                if (c.SubDpsIntro && c.Outro == "Lupa")
                {
                    foreach (Move action in BrantLupaOutro(c)) yield return action;
                    yield break;
                }
            }
            foreach (Move action in BreakEnemy(c)) yield return action;
            if (BrantForteReady(c.Hud))
            {
                yield return Move.Repeat(Skill, 1000, h => !BrantForteReady(h));
                c.Memory.LastLiberation = -1;
                c.Memory.BrantAnchor = c.Now;
                if (EchoWithoutCooldown(c.Hud)) yield return Move.Echo();
            }
            if (!c.Hud.ForteFullColor && !c.NeedFastPerform)
            {
                yield return Move.Liberation();
                if (c.Succeeded) yield return Move.Attacks(800);
            }
            bool attack = c.Hud.LevitatorKnown && !c.Air;
            if (attack)
            {
                foreach (Move action in BrantFlick(c)) yield return action;
                yield return Move.Wait(200);
            }
            long start = c.Now;
            while (c.Now - start <= 1300)
            {
                yield return Move.Tap(attack ? Attack : Jump, 50);
                attack = !attack;
            }
            if (BrantForteReady(c.Hud))
            {
                yield return Move.Repeat(Skill, 1000, h => !BrantForteReady(h));
                c.Memory.BrantAnchor = c.Now;
                yield break;
            }
            if (c.Since(c.Memory.LastLiberation) >= 12000 && EchoWithoutCooldown(c.Hud)) yield return Move.Echo();
        }

        private static bool BrantForteReady(HudSnapshot h) { return h.ForteFullColor && ResonanceWithoutCooldown(h); }

        private static IEnumerable<Move> BrantFlick(TurnContext c)
        {
            if (ResonanceWithoutCooldown(c.Hud)) yield return Move.Attacks(200, h => h.ResonanceWhiteRatio > 0);
            if (c.Hud.ResonanceWhiteRatio > 0 && ResonanceWithoutCooldown(c.Hud))
                yield return Move.Repeat(Skill, 200, h => !ResonanceWithoutCooldown(h));
        }

        private static IEnumerable<Move> BrantLupaOutro(TurnContext c)
        {
            long start = c.Now;
            bool liberated = c.Since(c.Memory.LastLiberation) < 12000;
            int limit = liberated ? 10000 : 1500;
            do
            {
                if (BrantForteReady(c.Hud))
                {
                    yield return Move.Repeat(Skill, 1000, h => !BrantForteReady(h));
                    c.Memory.LastLiberation = -1;
                    if (c.Air) yield return Move.Attacks(4000, Grounded);
                    if (liberated) break;
                }
                if (c.RReady)
                {
                    yield return Move.Liberation();
                    if (c.Succeeded)
                    {
                        start = c.Now;
                        limit = 10000;
                        liberated = true;
                    }
                }
                if (c.Now - start > limit) break;
                if (c.Hud.LevitatorKnown && !c.Air)
                {
                    foreach (Move action in BrantFlick(c)) yield return action;
                    yield return Move.Wait(200);
                }
                else if (!c.Hud.LevitatorKnown) yield return Move.Tap(Jump, 0);
                yield return Move.Tap(Attack, 50);
            } while (true);
            yield return Move.Echo();
        }

        internal static IEnumerable<Move> Lupa(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1000);
            bool changli = c.Intro && c.Outro == "Changli";
            yield return Move.Echo(0);
            if (c.Has("lupa_wolf_icon2") || c.Memory.Phase == 1)
            {
                foreach (Move action in LupaWolf(c)) yield return action;
                if (!changli) yield break;
            }
            if (c.Hud.ForteSegments == 2 && !c.Has("lupa_wolf_icon2"))
            {
                foreach (Move action in LupaHeavy(c)) yield return action;
                if (!changli) yield break;
                foreach (Move action in LupaOutroWolf(c)) yield return action;
            }
            if (c.Hud.ResonanceWhiteRatio > .05)
            {
                yield return Move.Resonance();
                if (c.Succeeded)
                {
                    c.Memory.LastLiberation = -1;
                    if (!c.RReady) yield break;
                    foreach (Move action in WaitDown(c)) yield return action;
                }
            }
            foreach (Move action in BreakEnemy(c)) yield return action;
            if (changli || !c.NeedFastPerform)
            {
                yield return Move.Liberation();
                if (c.Succeeded)
                {
                    yield return Move.Attacks(changli ? 1300 : 300);
                    if (!changli) yield break;
                }
            }
            if (c.Since(c.Memory.LastLiberation) < 12000)
            {
                long start = c.Now;
                bool jump = true;
                while (c.Now - start <= 4000)
                {
                    yield return Move.Tap(jump ? Jump : Attack, 50);
                    jump = !jump;
                    if (c.Hud.ForteSegments == 2) break;
                }
                foreach (Move action in LupaHeavy(c)) yield return action;
                if (changli) foreach (Move action in LupaOutroWolf(c)) yield return action;
            }
            else yield return Move.Attacks(100);
        }

        private static IEnumerable<Move> LupaHeavy(TurnContext c)
        {
            bool airborne = c.Air;
            if (!airborne) yield return Heavy(600, null, 0);
            yield return Move.Attacks(airborne ? 2000 : 1400, h => !h.ForteFullColor);
            if (!c.Hud.ForteFullColor) c.Memory.Phase = 1;
        }

        private static IEnumerable<Move> LupaOutroWolf(TurnContext c)
        {
            yield return Move.Wait(500, h => h.Shows("lupa_wolf_icon2"));
            if (c.Has("lupa_wolf_icon2") || c.Memory.Phase == 1)
                foreach (Move action in LupaWolf(c)) yield return action;
        }

        private static IEnumerable<Move> LupaWolf(TurnContext c)
        {
            long start = c.Now;
            while (c.Now - start <= 1000 && (c.Has("lupa_wolf_icon2") || c.Memory.Phase == 1 && c.Now - start < 200))
                yield return Move.Tap(Skill, 100);
            c.Memory.LastLiberation = -1;
            c.Memory.Phase = 0;
            yield return Move.Wait(1200);
        }

        internal static IEnumerable<Move> Changli(TurnContext c)
        {
            c.Memory.ExecuteWhenSwitching = true;
            if (c.Intro)
            {
                yield return Move.Attacks(300);
                c.Memory.Phase = 1;
            }
            int forte = ChangliForte(c.Hud);
            if (c.Memory.Phase == 1)
            {
                yield return Move.Attacks(200);
                yield return Move.Wait(200);
                if (c.Intro && c.Outro == "Brant")
                {
                    yield return Move.Wait(200);
                    foreach (Move action in ChangliBrantOutro(c, ChangliForte(c.Hud))) yield return action;
                    yield break;
                }
                if (forte == 3)
                {
                    yield return Move.Wait(200);
                    forte = ChangliForte(c.Hud);
                }
            }
            c.Memory.Phase = 0;
            if (forte == 4 || c.HeavyReady)
            {
                if (c.Air) yield return Heavy(600, null, 0);
                if (c.HeavyReady) yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                c.Memory.ExecuteWhenSwitching = false;
                yield break;
            }
            if (!(forte >= 3 && ResonanceWithoutCooldown(c.Hud)) && c.Hud.Skills[2] == SkillState.Ready)
            {
                yield return ChangliLiberation();
                if (c.Succeeded)
                {
                    foreach (Move action in ChangliFinishLiberation(c)) yield return action;
                    c.Memory.ExecuteWhenSwitching = false;
                    yield break;
                }
            }
            if (ChangliCanFlick(c.Hud))
            {
                yield return Move.Repeat(Skill, 200, h => !ResonanceWithoutCooldown(h));
                c.Memory.Phase = 1;
                yield break;
            }
            yield return Move.Echo();
            if (!c.Succeeded) yield return Move.Attacks(100);
        }

        private static int ChangliForte(HudSnapshot h) { return HeavyHint(h) ? 4 : h.ForteSegments; }
        private static bool ChangliCanFlick(HudSnapshot h) { return h.ResonanceWhiteRatio > 0 && ResonanceWithoutCooldown(h); }

        private static Move ChangliLiberation()
        {
            return Move.Liberation(0, AnimationInput.HoldAttack, readyTimeout: 5000, raw: true, animationDelay: 1500);
        }

        private static IEnumerable<Move> ChangliFinishLiberation(TurnContext c)
        {
            yield return Move.Wait(600, HeavyHint);
            yield return Move.Wait(600, h => !HeavyHint(h));
            if (c.AnimationAttackHeld) yield return Move.Release(Attack);
        }

        private static IEnumerable<Move> ChangliBrantOutro(TurnContext c, int forte)
        {
            if (forte == 3)
            {
                long start = c.Now, lastUnfilled = c.Now;
                bool tryResonance = true;
                while (c.Now - start < 5000)
                {
                    if (tryResonance && ChangliCanFlick(c.Hud))
                    {
                        yield return Move.Repeat(Skill, 200, h => !ResonanceWithoutCooldown(h));
                        tryResonance = false;
                        continue;
                    }
                    yield return Move.Tap(Attack, 100);
                    if (!c.HeavyReady) lastUnfilled = c.Now;
                    else if (c.Now - lastUnfilled > 200) break;
                }
                if (c.HeavyReady)
                {
                    yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                    yield return Move.Wait(1000);
                }
            }
            else if (forte >= 4 || c.HeavyReady)
            {
                if (c.Air) yield return Heavy(600, null, 0);
                if (c.HeavyReady) yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                forte = 0;
                yield return Move.Wait(1000);
            }
            if (c.Hud.Skills[2] == SkillState.Ready)
            {
                yield return ChangliLiberation();
                if (c.Succeeded)
                {
                    foreach (Move action in ChangliFinishLiberation(c)) yield return action;
                    yield return Move.Wait(600);
                    forte = 0;
                }
            }
            if (forte < 3 && ChangliCanFlick(c.Hud))
            {
                yield return Move.Repeat(Skill, 200, h => !ResonanceWithoutCooldown(h));
                c.Memory.Phase = 1;
            }
            else yield return Move.Echo();
        }

        internal static IEnumerable<Move> Cantarella(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1200);
            bool hasOutro = c.SubDpsIntro && (c.Outro == "Roccia" || c.Outro == "Sanhua");
            yield return Move.Liberation();
            if (c.HeavyReady || !c.Hud.CantarellaForte)
            {
                yield return Move.Resonance();
                if (hasOutro && c.Air) foreach (Move move in WaitDown(c)) yield return move;
                if (!c.Air && c.HeavyReady)
                {
                    yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                    if (c.Succeeded) c.Memory.LastHeavy = c.Now;
                }
                else
                {
                    yield return Move.Echo();
                    if (!c.Succeeded) yield return Move.Attacks(100);
                    yield break;
                }
            }
            long lastForte = c.Now;
            int retryAfter = -100;
            bool holding = false;
            while (c.Since(c.Memory.LastHeavy) < 8000 && !c.HeavyReady)
            {
                long frameAt = c.Now;
                if (ResonanceWithoutCooldown(c.Hud))
                {
                    yield return Move.Resonance(sendClick: false);
                    if (c.Succeeded && !hasOutro)
                    {
                        if (holding) yield return Move.Release(Attack);
                        yield break;
                    }
                }
                if (!hasOutro && c.NeedFastPerform && c.Since(c.Started) > 1100) break;
                if (frameAt - lastForte > retryAfter)
                {
                    if (holding) yield return Move.Release(Attack, 200);
                    else yield return Move.Wait(200);
                    yield return Move.Press(Attack);
                    holding = true;
                    retryAfter += 1000;
                }
                if (c.Hud.CantarellaForte) lastForte = frameAt;
                else if (frameAt - lastForte > 500) break;
                yield return Move.Wait(50);
            }
            if (holding) yield return Move.Release(Attack);
            yield return Move.Echo();
        }

        internal static IEnumerable<Move> Ciaccona(TurnContext c)
        {
            c.Memory.InRecital = false;
            if (c.Intro) yield return Move.Attacks(1500, interval: 100);
            if (c.Hud.EchoWhiteRatio < .22) yield return Move.Echo(0);
            if (!c.Intro && !c.HeavyReady)
            {
                long jumpStart = c.Now;
                while (c.Now - jumpStart <= 400)
                {
                    yield return Move.Tap(Attack, 40);
                    if (c.Now - jumpStart <= 400) yield return Move.Tap(Jump, 10);
                }
                if (c.Air) yield return Move.Attacks(1200, Grounded, 100);
                yield return Move.Attacks(200, interval: 100);
            }
            yield return Move.Resonance();
            bool launched = c.Succeeded;
            bool wait = launched;
            if (c.HeavyReady || c.Hud.ForteSegments >= 3)
            {
                if (!launched && !c.Air) yield return Move.Repeat(Jump, 300, h => h.Airborne, 50);
                if (c.HeavyReady) yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                wait = true;
            }
            if (c.RReady)
            {
                if (wait) yield return Move.Wait(400);
                yield return Move.Liberation();
                c.Memory.InRecital = c.Succeeded;
                string partner = CiacconaPartner(c.Team);
                if (c.Memory.InRecital && (partner == "Zani" || partner == "Phoebe"))
                    yield return Move.Repeat(BattleButton.Left, 600, interval: 100);
            }
            if (!c.Memory.InRecital && c.Hud.EchoWhiteRatio > .25) yield return Move.Echo();
        }

        internal static string CiacconaPartner(FighterMemory[] team)
        {
            foreach (FighterMemory fighter in team)
                if (fighter.Id == "Cartethyia" || fighter.Id == "Phoebe" || fighter.Id == "Zani") return fighter.Id;
            return null;
        }

        internal static IEnumerable<Move> Phrolova(TurnContext c)
        {
            c.Memory.LastLiberation = -1;
            bool cantarella = c.Intro && c.Outro == "Cantarella";
            if (c.Intro)
            {
                c.Memory.Phase = 0;
                yield return Move.Attacks(1700);
                yield return Move.Repeat(Dodge, 100);
            }
            if (c.Air) foreach (Move move in WaitDown(c)) yield return move;
            if (c.RReady)
            {
                yield return Move.Liberation(0);
                if (c.Succeeded) yield break;
            }
            if (c.HeavyReady)
            {
                yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                if (c.Succeeded)
                {
                    foreach (Move action in PhrolovaLiberation(c)) yield return action;
                    yield break;
                }
            }
            if (ResonanceWithoutCooldown(c.Hud) || c.Memory.Phase == 1)
            {
                foreach (Move move in PhrolovaResonance(c, false)) yield return move;
            }
            c.Memory.Phase = 0;
            long start = c.Now;
            while (cantarella ? c.Since(c.Started) < 16000 : c.Now - start < 4000)
            {
                if (c.RReady)
                {
                    yield return Move.Liberation(0);
                    if (c.Succeeded) yield break;
                }
                if (c.Air) foreach (Move move in ShoreKeeperDodge(c)) yield return move;
                if (c.HeavyReady)
                {
                    yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                    if (c.Succeeded)
                    {
                        foreach (Move action in PhrolovaLiberation(c)) yield return action;
                        yield break;
                    }
                }
                if (c.Now - start > 1000 && ResonanceWithoutCooldown(c.Hud) && (!cantarella || !c.Air))
                {
                    if (!cantarella)
                    {
                        c.Memory.Phase = 1;
                        yield break;
                    }
                    foreach (Move move in PhrolovaResonance(c, true)) yield return move;
                }
                yield return Move.Tap(Attack, 50);
            }
        }

        private static IEnumerable<Move> PhrolovaLiberation(TurnContext c)
        {
            if (!c.MayUseLiberation) yield break;
            long until = c.Now + 3000;
            do
            {
                yield return Move.Liberation(0);
                if (c.Succeeded) yield break;
                yield return Move.Wait(100, h => h.Skills[2] == SkillState.Ready);
            } while (c.Now < until);
        }

        private static IEnumerable<Move> PhrolovaResonance(TurnContext c, bool linked)
        {
            yield return Move.Attacks(linked ? 300 : 100);
            yield return Move.Resonance();
            if (linked && !c.Succeeded) yield break;
            yield return Move.Attacks(100);
            yield return Move.Attacks(300, h => !ResonanceWithoutCooldown(h) || linked && h.Airborne);
            yield return Move.Echo();
            if (!c.Succeeded) yield return Move.Repeat(Dodge, 100);
        }

        internal static IEnumerable<Move> Mornye(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1330);
            c.Memory.ExecuteWhenSwitching = true;
            if (c.Hud.LongActionLayout != 2 && c.Since(c.Memory.LastHeavy) < 23000)
            {
                yield return Move.Echo();
                if (!c.Succeeded) yield return Move.Resonance();
                if (!c.Succeeded) yield return Move.Attacks(100);
                yield break;
            }
            long started = c.Now;
            bool dodged = false;
            while (c.Hud.LongActionLayout != 2 && c.Now - started < 10000)
            {
                yield return Move.Tap(Attack, 0);
                yield return Move.Resonance();
                yield return Move.Wait(100);
                if (c.HeavyReady)
                {
                    if (!dodged) yield return Move.Tap(Dodge, 0);
                    dodged = true;
                    yield return Heavy(600, null, 310);
                }
            }

            started = c.Now;
            bool echoWasReady = false;
            Func<HudSnapshot, bool> elbow = h =>
            {
                if (!h.SkillLit[1] || !EchoWithoutCooldown(h)) return echoWasReady;
                echoWasReady = true;
                return false;
            };
            while (c.Hud.LongActionLayout == 2 && c.Now - started < 10000)
            {
                if (elbow(c.Hud)) yield return Move.Repeat(Dodge, 1500, h => !elbow(h), 50);
                yield return Move.Liberation();
                if (c.Hud.LongActionLayout == 2 && c.HeavyReady && !elbow(c.Hud))
                {
                    yield return Move.Hold(Attack, 2000, h => !HeavyHint(h) || elbow(h), 50, 0);
                    if (c.Succeeded)
                    {
                        if (elbow(c.Hud)) continue;
                        yield return Move.Wait(1500, Full);
                        long filling = c.Now;
                        while (!c.Full && c.Now - filling < 2000 && c.Hud.LongActionLayout == 2)
                        {
                            yield return Move.Attacks(500);
                            if (elbow(c.Hud)) break;
                        }
                        if (!c.Full) yield return Move.Echo(repeatFor: 200);
                        c.Memory.LastHeavy = c.Now;
                        c.Memory.ExecuteWhenSwitching = false;
                        yield break;
                    }
                }
                yield return Move.Resonance();
                yield return Move.Tap(Attack, 10);
            }
        }

        internal static IEnumerable<Move> Linnai(TurnContext c)
        {
            if (c.Intro && c.Hud.LinnaiAccelerating) yield return Move.Attacks(1330);
            else
            {
                yield return Move.Attacks(1000);
                yield return Move.Echo(0);
                if (!c.Full) yield return Move.Liberation();
                if (!c.HeavyReady) yield return Move.Resonance();
                yield return Move.Attacks(2000, HeavyHint);
                yield return Move.Press(Attack);
                yield return Move.Wait(5000, h => !HeavyHint(h));
                bool releasedHeavy = c.Succeeded;
                yield return Move.Release(Attack, releasedHeavy ? 400 : 0);
                if (releasedHeavy)
                {
                    yield return Move.Attacks(c.Intro && c.Outro == "Aemeath" ? 1600 : 1000,
                        h => h.LinnaiAccelerating);
                    if (c.Hud.LinnaiAccelerating)
                        foreach (Move move in LinnaiAccelerate(c)) yield return move;
                }
            }
            if (c.RReady) yield return Move.Liberation();
        }

        private static IEnumerable<Move> LinnaiAccelerate(TurnContext c)
        {
            yield return Move.Attacks(1000, h => h.LinnaiColorFull || h.ConcertoFull);
            yield return Move.Repeat(Jump, 3000, h => !h.ForteFullColor);
            if (c.Succeeded && !c.Hud.ForteFullColor)
            {
                foreach (Move move in LinnaiResonanceKick(c, 2000, false)) yield return move;
                if (c.Succeeded)
                {
                    yield return Move.Wait(300);
                    foreach (Move move in WaitDown(c)) yield return move;
                    foreach (Move move in LinnaiResonanceKick(c, 3000, true)) yield return move;
                    if (c.Succeeded)
                    {
                        yield return Move.Wait(300);
                        foreach (Move move in WaitDown(c)) yield return move;
                    }
                }
            }
            if (!c.Full)
            {
                yield return Move.Liberation();
                if (c.Succeeded) yield return Move.Attacks(1200, Full);
            }
        }

        private static IEnumerable<Move> LinnaiResonanceKick(TurnContext c, int duration, bool stopWhenFull)
        {
            long start = c.Now;
            while (c.Now - start < duration && (!stopWhenFull || !c.Full))
            {
                yield return Move.Resonance();
                if (c.Succeeded) yield break;
                yield return Move.Tap(Attack, 50);
            }
            c.Succeeded = false;
        }

        internal static IEnumerable<Move> Luhesi(TurnContext c)
        {
            yield return Move.Attacks(1100);
            if (!c.SubDpsIntro)
            {
                yield return Move.Tap(Skill);
                yield break;
            }
            c.Memory.LastIntro = c.Now;
            if (!c.Air) yield return Move.Repeat(Jump, 200, h => h.Airborne);
            bool detectElbow = c.QReady, triedJump = false;
            int kicks = 0;
            long start = c.Now;
            while (c.Since(start) < 12000)
            {
                if (LuhesiElbow(c.Hud, detectElbow))
                    yield return Move.Repeat(Dodge, 1500, h => !LuhesiElbow(h, detectElbow), 50);
                else
                {
                    if (c.Has("luhesi_kick"))
                    {
                        long attackStart = c.Now;
                        while (c.Has("luhesi_kick") && c.Now - attackStart < 3000)
                        {
                            if (kicks < 3) foreach (Move action in BreakEnemy(c)) yield return action;
                            yield return Move.Tap(Attack, 100);
                        }
                        if (kicks >= 3)
                        {
                            foreach (Move action in LuhesiLiberation(c)) yield return action;
                            yield break;
                        }
                    }
                    if (triedJump && c.Hud.LongActionLayout != 2)
                    {
                        yield return Move.Repeat(Skill, 1000, h => h.Shows("luhesi_kick") || LuhesiElbow(h, detectElbow));
                        if (LuhesiElbow(c.Hud, detectElbow)) continue;
                        triedJump = false;
                        if (c.Has("luhesi_kick")) kicks++;
                        else
                        {
                            if (kicks == 2) foreach (Move action in WaitDown(c)) yield return action;
                            foreach (Move action in LuhesiLiberation(c)) yield return action;
                            yield break;
                        }
                    }
                    else if (c.Hud.LongActionLayout == 2)
                    {
                        yield return Move.Tap(Jump, 10);
                        triedJump = true;
                    }
                    else yield return Move.Tap(Attack, 10);
                }
                yield return Move.Wait(10);
            }
        }

        private static bool LuhesiElbow(HudSnapshot h, bool detect) { return detect && !h.SkillLit[1]; }

        private static IEnumerable<Move> LuhesiLiberation(TurnContext c)
        {
            if (!c.Hud.LuhesiLiberation || c.Hud.Skills[2] == SkillState.Cooling) yield break;
            yield return Move.Liberation(0);
            if (c.Succeeded) foreach (Move action in BreakEnemy(c)) yield return action;
        }

        internal static IEnumerable<Move> Iuno(TurnContext c)
        {
            foreach (Move action in WaitDown(c)) yield return action;
            long start = c.Now;
            int duration = c.Intro ? 5500 : 1500;
            bool jumped = false, extra = false, lastWasAttack = true;
            yield return Move.Echo();
            while (c.Since(start) < duration)
            {
                long cycleStart = c.Now;
                if (c.Since(c.Memory.LastHeavy) > 20000 && c.Has("iuno_heavy"))
                {
                    while (c.Has("iuno_heavy"))
                    {
                        yield return Move.Wait(50);
                        yield return Heavy(600, null, 50);
                    }
                    c.Memory.LastHeavy = c.Now;
                    if (!c.Settings.IunoC6 || extra) yield break;
                    extra = true;
                    start = c.Now;
                    duration = 5000;
                }
                if (!jumped && c.Has("iuno_jump"))
                {
                    while (c.Has("iuno_jump")) yield return Move.Tap(Jump, 100);
                    jumped = true;
                    if (!c.Intro) yield break;
                    duration += 3000;
                    continue;
                }
                if (c.Since(c.Memory.LastLiberation) > 20000)
                {
                    yield return Move.Liberation(0);
                    if (c.Succeeded)
                    {
                        start = c.Now;
                        duration = 3000;
                    }
                }
                yield return Move.Tap(lastWasAttack ? Skill : Attack, 0);
                lastWasAttack = !lastWasAttack;
                if (c.Now - cycleStart < 100) yield return Move.Wait(100 - (int)(c.Now - cycleStart));
            }
        }

        internal static IEnumerable<Move> Jinhsi(TurnContext c)
        {
            if (c.Memory.Phase == 2)
            {
                c.Memory.Phase = 0;
                yield return Move.Incarnation();
                yield return Move.Echo();
                if (!c.Succeeded) yield return Move.Tap(Attack, 0);
                if (c.IncarnationFreezeAt >= 0) c.Clock.Record(c.IncarnationFreezeAt, c.Now - c.IncarnationFreezeAt, c.Now);
                yield break;
            }
            if (!c.Intro && c.Memory.Phase != 1)
            {
                yield return Move.Echo();
                yield break;
            }
            long start = c.Now;
            // The reference's intro/flight helper has no 6s phase timer.
            // A permanently stuck input is still bounded by the common 15s
            // no-progress limit, never by a fabricated early phase transition.
            while (true)
            {
                long elapsed = c.Now - start;
                if (c.Hud.Skills[0] == SkillState.Cooling)
                {
                    if (elapsed > 300 && elapsed < 1500)
                    {
                        c.Memory.Phase = 1;
                        yield return Move.Echo();
                        if (!c.Succeeded) yield return Move.Tap(Attack, 0);
                        yield break;
                    }
                    if (elapsed > 1500) break;
                    yield return Move.Wait(50);
                }
                else
                {
                    if (elapsed >= 15000) yield break;
                    yield return Move.Tap(Skill, 100);
                }
            }
            yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
            yield return Move.Attacks(c.Succeeded ? 300 : 1400);
            c.Memory.Phase = 2;
        }

        internal static IEnumerable<Move> Encore(TurnContext c)
        {
            if (c.Intro)
            {
                long sinceLib = c.Since(c.Memory.LastLiberation);
                if (sinceLib > 6000 && sinceLib < 10000 && c.HeavyReady)
                {
                    yield return Heavy(1200, null, 100);
                    c.Memory.LastHeavy = c.Now;
                    yield break;
                }
                foreach (Move action in WaitDown(c)) yield return action;
            }
            if (c.Since(c.Memory.LastLiberation) < 9500)
            {
                foreach (Move action in EncoreLiberatedChain(c)) yield return action;
                yield break;
            }
            yield return Move.Resonance();
            if (c.Succeeded && c.Since(c.Memory.EncoreStageAt, true) >= 4000)
            {
                c.Memory.EncoreStageAt = c.Now;
                yield break;
            }
            if (!c.IsOpenWorldAutoCombat && !c.NeedFastPerform)
            {
                yield return Move.Liberation(400);
                if (c.Succeeded)
                {
                    foreach (Move action in EncoreLiberatedChain(c)) yield return action;
                    yield break;
                }
            }
            if (EchoWithoutCooldown(c.Hud)) yield return Move.Echo();
        }

        private static IEnumerable<Move> EncoreLiberatedChain(TurnContext c)
        {
            yield return Move.Resonance();
            if (c.Since(c.Memory.LastLiberation) < 6000) yield return Move.Attacks(c.Succeeded ? 2700 : 2400);
            else if (c.HeavyReady)
            {
                yield return Heavy(600, null, 0);
                c.Memory.LastHeavy = c.Now;
            }
            else yield return Move.Resonance();
        }

        internal static IEnumerable<Move> Xigelika(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(770);
            else foreach (Move move in WaitDown(c)) yield return move;
            long start = c.Now;
            int length = c.SubDpsIntro ? 15000 : 500;
            while (c.Since(start) < length)
            {
                yield return Move.Echo(0);
                if (c.Air) foreach (Move move in ShoreKeeperDodge(c)) yield return move;
                if (c.Hud.XigelikaForte)
                {
                    long heavyStart = c.Now;
                    while (c.Hud.XigelikaForte && c.Since(heavyStart) < 3000)
                    {
                        if (c.Long)
                            yield return Move.Hold(Attack, 1200, h => h.LongActionLayout != 1, 10, 0);
                        else if (c.Hud.Skills[0] != SkillState.Cooling)
                            yield return Move.Hold(Skill, 1200, h => !h.XigelikaForte, 10, 0);
                        else yield return Move.Tap(Attack, 150);
                    }
                    yield break;
                }
                if (!c.Intro)
                {
                    yield return Move.Liberation(0);
                    if (c.Succeeded)
                    {
                        foreach (Move action in BreakEnemy(c)) yield return action;
                        length = 15000;
                        start = c.Now;
                        yield return Move.Wait(50);
                        continue;
                    }
                }
                yield return Move.Resonance(2000, sendClick: false);
                if (!c.Succeeded) yield return Move.Tap(Attack, 100);
                yield return Move.Wait(50);
            }
        }

        internal static IEnumerable<Move> Rebecca(TurnContext c)
        {
            if (c.Memory.FormUntil > c.Now)
            {
                yield return Move.Resonance();
                yield return Move.Attacks(500);
                foreach (Move move in RebeccaFinishConcerto(c)) yield return move;
                yield break;
            }
            yield return Move.Attacks(c.Intro ? 1300 : 1800);
            int turns = c.Intro ? 2 : 3;
            yield return Move.Wait(2000, ResonanceWithoutCooldown);
            for (int trigger = 0; trigger < turns; trigger++)
                yield return Move.Resonance(postSleep: trigger == 2 ? 1000 : 1500);
            yield return Move.Tap(Skill, 0);
            yield return Move.Attacks(4000, HeavyHint);
            if (c.HeavyReady) yield return Heavy(1500, null, 10);
            yield return Move.Echo();
            c.ContinueAfterLockLossUntil = c.Now + 6000;
            yield return Move.RawLiberationPress(800);
            c.Memory.LastLiberation = c.Now;
            long end = c.Now + 5200;
            long nextLiberation = c.Now + 900;
            while (c.Now < end)
            {
                yield return Move.Tap(Attack, 90);
                if (c.Now > nextLiberation)
                {
                    nextLiberation = c.Now + 900;
                    yield return Move.RawLiberationPress();
                }
            }
            c.ContinueAfterLockLossUntil = 0;
            c.Memory.FormUntil = c.Now + 17000;
            foreach (Move move in RebeccaFinishConcerto(c)) yield return move;
        }

        private static IEnumerable<Move> RebeccaFinishConcerto(TurnContext c)
        {
            long end = c.Now + 2200;
            while (!c.Full && c.Now < end)
            {
                yield return Move.Attacks(1000);
                yield return Move.Wait(100);
            }
        }
    }
}
