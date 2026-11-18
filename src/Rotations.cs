using System;
using System.Collections.Generic;

namespace TideCombat
{
    internal static partial class Rotations
    {
        private const BattleButton Skill = BattleButton.Resonance;
        private const BattleButton Echo = BattleButton.Echo;
        private const BattleButton Liberation = BattleButton.Liberation;
        private const BattleButton Attack = BattleButton.Attack;
        private const BattleButton Jump = BattleButton.Jump;
        private const BattleButton Dodge = BattleButton.Dodge;
        private const BattleButton Interact = BattleButton.Interact;

        private static bool Full(HudSnapshot hud) { return hud.ConcertoFull; }
        private static bool HeavyHint(HudSnapshot hud) { return hud.Shows("mouse_forte"); }
        private static bool ForteFull(HudSnapshot hud) { return hud.Forte >= .96 || hud.Shows("e_forte") || HeavyHint(hud); }
        private static bool Grounded(HudSnapshot hud) { return !hud.Airborne; }
        private static bool ResonanceWithoutCooldown(HudSnapshot h)
        {
            return h.Skills[0] == SkillState.Ready || h.Skills[0] == SkillState.Unavailable;
        }
        private static bool EchoWithoutCooldown(HudSnapshot h)
        {
            return h.Skills[1] == SkillState.Ready || h.Skills[1] == SkillState.Unavailable;
        }

        internal static IEnumerable<Move> BreakEnemy(TurnContext c)
        {
            if (!c.ExecutionAvailable) yield break;
            long start = c.Now;
            do
            {
                yield return Move.Tap(Interact);
                yield return Move.ExecutionAttack();
            } while (c.Now - start < 500 || c.Now - start < 5000 && c.ExecutionAvailable);
        }

        internal static bool AllowsSwitchExecution(CharacterKit kit, FighterMemory memory, CombatSettings settings,
            FreezeClock clock, long now, bool afterSwitch)
        {
            if (kit == null) return false;
            if (memory.ExecuteWhenSwitching.HasValue) return memory.ExecuteWhenSwitching.Value;
            switch (kit.Id)
            {
                case "Brant": case "Lupa": case "Zani": case "Rebecca":
                    return false;
                case "Phrolova":
                    return afterSwitch || clock.Elapsed(memory.LastLiberation, now) >= 24000;
                case "Cantarella": case "ShoreKeeper": case "Mornye": case "Chisa":
                    return true;
                default:
                    return kit.RoleFor(settings) != BattleRole.Support;
            }
        }

        private static Move Heavy(int time = 650, Func<HudSnapshot, bool> until = null, int after = 110)
        {
            return Move.Hold(Attack, time, until, after);
        }

        private static IEnumerable<Move> WaitDown(TurnContext c, bool click = true)
        {
            if (!c.Hud.LevitatorKnown && c.Intro)
            {
                int duration = CharacterKit.IntroDuration(c.Memory.Id == "Rover" ? c.Memory.RoverForm : c.Memory.Id);
                yield return click ? Move.Attacks(duration) : Move.Wait(duration);
            }
            else if (c.Air)
                yield return click ? Move.Attacks(2500, Grounded, 200) : Move.Wait(2500, Grounded);
        }

        internal static IEnumerable<Move> Standard(TurnContext c)
        {
            if (c.Intro) yield return WaitIntro();
            yield return Move.Echo(0);
            yield return Move.Liberation();
            yield return Move.Resonance();
            if (!c.Succeeded && c.HeavyReady)
                yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
        }

        private static Move WaitIntro()
        {
            return Move.Attacks(1200, h => h.Skills[0] == SkillState.Ready || h.Skills[2] == SkillState.Ready ||
                h.SkillLit[0] && h.Skills[0] != SkillState.Cooling || h.SkillLit[2] && h.Skills[2] != SkillState.Cooling);
        }

        internal static IEnumerable<Move> Aalto(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(400);
            foreach (Move action in WaitDown(c)) yield return action;
            long start = c.Now;
            yield return Move.Resonance(1000);
            bool used = c.Succeeded;
            yield return Move.Echo(0);
            yield return Move.Liberation(0, AnimationInput.InteractAndAttack);
            bool liberated = c.Succeeded;
            while (c.Since(start) < 4500 && !c.Full)
            {
                if (!used && ResonanceWithoutCooldown(c.Hud))
                {
                    yield return Move.Resonance(1000);
                    used = c.Succeeded;
                }
                else if (!liberated && c.RReady)
                {
                    yield return Move.Liberation(0, AnimationInput.InteractAndAttack);
                    liberated = c.Succeeded;
                }
                else yield return Move.Tap(Attack, 100);
            }
        }

        internal static IEnumerable<Move> Baizhi(TurnContext c)
        {
            if (c.Intro)
            {
                yield return Move.Attacks(1200, ResonanceWithoutCooldown);
                if (ResonanceWithoutCooldown(c.Hud)) yield return Move.Resonance();
            }
            yield return Move.Liberation();
            yield return Move.Resonance();
            yield return Move.Echo();
        }

        internal static IEnumerable<Move> Calcharo(TurnContext c)
        {
            if (c.Intro) yield return Move.Wait(1000);
            foreach (Move move in Standard(c)) yield return move;
        }

        internal static IEnumerable<Move> Danjin(TurnContext c)
        {
            yield return Move.Liberation();
            if (c.Succeeded)
            {
                yield return Move.Wait(1200);
                yield return Move.Echo(2000);
                yield break;
            }
            if (c.Intro && c.Hud.ForteFullColor)
            {
                yield return Heavy(800, null, 200);
                yield return Move.Tap(Attack);
                yield break;
            }
            if (c.Intro) yield return Move.Attacks(1100);
            else
            {
                foreach (Move action in WaitDown(c)) yield return action;
                yield return Move.Attacks(400);
            }
            yield return Move.Repeat(Skill, 1100, interval: 200);
        }

        internal static IEnumerable<Move> Jianxin(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1000);
            yield return Move.Liberation();
            yield return Move.Resonance();
            yield return Move.Echo();
        }

        internal static IEnumerable<Move> Mortefi(TurnContext c)
        {
            foreach (Move action in WaitDown(c)) yield return action;
            yield return Move.Liberation();
            bool opened = c.Succeeded;
            yield return Move.Resonance();
            yield return Move.Echo();
            if (!opened) yield return Move.Liberation(1000);
        }

        internal static IEnumerable<Move> Taoqi(TurnContext c)
        {
            if (c.Intro)
            {
                foreach (Move action in WaitDown(c)) yield return action;
                yield return Move.Attacks(2500);
            }
            else
            {
                yield return Move.Liberation();
                yield return Move.Resonance();
                yield return Move.Echo();
            }
        }

        internal static IEnumerable<Move> Yuanwu(TurnContext c)
        {
            yield return Move.Liberation();
            if (c.Succeeded)
            {
                yield return Move.Resonance();
                yield break;
            }
            if (c.Intro) yield return Move.Attacks(1200);
            else
            {
                yield return Move.Resonance();
                yield return Move.Echo();
            }
        }

        internal static IEnumerable<Move> Yangyang(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(600);
            foreach (Move action in WaitDown(c)) yield return action;
            yield return Move.Resonance(1000);
            if (c.HeavyReady) yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
            yield return Move.Echo(0);
            yield return Move.Liberation(0, AnimationInput.InteractAndAttack);
            if (c.Succeeded) yield return Move.Attacks(500);
            else
            {
                yield return Move.Attacks(800, ResonanceWithoutCooldown);
                if (ResonanceWithoutCooldown(c.Hud)) yield return Move.Resonance();
            }
        }

        internal static IEnumerable<Move> YangyangSp(TurnContext c)
        {
            long start = c.Now, resonanceReadyAt = -1;
            int duration = c.Intro ? 8000 : 3200;
            bool echoUsed = false;
            yield return Move.Press(Attack);
            while (c.Since(start) < duration)
            {
                if (c.RReady)
                {
                    yield return Move.Liberation(0);
                    if (c.Succeeded) duration += 2000;
                }
                else if (ResonanceWithoutCooldown(c.Hud))
                {
                    if (resonanceReadyAt < 0) resonanceReadyAt = c.Now;
                    if (c.Now - resonanceReadyAt > 200) yield return Move.Resonance(1000, sendClick: false);
                }
                else
                {
                    resonanceReadyAt = -1;
                    if (!echoUsed)
                    {
                        yield return Move.Echo(0);
                        echoUsed = c.Succeeded;
                    }
                }
                yield return Move.Wait(50);
            }
            yield return Move.Release(Attack, 100);
        }

        internal static IEnumerable<Move> Yinlin(TurnContext c)
        {
            if (c.Intro) yield return Move.Wait(400);
            yield return Move.Liberation();
            bool liberation = c.Succeeded;
            if (c.HeavyReady)
            {
                if (!c.Intro && !liberation) yield return Move.Tap(Attack, 105);
                yield return Heavy(600, null, 400);
                yield break;
            }
            yield return Move.Resonance(sendClick: false);
            if (c.Succeeded) yield return Move.Wait(100);
            else if (c.Hud.Skills[1] == SkillState.Ready || c.Hud.Skills[1] == SkillState.Unavailable) yield return Move.Echo();
            else yield return Heavy(600, null, 0);
        }

        internal static IEnumerable<Move> Verina(TurnContext c)
        {
            long started = c.Now;
            yield return Move.Attacks(600);
            if (c.Full || c.Since(started) >= 6500) yield break;
            if (c.RReady) yield return Move.Liberation();
            if (c.Full || c.Since(started) >= 6500) yield break;
            if (c.EReady) yield return Move.Resonance();
            if (c.Full || c.Since(started) >= 6500) yield break;
            if (c.QReady) yield return Move.Echo(0);
            if (c.Full || c.Since(started) >= 6500) yield break;
            yield return Move.Wait(800);
            if (c.HeavyReady && c.Since(c.Memory.LastHeavy) >= 8000)
            {
                yield return Heavy(700, null, 0);
                c.Memory.LastHeavy = c.Now;
            }
            yield return Move.Tap(Jump, 10);
            yield return Move.Attacks(500);
        }

        internal static IEnumerable<Move> Sanhua(TurnContext c)
        {
            yield return Move.Wait(20);
            long holdStarted = c.Now;
            yield return Move.Press(Attack);
            foreach (Move action in WaitDown(c, false)) yield return action;
            yield return Move.Liberation();
            bool liberated = c.Succeeded;
            if (liberated) yield return Move.Wait(150);
            else if (c.EReady)
            {
                yield return Move.Release(Attack);
                yield return Move.Resonance(sendClick: false);
                holdStarted = c.Now;
                yield return Move.Press(Attack);
                yield return Move.Wait(100);
            }
            int remaining = (liberated ? 950 : 850) - (int)c.Since(holdStarted);
            if (remaining > 0) yield return Move.Wait(remaining);
            yield return Move.Release(Attack);
            yield return Move.Wait(800);
            if (liberated)
            {
                yield return Move.Resonance(sendClick: false);
                yield return Move.Wait(300);
            }
            if (c.Full) yield return Move.Echo();
        }

        internal static IEnumerable<Move> ShoreKeeper(TurnContext c)
        {
            if (c.Intro)
            {
                c.ContinueAfterLockLossUntil = c.Now + 4100;
                yield return Move.Wait(100);
                if (!c.TeamHiddenDuringMove) yield return Move.Attacks(1200);
                c.ContinueAfterLockLossUntil = 0;
            }
            yield return Move.Echo(0);
            yield return Move.Liberation();
            yield return Move.Resonance();
            if (!c.Succeeded && c.HeavyReady) yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
            if (c.Full)
            {
                c.Memory.ShoreKeeperOutro = c.Now;
                c.Memory.ShoreKeeperDodges = 5;
            }
        }

        private static IEnumerable<Move> ShoreKeeperDodge(TurnContext c)
        {
            FighterMemory keeper = c.Ally("ShoreKeeper");
            if (keeper == null || !c.Air || keeper.ShoreKeeperDodges <= 0 || c.Since(keeper.ShoreKeeperOutro) >= 30000)
                yield break;
            yield return Move.Repeat(Dodge, 1500, Grounded);
            if (c.Succeeded) keeper.ShoreKeeperDodges--;
        }

        internal static IEnumerable<Move> Qiuyuan(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(1170);
            if (c.Air) foreach (Move action in WaitDown(c)) yield return action;
            long start = c.Now;
            int window = c.SubDpsIntro ? 4000 : 1200;
            while (c.Now - start < window)
            {
                yield return Move.Echo(0);
                if (c.Now - start < 500)
                {
                    yield return Move.Liberation();
                    if (c.Succeeded) start = c.Now;
                }
                if (c.HeavyReady)
                {
                    yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                    if (c.Succeeded) yield break;
                }
                if (c.Air && !c.HeavyReady)
                    foreach (Move move in ShoreKeeperDodge(c)) yield return move;
                yield return Move.Tap(Attack, 100);
            }
            yield return Move.Resonance();
        }

        internal static IEnumerable<Move> Xiangliyao(TurnContext c)
        {
            foreach (Move action in WaitDown(c)) yield return action;
            yield return Move.Liberation();
            if (c.Since(c.Memory.LastLiberation) < 25000)
            {
                yield return Move.Resonance();
                while (!c.Succeeded)
                {
                    yield return Move.Attacks(1000);
                    yield return Move.Resonance();
                }
            }
            else if (EchoWithoutCooldown(c.Hud)) yield return Move.Echo();
            else yield return Move.Resonance();
        }

        internal static IEnumerable<Move> Jiyan(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(2000);
            yield return Move.Liberation();
            if (c.Succeeded)
            {
                long end = c.Now + 12000;
                while (c.Now < end)
                {
                    yield return Move.Resonance();
                    if (c.Succeeded) yield return Move.Tap(BattleButton.MiddleClick, 0);
                    yield return Move.Tap(Attack, 100);
                }
                yield break;
            }
            int attacks = 0;
            while (!c.Hud.ForteFullColor && !c.Full)
            {
                if (attacks % 4 == 0)
                {
                    yield return Heavy(600, null, 0);
                    if (ResonanceWithoutCooldown(c.Hud) || EchoWithoutCooldown(c.Hud))
                    {
                        yield return Move.Tap(BattleButton.MiddleClick, 0);
                        break;
                    }
                }
                yield return Move.Tap(Attack, 100);
                attacks++;
            }
            if (!c.Hud.ForteFullColor && ResonanceWithoutCooldown(c.Hud)) yield return Move.Resonance(postSleep: 1000);
            if (EchoWithoutCooldown(c.Hud)) yield return Move.Echo();
        }

        internal static IEnumerable<Move> Lingyang(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(800);
            foreach (Move action in WaitDown(c)) yield return action;
            yield return Move.Echo(0);
            if (c.RReady)
            {
                yield return Move.Liberation(animation: AnimationInput.InteractAndAttack);
                if (c.Succeeded) yield return Move.Attacks(1000);
            }
            long start = c.Now;
            while (c.Since(start) < 8000 && (!c.Full || c.Since(start) <= 2000))
            {
                if (c.HeavyReady)
                {
                    yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                    if (c.Succeeded) yield return Move.Attacks(400);
                    continue;
                }
                if (ResonanceWithoutCooldown(c.Hud))
                {
                    yield return Move.Resonance(1000);
                    if (c.Succeeded)
                    {
                        yield return Move.Attacks(350);
                        continue;
                    }
                }
                if (c.RReady) yield return Move.Liberation(0, AnimationInput.InteractAndAttack);
                else yield return Move.Tap(Attack, 100);
            }
        }

        internal static IEnumerable<Move> Lumi(TurnContext c)
        {
            if (c.Intro) yield return Move.Attacks(500);
            foreach (Move action in WaitDown(c)) yield return action;
            long start = c.Now;
            yield return Move.Echo(0);
            yield return Move.Liberation(0, AnimationInput.InteractAndAttack);
            while (c.Since(start) < 5000 && (!c.Full || c.Since(start) <= 1500))
            {
                if (ResonanceWithoutCooldown(c.Hud))
                {
                    yield return Move.Resonance(1000);
                    if (c.Succeeded)
                    {
                        yield return Move.Attacks(400);
                        continue;
                    }
                }
                if (c.HeavyReady) yield return Move.Hold(Attack, 2000, h => !HeavyHint(h), 50, 0);
                else if (c.RReady) yield return Move.Liberation(0, AnimationInput.InteractAndAttack);
                else yield return Move.Tap(Attack, 100);
            }
        }

        internal static IEnumerable<Move> JingRan(TurnContext c)
        {
            if (c.Intro) yield return WaitIntro();
            yield return Move.Echo(0);
            long start = c.Now, liberationStart = -1;
            while (c.Since(start) < 23000 && (liberationStart < 0 ? c.Since(start) < 8000 : c.Since(liberationStart) < 15000))
            {
                if (c.Hud.ForteFullColor)
                    yield return Move.Hold(Attack, 2000, h => !h.ForteFullColor, 50, 0);
                else
                {
                    if (liberationStart < 0)
                    {
                        yield return Move.Liberation(0);
                        if (c.Succeeded)
                        {
                            liberationStart = c.Now;
                            yield return Move.Wait(10);
                            continue;
                        }
                    }
                    yield return Move.Resonance(1000, sendClick: false);
                    if (c.Succeeded)
                        yield return Move.Attacks(800);
                    else yield return Move.Tap(Attack, 100);
                }
                yield return Move.Wait(10);
            }
        }

        internal static IEnumerable<Move> Galbrena(TurnContext c)
        {
            if (c.Intro)
            {
                long holdStarted = c.Now;
                yield return Move.Press(Attack);
                yield return Move.Wait(1000, h => c.Now - holdStarted >= 1000);
                bool handoff = c.NeedFastPerform;
                if (!handoff) yield return Move.Wait(440, h => c.Now - holdStarted >= 1440);
                yield return Move.Release(Attack);
                if (handoff) yield break;
                yield return Move.Repeat(Dodge, 600);
            }
            else if (c.Air) foreach (Move action in WaitDown(c)) yield return action;
            yield return Move.Echo(0);
            if (c.Hud.ForteFullColor && !c.NeedFastPerform)
            {
                yield return Move.Resonance();
                yield return Move.Liberation();
                if (c.Succeeded) yield return Move.Attacks(1000);
            }
            if (c.Long && !c.NeedFastPerform)
            {
                yield return Move.Liberation();
                long start = c.Now;
                while (c.Long && c.Now - start < 10000)
                {
                    if (c.Air) foreach (Move move in ShoreKeeperDodge(c)) yield return move;
                    yield return Move.Tap(Attack, 100);
                }
            }
            else
            {
                yield return Move.Attacks(1000);
                yield return Move.Resonance();
            }
        }
    }
}
