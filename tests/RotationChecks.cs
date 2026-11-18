using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TideCombat
{
    internal static class RotationChecks
    {
        private static HudSnapshot Snapshot()
        {
            var hud = new HudSnapshot { Valid = true, Locked = true, EnemyHealth = true, Current = 0, OpenWorld = true,
                LockOffset = 391, Concerto = .6, Forte = .4 };
            for (int index = 0; index < 3; index++)
            {
                hud.Health[index] = 1;
                hud.Skills[index] = SkillState.Ready;
            }
            hud.Keys[0] = 'E';
            hud.Keys[1] = 'Q';
            hud.Keys[2] = 'R';
            return hud;
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }

        private sealed class Simulation : IDisposable
        {
            private readonly CharacterTurn turn;
            private readonly HashSet<BattleButton> held = new HashSet<BattleButton>();
            private long clock = 1000;
            internal readonly List<Tuple<long, BattleAction>> Actions = new List<Tuple<long, BattleAction>>();
            internal readonly FighterMemory Actor;
            internal readonly HudSnapshot Hud;
            internal FighterMemory[] Team { get { return turn.Context.Team; } }

            internal Simulation(string id, bool intro = false, string outro = null, CombatSettings settings = null,
                FighterMemory actor = null, bool fastPerform = false, long start = 1000)
            {
                clock = start;
                Actor = actor ?? new FighterMemory { Id = id };
                Hud = Snapshot();
                var members = new[] { Actor, new FighterMemory { Id = "Verina" }, new FighterMemory { Id = "Sanhua" } };
                CombatSettings options = settings ?? CombatSettings.Defaults();
                CharacterKit previous = CharacterRoster.Find(outro);
                var context = new TurnContext(Actor, members, options, intro, outro, clock,
                    previous != null && previous.RoleFor(options) == BattleRole.Secondary, (hud, now) => fastPerform);
                turn = new CharacterTurn(CharacterRoster.Find(id), context);
            }

            internal void Run(Action<BattleAction, long, HudSnapshot> update = null, Action<long, HudSnapshot> frame = null,
                int liberationMs = 0)
            {
                long liberationEnds = -1;
                while (!turn.Finished && clock < 65000)
                {
                    if (frame != null) frame(clock, Hud);
                    if (liberationMs > 0) Hud.Valid = clock >= liberationEnds;
                    BattleAction action = Hud.Valid ? turn.Observe(Hud, clock) : turn.ObserveUnavailable(clock, true);
                    if (action.Kind != ActionKind.Wait)
                    {
                        Actions.Add(Tuple.Create(clock, action));
                        if (action.Kind == ActionKind.Hold)
                            Require(action.ReassertHold ? held.Contains(action.Button) : held.Add(action.Button), "invalid hold transition");
                        if (action.Kind == ActionKind.Release)
                            Require(held.Remove(action.Button), "release without hold");
                        if (action.DuringAnimation) turn.AnimationInputSent(action, clock);
                        else turn.Sent(action, clock);
                        if (liberationMs > 0 && action.Kind == ActionKind.Button && action.Button == BattleButton.Liberation)
                        {
                            Hud.Skills[2] = SkillState.Cooling;
                            liberationEnds = clock + liberationMs;
                        }
                        if (update != null) update(action, clock, Hud);
                    }
                    clock += 50;
                }
                Require(turn.Finished && held.Count == 0, Actor.Id + " never finished or left a button held");
            }

            public void Dispose() { turn.Dispose(); }
        }

        internal static void Run(Action<string, Action> check)
        {
            check("all 58 character kits and four Rover forms finish bounded turns", delegate
            {
                Require(CharacterRoster.All.Length == 62, "expected 58 entries and four explicit forms");
                var patterns = new HashSet<string>(PatternBank.Portraits.Select(x => x.Fighter));
                Require(patterns.Count == 58, "portrait index does not cover all registered entries");
                foreach (CharacterKit kit in CharacterRoster.All)
                foreach (bool intro in new[] { false, true })
                {
                    var settings = CombatSettings.Defaults();
                    settings.UseLiberation = intro;
                    using (var sim = new Simulation(kit.Id, intro, intro ? "Sanhua" : null, settings))
                    {
                        sim.Hud.RoverForm = "RoverHavoc";
                        sim.Run((action, time, hud) =>
                        {
                            int skill = TurnContext.SkillIndex(action.Button);
                            if (skill >= 0 && action.Kind == ActionKind.Button)
                                hud.Skills[skill] = SkillState.Cooling;
                        });
                        Require(intro || kit.Id == "Rebecca" || kit.Id == "Changli" || kit.Id == "Carlotta" ||
                            !sim.Actions.Any(x => x.Item2.Button == BattleButton.Liberation),
                            kit.Id + " sent liberation when disabled");
                    }
                }
            });
            check("all character states handle full resources, unavailable skills, and enabled variants", RosterStateMatrix);
            check("Verina exits early when the opening attacks fill concerto", delegate
            {
                using (var sim = new Simulation("Verina"))
                {
                    sim.Run((action, time, hud) => { if (action.Button == BattleButton.Attack) hud.ConcertoFull = true; });
                    Require(sim.Actions.Any() && sim.Actions.All(x => x.Item2.Button == BattleButton.Attack),
                        "full concerto should skip the rest of the skill chain");
                }
            });
            check("Sanhua holds through liberation, then releases before resonance", delegate
            {
                using (var sim = new Simulation("Sanhua"))
                {
                    sim.Run((action, time, hud) =>
                    {
                        if (action.Button == BattleButton.Resonance) hud.Skills[0] = SkillState.Cooling;
                    }, liberationMs: 400);
                    string actual = string.Join(",", sim.Actions.Where(x => !x.Item2.DuringAnimation)
                        .Select(x => x.Item2.Kind + "/" + x.Item2.Button));
                    Require(actual == "Hold/Attack,Button/Liberation,Release/Attack,Button/Resonance", actual);
                }
            });
            check("Hsin uses the enhanced skill and completes the second liberation", HsinChain);
            check("Hsin does not enter the finisher on cooldown evidence alone", HsinRequiresAnimation);
            check("Hsin releases H2 after 250 ms of hidden HUD and waits for generic R2", HsinHiddenHeavy);
            check("Hiyuki reasserts the held finisher through short cooldown and releases on animation", HiyukiHeldFinisher);
            check("Suoming support passes the free intro while DPS holds the enhanced skill", SuomingBranches);
            check("Suoming releases between E attempts and keeps the final hold through the ready wait", SuomingHoldSequence);
            check("Buling has separate turns and skips a heavy with one hexagram", DoulingPhases);
            check("Electro Rover requires full forte as well as the enhanced prompt", ElectroRover);
            check("Lily hands off Slumberland and releases a timed-out heavy", LilyStates);
            check("Lily finishes concerto if liberation is unavailable or the attempted cast fails", LilyConcertoTopUp);
            check("Roccia releases the movement key after aerial and ground branches", RocciaRelease);
            check("Camellya restarts stalled heavy attack and ends on actual charge depletion", CamellyaCharge);
            check("Camellya starts liberation while holding in Budding and then reholds", CamellyaBudding);
            check("Qiuyuan extends field time only on sub-DPS intro", QiuyuanIntroRole);
            check("ShoreKeeper grants five airborne dodge responses and then expires", ShoreKeeperDodgeWindow);
            check("Xigelika selects mouse or resonance hold from the action bar", XigelikaHeavy);
            check("Linnai accepts Aemeath's longer acceleration window and waits between kicks", LinnaiAccelerate);
            check("Qingxiao confirms the second heavy, executes and waits for liberation", QingxiaoHeavyFinish);
            check("Luhesi clears an interrupted elbow strike and counts only three confirmed kicks", LuhesiKicks);
            check("Lucy holds the two enhanced heavies before sustained charge and eleven post-R attacks", LucyFinisher);
            check("Mornye keeps the 23-second grounded cooldown after a confirmed aerial heavy", MornyeCooldown);
            check("Galbrena releases the opening heavy early when a teammate has mandatory priority", GalbrenaPriority);
            check("execution alternates interaction and normal attack and then ends", BreakSequence);
            check("Carlotta stores her heavy attack for the Zhezhi outro window", CarlottaHandoff);
            check("Zhezhi keeps the attack held through three enhanced resonance casts for Carlotta", ZhezhiBlueChain);
            check("Ciaccona chooses wind or spectro recital by teammate slot order", CiacconaRecital);
            check("Cartethyia transforms and casts the Fleurdelys finisher before returning to resonance", CartethyiaForms);
            check("Changli starts a held attack at 1.5 seconds into liberation and releases after the gauge clears", ChangliLiberationHold);
            check("Phoebe keeps the first support turn intact and casts before leaving a full-Blaze Zani team", PhoebeSupportLink);
            check("Jinhsi preserves separate intro-cooldown and Incarnation branches", JinhsiIncarnation);
            check("Augusta releases Majesty on animation, attacks during it and only then sends echo", AugustaMajesty);
            check("Zani distinguishes an almost full Blaze bar from the direct liberation threshold", ZaniThreshold);
            check("Zani cancels the last Nightfall smash and ends the liberated stance with raw R", ZaniNightfallAndFinisher);
            check("Lucilla charges through short cooldown and stays out when the energy icon is already lit", LucillaCharge);
            check("Lucilla uses 600 ms transformation pulses and stops when concerto falls to zero", LucillaTransform);
            check("Rebecca runs raw R entry and periodic turret input independently of the common animation helper", RebeccaTurret);
            check("confirmed slots carry concerto and trigger the registered program", PlannerIntegration);
        }

        private static void RosterStateMatrix()
        {
            foreach (CharacterKit kit in CharacterRoster.All)
            foreach (bool enhanced in new[] { false, true })
            foreach (bool alternate in new[] { false, true })
            {
                var settings = CombatSettings.Defaults();
                settings.IunoC6 = settings.RocciaC6 = settings.ChisaDps = settings.SuomingDps = alternate;
                using (var sim = new Simulation(kit.Id, true, "Sanhua", settings))
                {
                    sim.Hud.RoverForm = alternate ? "RoverAero" : "RoverElectro";
                    sim.Hud.Concerto = sim.Hud.Forte = sim.Hud.Blazes = sim.Hud.CharacterCharge = enhanced ? 1 : 0;
                    sim.Hud.ConcertoFull = enhanced;
                    sim.Hud.EnhancedColor = enhanced;
                    sim.Hud.Airborne = alternate;
                    sim.Hud.ForteSegments = enhanced ? 3 : 0;
                    if (enhanced) sim.Hud.Indicators.Add("mouse_forte");
                    for (int i = 0; i < sim.Hud.Skills.Length; i++)
                        sim.Hud.Skills[i] = alternate ? SkillState.Unavailable : SkillState.Ready;
                    sim.Run((action, time, hud) =>
                    {
                        int skill = TurnContext.SkillIndex(action.Button);
                        if (skill >= 0 && action.Kind != ActionKind.Release && !alternate)
                            hud.Skills[skill] = SkillState.Cooling;
                        if (kit.Id == "Camellya" && action.Button == BattleButton.Resonance) hud.EnhancedColor = false;
                        if (action.Button == BattleButton.Dodge || action.Button == BattleButton.Attack) hud.Airborne = false;
                    });
                }
            }
        }

        private static void CamellyaCharge()
        {
            using (var sim = new Simulation("Camellya", true, "Sanhua"))
            {
                sim.Hud.CharacterCharge = .8;
                long secondHold = 0;
                int holds = 0;
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold && ++holds == 2) secondHold = time;
                }, (time, hud) =>
                {
                    if (secondHold > 0) hud.CharacterCharge = Math.Max(0, .8 - (time - secondHold) / 1000.0);
                });
                var changes = sim.Actions.Where(x => x.Item2.Kind == ActionKind.Hold || x.Item2.Kind == ActionKind.Release).ToArray();
                Require(changes.Length == 4 && changes[1].Item1 - changes[0].Item1 == 750,
                    "stationary bar hold transitions: " + string.Join(",", changes.Select(x => x.Item1 + "/" + x.Item2.Kind)));
                Require(changes[2].Item1 - changes[1].Item1 >= 100 && changes[3].Item1 - secondHold < 1150,
                    "the second hold should stop once the meter is depleted");
            }
            using (var sim = new Simulation("Camellya", true, "Sanhua"))
            {
                sim.Hud.CharacterCharge = .8;
                sim.Run();
                var changes = sim.Actions.Where(x => x.Item2.Kind == ActionKind.Hold || x.Item2.Kind == ActionKind.Release).ToArray();
                Require(changes.Length > 4 && changes.Last().Item1 - changes[0].Item1 <= 4800,
                    "retries must not extend the original hold deadline indefinitely");
            }
        }

        private static void BreakSequence()
        {
            using (var sim = new Simulation("Brant"))
            {
                sim.Hud.BreakPrompt = true;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Interact) hud.BreakPrompt = false;
                });
                BattleButton[] first = sim.Actions.Take(6).Select(x => x.Item2.Button).ToArray();
                Require(first.SequenceEqual(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ?
                    BattleButton.Interact : BattleButton.Attack)), "execution inputs did not alternate");
                Require(sim.Actions.Count(x => x.Item2.Button == BattleButton.Interact) == 3,
                    "execution should stop when the prompt disappears");
            }
        }

        private static void CamellyaBudding()
        {
            using (var sim = new Simulation("Camellya"))
            {
                sim.Hud.Concerto = .95;
                sim.Hud.ConcertoFull = true;
                sim.Hud.EnhancedColor = true;
                sim.Hud.Skills[0] = SkillState.Cooling;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Resonance && hud.EnhancedColor)
                    {
                        hud.EnhancedColor = false;
                        hud.Indicators.Add("camellya_budding");
                    }
                }, (time, hud) =>
                {
                    hud.BuddingCharge = Math.Max(0, .8 - (time - 1000) / 5800.0);
                    if (time >= 6000) hud.Indicators.Remove("camellya_budding");
                }, liberationMs: 400);
                var first = sim.Actions.Where(x => !x.Item2.DuringAnimation).Take(5).ToArray();
                Require(first.Length == 5 && first[0].Item2.Button == BattleButton.Resonance && first[1].Item2.Kind == ActionKind.Hold &&
                    first[2].Item2.Button == BattleButton.Liberation && first[3].Item2.Kind == ActionKind.Release &&
                    first[4].Item2.Kind == ActionKind.Hold && first[1].Item1 - first[0].Item1 >= 1100 &&
                    first[4].Item1 - first[3].Item1 == 200,
                    "Budding trace: " + string.Join(",", first.Select(x => x.Item1 + "/" + x.Item2.Kind + "/" + x.Item2.Button)));
            }
        }

        private static void QiuyuanIntroRole()
        {
            long mainIntro = QiuyuanLastAttack("Calcharo");
            long subDpsIntro = QiuyuanLastAttack("Danjin");
            Require(subDpsIntro - mainIntro >= 2500, "Danjin's sub-DPS intro must extend the combat phase");
        }

        private static void ShoreKeeperDodgeWindow()
        {
            FighterMemory keeper;
            using (var sim = new Simulation("ShoreKeeper"))
            {
                sim.Hud.ConcertoFull = true;
                for (int i = 0; i < sim.Hud.Skills.Length; i++) sim.Hud.Skills[i] = SkillState.Cooling;
                sim.Run();
                keeper = sim.Actor;
            }
            Require(keeper.ShoreKeeperDodges == 5 && keeper.ShoreKeeperOutro >= 1000, "full concerto grants the outro effect");
            keeper.ResetForNextBattle();
            Require(XigelikaDodgeCount(keeper, keeper.ShoreKeeperOutro + 30000) == 0 && keeper.ShoreKeeperDodges == 5,
                "an expired outro must not send dodge or spend a charge");
            for (int trigger = 0; trigger < 5; trigger++)
                Require(XigelikaDodgeCount(keeper, keeper.ShoreKeeperOutro + 1000 * (trigger + 1)) == 1,
                    "one dodge should end as soon as the ground pose appears");
            Require(XigelikaDodgeCount(keeper, keeper.ShoreKeeperOutro + 6000) == 0 && keeper.ShoreKeeperDodges == 0,
                "the sixth response must find the five-charge budget spent");
        }

        private static int XigelikaDodgeCount(FighterMemory keeper, long start)
        {
            using (var sim = new Simulation("Xigelika", true, "Sanhua", start: start))
            {
                sim.Team[1] = keeper;
                sim.Hud.Airborne = true;
                for (int i = 0; i < sim.Hud.Skills.Length; i++) sim.Hud.Skills[i] = SkillState.Cooling;
                sim.Run((action, time, hud) => { if (action.Button == BattleButton.Dodge) hud.Airborne = false; });
                return sim.Actions.Count(action => action.Item2.Button == BattleButton.Dodge);
            }
        }

        private static void XigelikaHeavy()
        {
            foreach (bool useMouse in new[] { false, true })
            using (var sim = new Simulation("Xigelika"))
            {
                sim.Hud.LongActionLayout = useMouse ? 1 : 0;
                sim.Hud.XigelikaForte = true;
                BattleButton charge = useMouse ? BattleButton.Attack : BattleButton.Resonance;
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold && action.Button == charge)
                    {
                        hud.XigelikaForte = false;
                        hud.LongActionLayout = 0;
                    }
                });
                var holds = sim.Actions.Where(action => action.Item2.Kind == ActionKind.Hold || action.Item2.Kind == ActionKind.Release).ToArray();
                Require(holds.Length == 2 && holds.All(action => action.Item2.Button == charge), "use the correct input and release on the fresh frame");
            }
        }

        private static void LinnaiAccelerate()
        {
            foreach (string from in new[] { "Aemeath", "Sanhua" })
            using (var sim = new Simulation("Linnai", true, from))
            {
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Hud.Indicators.Add("mouse_forte");
                long release = 0, kick = 0;
                int kicks = 0;
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold) hud.Indicators.Remove("mouse_forte");
                    if (action.Kind == ActionKind.Release) release = time;
                    if (action.Button == BattleButton.Resonance)
                    {
                        kick = time;
                        hud.Skills[0] = SkillState.Cooling;
                        if (++kicks == 2) hud.ConcertoFull = true;
                    }
                }, (time, hud) =>
                {
                    if (release > 0 && time >= release + 1700)
                    {
                        hud.LinnaiAccelerating = hud.LinnaiColorFull = true;
                        if (kicks == 0 || kicks == 1 && time - kick >= 400) hud.Skills[0] = SkillState.Ready;
                    }
                });
                var casts = sim.Actions.Where(action => action.Item2.Button == BattleButton.Resonance).ToArray();
                Require(casts.Length == (from == "Aemeath" ? 2 : 0), from + " resonance count=" + casts.Length);
                if (casts.Length > 0) Require(casts[1].Item1 - casts[0].Item1 >= 400, "kick animation needs its recovery delay");
            }
        }

        private static void QingxiaoHeavyFinish()
        {
            using (var sim = new Simulation("Qingxiao", true, "Sanhua"))
            {
                foreach (FighterMemory teammate in sim.Team.Skip(1))
                {
                    teammate.BuffStarted = 0;
                    teammate.BuffDuration = 20000;
                }
                sim.Hud.BreakPrompt = true;
                sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Interact) hud.BreakPrompt = false;
                }, (time, hud) =>
                {
                    if (time < 1100 || time >= 1200 && time < 1300) hud.Indicators.Add("qingxiao_h2");
                    else hud.Indicators.Remove("qingxiao_h2");
                    if (time == 2400) hud.Skills[2] = SkillState.Ready;
                }, liberationMs: 400);
                var release = sim.Actions.Single(action => action.Item2.Kind == ActionKind.Release);
                var execution = sim.Actions.First(action => action.Item2.Button == BattleButton.Interact);
                var cast = sim.Actions.Single(action => action.Item2.Button == BattleButton.Liberation);
                Require(release.Item1 >= 1550 && execution.Item1 > release.Item1 && cast.Item1 >= 2400,
                    "require a stable dark icon, execution, and a fresh ready liberation frame");
            }
            using (var sim = new Simulation("Qingxiao", true, "Sanhua"))
            {
                sim.Hud.Indicators.Add("qingxiao_h2");
                sim.Hud.Indicators.Add("qingxiao_e");
                sim.Run((action, time, hud) => { if (action.Button == BattleButton.Resonance) hud.Skills[0] = SkillState.Cooling; });
                Require(sim.Actions.Count == 1 && sim.Actions[0].Item2.Button == BattleButton.Resonance,
                    "without both buffs the turn only casts enhanced resonance");
            }
        }

        private static void LuhesiKicks()
        {
            using (var sim = new Simulation("Luhesi", true, "Sanhua"))
            {
                int casts = 0, kicks = 0;
                sim.Hud.Airborne = sim.Hud.SkillLit[1] = true;
                sim.Hud.LongActionLayout = 2;
                sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Jump) hud.LongActionLayout = 0;
                    if (action.Button == BattleButton.Resonance)
                    {
                        if (++casts == 1) hud.SkillLit[1] = false;
                        else hud.Indicators.Add("luhesi_kick");
                    }
                    if (action.Button == BattleButton.Dodge) hud.SkillLit[1] = true;
                    if (action.Button == BattleButton.Attack && hud.Indicators.Remove("luhesi_kick"))
                    {
                        hud.LongActionLayout = 2;
                        if (++kicks == 3)
                        {
                            hud.LuhesiLiberation = true;
                            hud.Skills[2] = SkillState.Ready;
                        }
                    }
                }, liberationMs: 400);
                Require(casts == 4 && kicks == 3 && sim.Actions.Count(x => x.Item2.Button == BattleButton.Dodge) == 1 &&
                    sim.Actions.Count(x => x.Item2.Button == BattleButton.Liberation) == 1,
                    "elbow interruption should consume neither a kick nor the jump-to-resonance request");
            }
            using (var sim = new Simulation("Luhesi", true, "Carlotta"))
            {
                sim.Hud.LuhesiLiberation = true;
                sim.Run();
                Require(sim.Actions.Count(x => x.Item2.Button == BattleButton.Resonance) == 1 &&
                    sim.Actions.All(x => x.Item2.Button == BattleButton.Attack || x.Item2.Button == BattleButton.Resonance),
                    "an intro from main DPS sends the short raw E branch only");
            }
        }

        private static void LucyFinisher()
        {
            using (var sim = new Simulation("Lucy"))
            {
                sim.Hud.ForteFullColor = true;
                sim.Hud.Skills[0] = SkillState.Cooling;
                sim.Hud.Indicators.Add("mouse_forte");
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                }, liberationMs: 400);
                var holds = sim.Actions.Where(x => x.Item2.Kind == ActionKind.Hold).ToArray();
                var releases = sim.Actions.Where(x => x.Item2.Kind == ActionKind.Release).ToArray();
                var cast = sim.Actions.Single(x => x.Item2.Button == BattleButton.Liberation);
                var echo = sim.Actions.Single(x => x.Item2.Button == BattleButton.Echo);
                Require(holds.Length == 3 && releases.Length == 3 && releases[0].Item1 - holds[0].Item1 == 400 &&
                    releases[1].Item1 - holds[1].Item1 == 400 && releases[2].Item1 - holds[2].Item1 == 2500 &&
                    releases[2].Item1 < echo.Item1 && echo.Item1 < cast.Item1,
                    "charged input order and echo before liberation");
                Require(sim.Actions.Count(x => !x.Item2.DuringAnimation && x.Item1 > cast.Item1 && x.Item2.Button == BattleButton.Attack) == 11,
                    "eleven follow-up attacks after liberation");
            }
        }

        private static void MornyeCooldown()
        {
            FighterMemory memory;
            using (var sim = new Simulation("Mornye"))
            {
                sim.Hud.LongActionLayout = 2;
                sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Hud.ConcertoFull = true;
                sim.Hud.Indicators.Add("mouse_forte");
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold) hud.Indicators.Remove("mouse_forte");
                });
                memory = sim.Actor;
                Require(memory.LastHeavy > 1000 && memory.ExecuteWhenSwitching == false, "aerial heavy stores the completed combo state");
            }
            memory.ResetForNextBattle();
            using (var sim = new Simulation("Mornye", actor: memory, start: memory.LastHeavy + 1000))
            {
                sim.Hud.InRealm = true;
                sim.Run((action, time, hud) => { if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling; });
                Require(sim.Actions.Count == 1 && sim.Actions[0].Item2.Button == BattleButton.Echo && memory.ExecuteWhenSwitching == true,
                    "grounded cooldown carries into the next battle and uses the quick echo branch");
            }
        }

        private static long QiuyuanLastAttack(string outro)
        {
            using (var sim = new Simulation("Qiuyuan", true, outro))
            {
                for (int index = 0; index < sim.Hud.Skills.Length; index++) sim.Hud.Skills[index] = SkillState.Cooling;
                sim.Run();
                return sim.Actions.Last(x => x.Item2.Button == BattleButton.Attack).Item1;
            }
        }

        private static void GalbrenaPriority()
        {
            using (var sim = new Simulation("Galbrena", true, "Danjin", fastPerform: true))
            {
                sim.Run();
                Require(sim.Actions.Count == 2 && sim.Actions[0].Item2.Kind == ActionKind.Hold &&
                    sim.Actions[1].Item2.Kind == ActionKind.Release && sim.Actions[1].Item1 - sim.Actions[0].Item1 == 1000,
                    "third action or an extended heavy would delay the priority teammate");
            }
        }

        private static void CarlottaHandoff()
        {
            var memory = new FighterMemory { Id = "Carlotta" };
            using (var preparation = new Simulation("Carlotta", actor: memory))
            {
                preparation.Team[1].Id = "Zhezhi";
                preparation.Hud.Indicators.Add("mouse_forte");
                preparation.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold) hud.Indicators.Remove("mouse_forte");
                });
                Require(memory.LiberationPrimed && preparation.Actions.All(x => x.Item2.Button != BattleButton.Liberation),
                    "prepare the liberation, then yield the field to Zhezhi");
            }
            using (var outro = new Simulation("Carlotta", true, "Zhezhi", actor: memory))
            {
                outro.Team[1].Id = "Zhezhi";
                outro.Hud.ResonanceWhiteRatio = .2;
                outro.Run((action, time, hud) =>
                {
                    int skill = TurnContext.SkillIndex(action.Button);
                    if (skill >= 0) hud.Skills[skill] = SkillState.Cooling;
                }, liberationMs: 400);
                Require(outro.Actions.Any(x => x.Item2.Button == BattleButton.Liberation) && !memory.LiberationPrimed,
                    "Zhezhi's outro should consume the primed liberation");
            }
        }

        private static void ZhezhiBlueChain()
        {
            using (var sim = new Simulation("Zhezhi"))
            {
                sim.Team[1].Id = "Carlotta";
                sim.Team[1].LiberationPrimed = true;
                sim.Hud.EnhancedColor = true;
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = sim.Hud.Skills[2] = SkillState.Cooling;
                int casts = 0;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Resonance && ++casts == 3) hud.EnhancedColor = false;
                });
                var trace = sim.Actions.Select(x => x.Item2).ToArray();
                Require(trace.Length == 5 && trace[0].Kind == ActionKind.Hold && trace[4].Kind == ActionKind.Release &&
                    trace[0].Button == BattleButton.Attack && trace[4].Button == BattleButton.Attack &&
                    trace.Skip(1).Take(3).All(x => x.Button == BattleButton.Resonance),
                    "blue resonance bypasses the displayed cooldown, then releases the mouse immediately");
            }
        }

        private static void CiacconaRecital()
        {
            foreach (bool spectroFirst in new[] { false, true })
            using (var sim = new Simulation("Ciaccona", intro: true))
            {
                sim.Team[1].Id = spectroFirst ? "Phoebe" : "Cartethyia";
                sim.Team[2].Id = spectroFirst ? "Cartethyia" : "Phoebe";
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                sim.Run(liberationMs: 400);
                Require(sim.Actor.InRecital && sim.Actor.LastLiberation > 0, "recital is set only upon confirmed liberation");
                int left = sim.Actions.Count(x => x.Item2.Button == BattleButton.Left);
                Require(left == (spectroFirst ? 6 : 0), "spectro recital repeats A and wind recital does not");
            }
        }

        private static void CartethyiaForms()
        {
            using (var sim = new Simulation("Cartethyia"))
            {
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                sim.Hud.Indicators.UnionWith(new[] { "cartethyia_small", "forte_cartethyia_sword1",
                    "forte_cartethyia_sword2", "forte_cartethyia_sword3" });
                int stage = 0;
                long transformed = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation && ++stage == 1)
                    {
                        transformed = time;
                        hud.Indicators.Remove("cartethyia_small");
                    }
                    else if (action.Button == BattleButton.Liberation && stage == 2)
                    {
                        hud.Indicators.Remove("lib_cartethyia_big");
                        hud.Indicators.Add("cartethyia_small");
                        hud.Skills[0] = SkillState.Ready;
                    }
                    if (action.Button == BattleButton.Resonance) hud.Skills[0] = SkillState.Cooling;
                }, (time, hud) =>
                {
                    if (stage == 1 && time >= transformed + 600)
                    {
                        hud.Indicators.Add("lib_cartethyia_big");
                        hud.Skills[2] = SkillState.Ready;
                    }
                }, liberationMs: 400);
                Require(stage == 2 && !sim.Actor.CartethyiaTransformed && sim.Actions.Last().Item2.Button == BattleButton.Resonance,
                    "the large-form finisher must be followed by small-form resonance");
            }
        }

        private static void ChangliLiberationHold()
        {
            var settings = CombatSettings.Defaults();
            settings.UseLiberation = false;
            using (var sim = new Simulation("Changli", settings: settings))
            {
                long pressed = -1;
                sim.Hud.Skills[0] = SkillState.Cooling;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button != BattleButton.Liberation) return;
                    pressed = time;
                    hud.Skills[2] = SkillState.Cooling;
                }, (time, hud) =>
                {
                    if (pressed < 0) return;
                    hud.Valid = time >= pressed + 2400;
                    if (time >= pressed + 2550 && time < pressed + 2800) hud.Indicators.Add("mouse_forte");
                    if (time >= pressed + 2800) hud.Indicators.Remove("mouse_forte");
                });
                var inputs = sim.Actions.ToArray();
                Require(inputs.Length == 3 && inputs[0].Item2.Button == BattleButton.Liberation &&
                    inputs[1].Item2.Kind == ActionKind.Hold && inputs[1].Item1 - pressed == 1550 &&
                    inputs[2].Item2.Kind == ActionKind.Release && inputs[2].Item1 - pressed >= 2800,
                    "run R, delayed mouse-down, then mouse-up with no interaction pulse");
            }
        }

        private static void PhoebeSupportLink()
        {
            using (var first = new Simulation("Phoebe"))
            {
                first.Team[1].Id = "Cartethyia";
                first.Team[2].Id = "RoverAero";
                first.Hud.ChargeMode = 2;
                first.Hud.Skills[2] = SkillState.Cooling;
                first.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Attack)
                    {
                        hud.Concerto = Math.Min(1, hud.Concerto + .15);
                        hud.ConcertoFull = hud.Concerto == 1;
                    }
                    if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                });
                Require(first.Actor.Phoebe.Mode == 2 && first.Actor.Phoebe.FirstRotationDone && first.Hud.ConcertoFull,
                    "the first Cartethyia/Rover support turn fills concerto");
                Require(first.Actions.Last().Item2.Button == BattleButton.Echo &&
                    first.Actions.All(x => x.Item2.Button != BattleButton.Resonance), "skip short E and use echo at the outro");
            }
            using (var link = new Simulation("Phoebe"))
            {
                link.Team[1].Id = "Zani";
                link.Team[1].Resource = .95;
                link.Hud.ChargeMode = 2;
                link.Run(liberationMs: 400);
                Require(link.Actor.Phoebe.PriorityLiberation && !link.Actor.Phoebe.FirstRotationDone &&
                    link.Actions.Count(x => x.Item2.Button == BattleButton.Liberation) == 1 &&
                    link.Actions.All(x => x.Item2.Button != BattleButton.Resonance), "stop filling Blaze and cast one ready R before leaving");
            }
        }

        private static void JinhsiIncarnation()
        {
            using (var early = new Simulation("Jinhsi", intro: true))
            {
                early.Hud.Skills[0] = early.Hud.Skills[1] = SkillState.Cooling;
                early.Run();
                Require(early.Actor.Phase == 1 && early.Actions.Count == 1 && early.Actions[0].Item2.Button == BattleButton.Attack,
                    "an early cooldown defers Incarnation and uses the echo fallback");
            }
            var memory = new FighterMemory { Id = "Jinhsi", Phase = 2 };
            using (var incarnation = new Simulation("Jinhsi", actor: memory))
            {
                incarnation.Hud.Skills[0] = incarnation.Hud.Skills[1] = SkillState.Cooling;
                long resonated = -1;
                incarnation.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Resonance) resonated = time;
                }, (time, hud) => hud.Valid = resonated < 0 || time >= resonated + 450);
                var trace = incarnation.Actions.Select(x => x.Item2.Button).ToArray();
                Require(trace.SequenceEqual(new[] { BattleButton.Attack, BattleButton.Resonance, BattleButton.Attack, BattleButton.Attack }) &&
                    memory.Phase == 0 && !memory.FreeIntro, "alternate raw attacks and E, then resume once on return and finish without a fabricated intro");
            }
        }

        private static void AugustaMajesty()
        {
            foreach (bool showAnimation in new[] { true, false })
            {
                var settings = CombatSettings.Defaults();
                settings.UseLiberation = false;
                using (var sim = new Simulation("Augusta", settings: settings))
                {
                    sim.Hud.Indicators.Add("Augusta_lib2");
                    sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                    long pressed = -1;
                    sim.Run((action, time, hud) =>
                    {
                        if (action.Kind == ActionKind.Hold) pressed = time;
                        if (action.Kind == ActionKind.Release) hud.Indicators.Remove("Augusta_lib2");
                    }, (time, hud) => hud.Valid = !showAnimation || pressed < 0 || time < pressed + 150 || time >= pressed + 8250);
                    var inputs = sim.Actions.ToArray();
                    Require(inputs[0].Item2.Kind == ActionKind.Hold && inputs[0].Item2.Button == BattleButton.Liberation,
                        "Majesty starts with the raw R hold");
                    Require(inputs[1].Item2.Kind == ActionKind.Release && inputs[1].Item1 - pressed == (showAnimation ? 150 : 600),
                        "release R when the HUD disappears, or at the 600 ms limit");
                    if (showAnimation)
                    {
                        Require(inputs.Skip(2).Take(inputs.Length - 3).All(x => x.Item2.DuringAnimation && x.Item2.Button == BattleButton.Attack),
                            "the animation uses normal attack and never F");
                        Require(inputs.Any(x => x.Item1 >= pressed + 7500 && x.Item2.DuringAnimation),
                            "Majesty's attack window extends past ordinary liberation's 7 seconds");
                        Require(inputs.Last().Item2.Button == BattleButton.Echo && inputs.Last().Item1 == pressed + 8250,
                            "send echo immediately after the HUD returns");
                    }
                    else Require(inputs[2].Item2.Button == BattleButton.Attack && !inputs[2].Item2.DuringAnimation,
                        "failed entry must resume the regular rotation");
                }
            }
        }

        private static void ZaniThreshold()
        {
            foreach (double blazes in new[] { .96, 1.0 })
            {
                using (var sim = new Simulation("Zani"))
                {
                    sim.Hud.Blazes = blazes;
                    sim.Hud.Skills[0] = SkillState.Cooling;
                    sim.Run((action, time, hud) =>
                    {
                        if (action.Button == BattleButton.Liberation) hud.Skills[2] = SkillState.Cooling;
                    });
                    bool cast = sim.Actions.Any(x => x.Item2.Button == BattleButton.Liberation);
                    Require(cast == (blazes == 1), "direct liberation threshold was applied incorrectly");
                }
            }
        }

        private static void ZaniNightfallAndFinisher()
        {
            using (var stance = new Simulation("Zani"))
            {
                stance.Hud.Blazes = 1;
                stance.Hud.ZaniNightfallCharge = .22;
                long firstDodge = -1;
                stance.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                    if (action.Button == BattleButton.Dodge && firstDodge < 0) firstDodge = time;
                }, (time, hud) =>
                {
                    if (firstDodge >= 0 && time >= firstDodge + 900) hud.ZaniNightfallCharge = .025;
                }, liberationMs: 400);
                Require(stance.Actor.ZaniLiberated && stance.Actor.ZaniLiberationAt >= 0, "first R confirms the liberated stance");
                var dodges = stance.Actions.Where(x => x.Item2.Button == BattleButton.Dodge).ToArray();
                Require(dodges.Length == 2 && dodges[1].Item1 - firstDodge >= 1150,
                    "wait for the dim Nightfall ring and a quarter second before cancelling");
            }
            var options = CombatSettings.Defaults();
            options.UseLiberation = false;
            using (var finish = new Simulation("Zani", settings: options))
            {
                finish.Hud.ZaniForm = 2;
                finish.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation) hud.ZaniForm = 1;
                });
                Require(finish.Actions.Count == 1 && finish.Actions[0].Item2.Button == BattleButton.Liberation &&
                    !finish.Actor.ZaniLiberated && finish.Actor.Resource == -1,
                    "leave the existing stance through its dedicated second R even with new liberations disabled");
            }
        }

        private static void LucillaCharge()
        {
            foreach (double seconds in new[] { 1.2, 1.5, 1.6 })
            foreach (bool lit in new[] { false, true })
            {
                using (var sim = new Simulation("Lucilla"))
                {
                    sim.Hud.Skills[2] = SkillState.Cooling;
                    sim.Hud.SkillLit[2] = lit;
                    sim.Hud.Cooldowns[2] = seconds;
                    sim.Run((action, time, hud) =>
                    {
                        if (action.Kind == ActionKind.Release) hud.Cooldowns[2] = 2;
                    });
                    int holds = sim.Actions.Count(x => x.Item2.Kind == ActionKind.Hold);
                    Require(holds == (!lit && seconds <= 1.5 ? 1 : 0), "cooldown cutoff or lit energy gate mismatch");
                }
            }
        }

        private static void LucillaTransform()
        {
            using (var sim = new Simulation("Lucilla"))
            {
                int pulses = 0;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation) hud.Skills[2] = SkillState.Unavailable;
                    if (action.Kind == ActionKind.Release && action.Button == BattleButton.Attack && ++pulses == 2)
                        hud.Concerto = 0;
                });
                var raw = sim.Actions.Where(x => x.Item2.Kind == ActionKind.Hold || x.Item2.Kind == ActionKind.Release).ToArray();
                Require(raw.Length == 4 && raw[1].Item1 - raw[0].Item1 == 600 && raw[3].Item1 - raw[2].Item1 == 600,
                    "stop after exactly two complete pulses when the concerto ring confirms the end");
                var firstR = sim.Actions.First(x => x.Item2.Button == BattleButton.Liberation);
                Require(raw[0].Item1 - firstR.Item1 >= 3000, "the transformation must settle before heavy pulses start");
            }
        }

        private static void RebeccaTurret()
        {
            var settings = CombatSettings.Defaults();
            settings.UseLiberation = false;
            using (var sim = new Simulation("Rebecca", settings: settings))
            {
                sim.Hud.ConcertoFull = true;
                long lastResonance = -1000;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Resonance)
                    {
                        lastResonance = time;
                        hud.Skills[0] = SkillState.Cooling;
                    }
                    if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                }, (time, hud) =>
                {
                    if (time - lastResonance >= 250) hud.Skills[0] = SkillState.Ready;
                });
                var uses = sim.Actions.Where(x => x.Item2.Button == BattleButton.Liberation).Select(x => x.Item1).ToArray();
                Require(uses.Length >= 12, "the raw 800 ms entry and 5.2 s turret phase both send R even with no helper");
                Require(uses.Take(8).Zip(uses.Skip(1), (a, b) => b - a).Take(7).All(gap => gap == 100), "entry cadence mismatch");
                Require(uses.Skip(8).Zip(uses.Skip(9), (a, b) => b - a).All(gap => gap > 900), "turret refresh must wait more than 900 ms");
                Require(sim.Actions.Any(x => x.Item2.Button == BattleButton.Attack && x.Item1 > uses[7] && x.Item1 < uses[8]),
                    "turret continues normal attack between R presses");
            }
        }

        private static void HsinChain()
        {
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Indicators.Add("hsin_lib1");
                sim.Hud.Skills[0] = SkillState.Cooling;
                long setup = -1, spentHeavy = -1, animationUntil = 0;
                sim.Run((a, t, h) =>
                {
                    if (a.Button == BattleButton.Liberation)
                    {
                        h.Skills[2] = SkillState.Cooling;
                        animationUntil = t + 300;
                        if (setup < 0) { setup = t; h.Indicators.Remove("hsin_lib1"); }
                    }
                    if (a.Button == BattleButton.Resonance && setup >= 0)
                    {
                        h.Indicators.Remove("e_forte");
                        h.Indicators.Add("hsin_h2");
                    }
                    if (a.Kind == ActionKind.Release && a.Button == BattleButton.Attack)
                        spentHeavy = t;
                },
                (t, h) =>
                {
                    h.Valid = t >= animationUntil;
                    if (setup >= 0 && t >= setup + 950 && !sim.Actions.Any(a => a.Item2.Button == BattleButton.Resonance))
                        h.Indicators.Add("e_forte");
                    var heavy = sim.Actions.FirstOrDefault(a => a.Item2.Kind == ActionKind.Hold);
                    if (heavy != null && t >= heavy.Item1 + 490) h.Indicators.Remove("hsin_h2");
                    if (spentHeavy >= 0 && t >= spentHeavy + 600 && !h.Indicators.Contains("hsin_lib2"))
                    {
                        h.Skills[2] = SkillState.Ready;
                        h.Indicators.Add("hsin_lib2");
                    }
                });
                Require(spentHeavy >= 0 && sim.Actions.Count(a => a.Item2.Button == BattleButton.Liberation) == 2,
                    "expected R1, confirmed heavy, and R2");
                Require(sim.Actions.Last(x => !x.Item2.DuringAnimation).Item2.Button == BattleButton.Liberation,
                    "finisher must end on R2 and its animation inputs");
            }
        }

        private static void HsinRequiresAnimation()
        {
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Indicators.Add("hsin_lib1");
                sim.Hud.Skills[0] = SkillState.Cooling;
                int resonance = 0;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation)
                    {
                        hud.Skills[2] = SkillState.Cooling;
                        hud.Indicators.Remove("hsin_lib1");
                        hud.Skills[0] = SkillState.Ready;
                    }
                    if (action.Button == BattleButton.Resonance)
                    {
                        resonance++;
                        hud.Skills[0] = SkillState.Cooling;
                    }
                });
                Require(resonance == 1 && sim.Actor.LastLiberation < 0, "a failed cast should leave the ordinary E branch available");
            }
        }

        private static void HsinHiddenHeavy()
        {
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Indicators.Add("hsin_h2");
                sim.Hud.Skills[2] = SkillState.Cooling;
                long hold = -1, released = -1, liberation = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold) hold = time;
                    if (action.Kind == ActionKind.Release) released = time;
                    if (action.Button == BattleButton.Liberation)
                    {
                        liberation = time;
                        hud.Skills[2] = SkillState.Cooling;
                    }
                }, (time, hud) =>
                {
                    hud.Valid = (hold < 0 || time >= hold + 600) && (liberation < 0 || time >= liberation + 400);
                    if (hold >= 0 && time >= hold + 50) hud.Indicators.Remove("hsin_h2");
                    if (hold >= 0 && time == hold + 800) hud.Skills[2] = SkillState.Ready;
                });
                Require(hold >= 0 && released - hold == 300 && liberation >= hold + 800 &&
                    sim.Actions.All(x => x.Item2.Button != BattleButton.Resonance && x.Item2.Button != BattleButton.Echo),
                    "release while the HUD is still hidden, then reserve the turn for the second liberation");
            }
        }

        private static void HiyukiHeldFinisher()
        {
            using (var sim = new Simulation("Hiyuki"))
            {
                sim.Hud.LockOffset = 555;
                sim.Hud.LongActionLayout = 2;
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                sim.Hud.Indicators.Add("hiyuki_lib_forte");
                long heldAt = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold && action.Button == BattleButton.Attack)
                        hud.Indicators.Remove("hiyuki_lib_forte");
                    if (action.Kind == ActionKind.Hold && action.Button == BattleButton.Liberation && !action.ReassertHold)
                    {
                        heldAt = time;
                        hud.Skills[2] = SkillState.Cooling;
                        hud.Cooldowns[2] = .8;
                    }
                }, (time, hud) => hud.Valid = heldAt < 0 || time < heldAt + 1000 || time >= heldAt + 1500);
                var held = sim.Actions.Where(x => x.Item2.Button == BattleButton.Liberation).ToArray();
                Require(heldAt >= 0 && held.Length == 6, "one physical hold, four repeated down events and one release");
                Require(held.Skip(1).Take(4).All(x => x.Item2.Kind == ActionKind.Hold && x.Item2.ReassertHold) &&
                    held.Take(5).Zip(held.Skip(1), (a, b) => b.Item1 - a.Item1).All(gap => gap == 200),
                    "reassert R every 200 ms while the residual cooldown permits it");
                Require(held.Last().Item2.Kind == ActionKind.Release && held.Last().Item1 == heldAt + 1000 &&
                    sim.Actions.All(x => x.Item2.Button != BattleButton.Interact), "release at HUD disappearance with no F");
            }
        }

        private static void SuomingBranches()
        {
            var settings = CombatSettings.Defaults();
            using (var support = new Simulation("Suoming", settings: settings))
            {
                support.Run(liberationMs: 400);
                Require(support.Actor.FreeIntro && support.Actions.All(a => a.Item2.Button != BattleButton.Resonance),
                    "support must pass Unison instead of consuming it");
            }
            settings.SuomingDps = true;
            using (var main = new Simulation("Suoming", true, settings: settings))
            {
                main.Hud.Indicators.Add("suoming_forte_e");
                main.Run((a, t, h) =>
                {
                    if (a.Button == BattleButton.Liberation) h.Skills[2] = SkillState.Cooling;
                    if (a.Kind == ActionKind.Release) h.Indicators.Remove("suoming_forte_e");
                }, liberationMs: 400);
                Require(!main.Actor.FreeIntro && main.Actions.Any(a => a.Item2.Kind == ActionKind.Hold &&
                    a.Item2.Button == BattleButton.Resonance), "main rotation needs the enhanced hold");
            }
        }

        private static void SuomingHoldSequence()
        {
            foreach (bool persistent in new[] { false, true })
            {
                var settings = CombatSettings.Defaults();
                settings.SuomingDps = true;
                using (var sim = new Simulation("Suoming", settings: settings))
                {
                    sim.Hud.Skills[0] = SkillState.Cooling;
                    sim.Hud.Indicators.Add("suoming_forte_e");
                    int holds = 0;
                    long finalHeldAt = -1;
                    sim.Run((action, time, hud) =>
                    {
                        if (action.Button == BattleButton.Resonance && action.Kind == ActionKind.Hold)
                        {
                            holds++;
                            if (holds == (persistent ? 3 : 2)) finalHeldAt = time;
                        }
                        if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                    }, (time, hud) =>
                    {
                        if (!persistent && finalHeldAt >= 0 && time >= finalHeldAt + 2000)
                            hud.Indicators.Remove("suoming_forte_e");
                        if (finalHeldAt >= 0 && time >= finalHeldAt + 6000) hud.Skills[0] = SkillState.Ready;
                    }, liberationMs: 400);
                    var resonance = sim.Actions.Where(a => a.Item2.Button == BattleButton.Resonance).ToArray();
                    Require(holds == (persistent ? 3 : 2) && resonance.Length == 2 * holds, "bounded and paired hold attempts");
                    for (int i = 0; i < resonance.Length; i++)
                    {
                        Require(resonance[i].Item2.Kind == (i % 2 == 0 ? ActionKind.Hold : ActionKind.Release), "every hold has a release");
                        if (i % 2 == 0 && i > 0) Require(resonance[i].Item1 - resonance[i - 1].Item1 >= 100, "allow reset between holds");
                    }
                    Require(resonance.Last().Item1 >= finalHeldAt + 6000 && sim.Actions.Last().Item2.Button == BattleButton.Echo,
                        "release only when the ready wait ends, then use echo");
                }
            }
        }

        private static void DoulingPhases()
        {
            var memory = new FighterMemory { Id = "Douling" };
            using (var first = new Simulation("Douling", actor: memory))
            {
                first.Run();
                Require(memory.Phase == 1 && first.Actions.Any(a => a.Item2.Button == BattleButton.Resonance),
                    "opening must set up the second turn");
            }
            using (var second = new Simulation("Douling", actor: memory))
            {
                second.Hud.GuaCount = 1;
                second.Run();
                Require(memory.Phase == 0 && second.Actions.Any(a => a.Item2.Button == BattleButton.Jump) &&
                    !second.Actions.Any(a => a.Item2.Kind == ActionKind.Hold), "single hexagram does not authorize a hold");
            }
        }

        private static void ElectroRover()
        {
            using (var sim = new Simulation("RoverElectro", true))
            {
                sim.Hud.Indicators.Add("e_forte");
                sim.Hud.Forte = .45;
                sim.Hud.Skills[0] = SkillState.Cooling;
                sim.Run();
                Require(!sim.Actions.Any(a => a.Item2.Button == BattleButton.Resonance), "prompt alone authorized E");
            }
            using (var sim = new Simulation("RoverElectro", true))
            {
                sim.Hud.Indicators.Add("e_forte");
                sim.Hud.Forte = 1;
                sim.Hud.ForteFullColor = true;
                sim.Hud.Skills[0] = SkillState.Cooling;
                sim.Run((a, t, h) =>
                {
                    if (a.Button == BattleButton.Resonance)
                    {
                        h.Indicators.Remove("e_forte");
                        h.Forte = .3;
                        h.ForteFullColor = false;
                    }
                });
                var enhanced = sim.Actions.Where(a => a.Item2.Button == BattleButton.Resonance).ToArray();
                Require(enhanced.Length == 1 && enhanced[0].Item2.DownMilliseconds == 50,
                    "ready enhanced E must run once even if ordinary E is cooling");
            }
        }

        private static void LilyStates()
        {
            using (var asleep = new Simulation("Lily"))
            {
                asleep.Hud.Indicators.Add("lily_slumberland");
                asleep.Run();
                Require(asleep.Actions.Count == 0, "Slumberland must be preserved");
            }
            using (var ready = new Simulation("Lily"))
            {
                ready.Hud.Indicators.Add("mouse_forte");
                ready.Run();
                Require(ready.Actions.Any(a => a.Item2.Kind == ActionKind.Release && a.Item2.Button == BattleButton.Attack),
                    "no channel confirmation still requires the mouse-up");
            }
        }

        private static void LilyConcertoTopUp()
        {
            foreach (bool ready in new[] { false, true })
            using (var sim = new Simulation("Lily"))
            {
                sim.Hud.Indicators.Add("mouse_forte");
                if (ready) sim.Hud.Indicators.Add("lily_liberation");
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                double beforeLiberation = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation)
                    {
                        beforeLiberation = hud.Concerto;
                        hud.Skills[2] = SkillState.Cooling;
                    }
                    if (action.Button == BattleButton.Attack && action.Kind == ActionKind.Button)
                    {
                        hud.Concerto = Math.Min(1, hud.Concerto + .11);
                        hud.ConcertoFull = hud.Concerto == 1;
                    }
                });
                Require(sim.Hud.ConcertoFull, "the turn must top up the remainder before handing off");
                Require(ready ? beforeLiberation >= .8 && beforeLiberation < 1 : beforeLiberation < 0,
                    "only an available liberation starts at the 80% threshold");
            }
        }

        private static void RocciaRelease()
        {
            using (var sim = new Simulation("Roccia"))
            {
                sim.Hud.AirCharges = 2;
                sim.Run((a, t, h) =>
                {
                    if (a.Button == BattleButton.Liberation) h.Skills[2] = SkillState.Cooling;
                    if (a.Kind == ActionKind.Release && a.Button == BattleButton.Attack) h.AirCharges--;
                });
                Require(sim.Actions.Count(a => a.Item2.Kind == ActionKind.Hold && a.Item2.Button == BattleButton.Attack) == 2 &&
                    sim.Actions.Any(a => a.Item2.Kind == ActionKind.Release && a.Item2.Button == BattleButton.Forward),
                    "airborne sequence should use both charges and release forward");
            }
        }

        private static void PlannerIntegration()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            var hud = Snapshot();
            hud.Fighters[0] = "Sanhua";
            hud.Fighters[1] = "Cantarella";
            hud.Fighters[2] = "Verina";
            var planner = new BattlePlanner(settings);
            Require(planner.Observe(hud, 1000).Kind == ActionKind.Wait, "first frame must confirm");
            Require(planner.Observe(hud, 1110).Kind == ActionKind.Wait, "Sanhua waits briefly before holding attack");
            BattleAction first = planner.Observe(hud, 1130);
            Require(first.Kind == ActionKind.Hold && first.Button == BattleButton.Attack,
                "a named runner must enter Sanhua's hold program");
            planner.Sent(first, 1130);
            planner.Invalidate(1200);
            Require(planner.TakeReleaseRequest(), "window invalidation did not request mouse-up cleanup");
        }

        internal static void Replay(string reference, Action<string, Action> check)
        {
            check("built-in roster matches reference registry entry by entry", delegate
            {
                string registration = File.ReadAllText(Path.Combine(reference, "src", "char", "CharFactory.py"));
                var expected = new HashSet<string>(Regex.Matches(registration, "'cls': (\\w+)")
                    .Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
                var actual = new HashSet<string>(PatternBank.Portraits.Select(x => x.Fighter), StringComparer.Ordinal);
                Require(expected.SetEquals(actual), "missing or extra registered character");
            });
            check("all base roles match the supplied reference registry", delegate
            {
                string registration = File.ReadAllText(Path.Combine(reference, "src", "char", "CharFactory.py"));
                var roles = new Dictionary<string, BattleRole> { { "MAIN_DPS", BattleRole.Main },
                    { "SUB_DPS", BattleRole.Secondary }, { "HEALER", BattleRole.Support } };
                var matches = Regex.Matches(registration, "'cls'\\s*:\\s*(\\w+),\\s*'char_type'\\s*:\\s*CharType\\.(\\w+)");
                Require(matches.Count == 58, "unexpected reference roster format");
                foreach (Match entry in matches)
                {
                    CharacterKit kit = CharacterRoster.Find(entry.Groups[1].Value);
                    Require(kit != null && kit.Role == roles[entry.Groups[2].Value], "role mismatch: " + entry.Groups[1].Value);
                }
            });
            check("Hiyuki and Lily portraits and alternate skins", delegate
            {
                foreach (string name in new[] { "hiyuki1", "hiyuki3", "hiyuki-skin", "hiyuki-skin-inactive", "lily-start", "lily-heavy",
                    "lily-validation", "lily-slumberland" })
                using (var reader = new FrameReader())
                using (var bitmap = new Bitmap(Path.Combine(reference, "ok_templates", name + ".png")))
                {
                    PixelFrame frame = reader.Read(bitmap);
                    var vision = new CombatVision(CombatSettings.Defaults());
                    string fighter = name.StartsWith("hiyuki") ? "Hiyuki" : "Lily";
                    Require(vision.Identify(frame, 0) == fighter && vision.Identify(frame, 1) != fighter &&
                        vision.Identify(frame, 2) != fighter, name);
                }
            });
            check("Lily's ready, channeling and Slumberland visual states", delegate
            {
                foreach (string name in new[] { "start", "ready", "heavy", "validation", "slumberland" })
                using (var reader = new FrameReader())
                using (var bitmap = new Bitmap(Path.Combine(reference, "ok_templates", "lily-" + name + ".png")))
                {
                    HudSnapshot hud = ReadMarkers(reader.Read(bitmap), "Lily");
                    Require(hud.Shows("mouse_forte") == (name == "ready") &&
                        hud.Shows("lily_heavy") == (name == "heavy" || name == "validation") &&
                        hud.Shows("lily_slumberland") == (name == "slumberland") &&
                        hud.Shows("lily_liberation") == (name != "slumberland"), name);
                }
            });
            check("Buling's 1/3/4 hexagrams, including the bright scene and 1080p", delegate
            {
                foreach (string name in new[] { "g", "zzg", "gzzz", "gzzz_1080p" })
                using (var reader = new FrameReader())
                using (var bitmap = new Bitmap(Path.Combine(reference, "tests", "images", "douling_gua_" + name + ".png")))
                {
                    HudSnapshot hud = ReadMarkers(reader.Read(bitmap), "Douling");
                    int count = name.Split('_')[0].Length;
                    Require(hud.GuaCount == count, name + " saw " + hud.GuaCount + " instead of " + count);
                }
                using (var reader = new FrameReader())
                using (var bitmap = new Bitmap(Path.Combine(reference, "tests", "images", "33forte.png")))
                    Require(ReadMarkers(reader.Read(bitmap), "Douling").GuaCount == 0, "ground negative example");
            });
            check("Roccia aerial icon distinguishes independent and ground captures", delegate
            {
                foreach (string name in new[] { "positive", "negative" })
                using (var reader = new FrameReader())
                using (var crop = new Bitmap(Path.Combine(reference, "tests", "images", "roccia_air_" + name + ".png")))
                using (var canvas = new Bitmap(3840, 2160))
                {
                    using (Graphics graphics = Graphics.FromImage(canvas))
                    {
                        graphics.Clear(Color.Black);
                        graphics.DrawImageUnscaled(crop, 1580, 1930);
                    }
                    HudSnapshot hud = ReadMarkers(reader.Read(canvas), "Roccia");
                    Require((hud.AirCharges > 0) == (name == "positive"), name + " saw " + hud.AirCharges);
                }
            });
            check("Roccia animation frames agree with the captured reference sequence", () => ReplayRocciaAnimation(reference));
            check("Suoming enhanced E and HUD recognition with mixed parties", delegate
            {
                using (var reader = new FrameReader())
                using (var suoming = new Bitmap(Path.Combine(reference, "ok_templates", "61.png")))
                using (var combat = new Bitmap(Path.Combine(reference, "tests", "images", "in_combat.png")))
                {
                    PixelFrame first = reader.Read(suoming);
                    var vision = new CombatVision(CombatSettings.Defaults());
                    Require(vision.Identify(first, 0) == "Suoming" &&
                        ReadMarkers(first, "Suoming").Shows("suoming_forte_e"), "Suoming signal");
                    HudSnapshot hud = new HudVision(CombatSettings.Defaults()).Read(reader.Read(combat));
                    Require(hud.Valid && hud.Locked && hud.Fighters[0] == "Iuno" && hud.Fighters[1] == "Roccia",
                        "normal capture failed mapping recognized portraits to slots");
                }
            });
        }

        private static void ReplayRocciaAnimation(string reference)
        {
            string path = Path.Combine(reference, "tests", "images", "roccia_air_animation");
            var manifest = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                File.ReadAllText(Path.Combine(path, "sequences.json")));
            var mismatches = new List<string>();
            foreach (Dictionary<string, object> sequence in (IList)manifest["sequences"])
            foreach (int scale in new[] { 1, 2 })
            using (var strip = new Bitmap(Path.Combine(path, (string)sequence["file"])))
            using (var frame = new Bitmap(3840 / scale, 2160 / scale, PixelFormat.Format32bppRgb))
            using (var reader = new FrameReader())
            using (Graphics canvas = Graphics.FromImage(frame))
            {
                var samples = (IList)sequence["samples"];
                var offsets = new long[samples.Count];
                var charges = new int[samples.Count];
                for (int index = 0; index < samples.Count; index++)
                {
                    var sample = (Dictionary<string, object>)samples[index];
                    offsets[index] = (long)Math.Round(Convert.ToDouble(sample["offset"]) * 1000);
                    canvas.DrawImage(strip, new Rectangle(1580 / scale, 1930 / scale, 610 / scale, 110 / scale),
                        new Rectangle(0, index * 110, 610, 110), GraphicsUnit.Pixel);
                    HudSnapshot hud = ReadMarkers(reader.Read(frame), "Roccia");
                    charges[index] = hud.AirCharges;
                    if (scale == 1 && (hud.AirCharges > 0) != (bool)sample["visible"])
                        mismatches.Add(sequence["trial"] + "/" + index + " " + sample["phase"] + " gave " + hud.AirCharges);
                    int expected = index == 0 ? 2 : index == samples.Count - 2 ? 1 : index == samples.Count - 1 ? 0 : -1;
                    if (expected >= 0 && hud.AirCharges != expected)
                        mismatches.Add(sequence["trial"] + "/" + index + " at " + frame.Height + "p expected " + expected + " got " + hud.AirCharges);
                }
                string holdFailure = ReplayRocciaHolds(offsets, charges);
                if (holdFailure != null) mismatches.Add(sequence["trial"] + " at " + frame.Height + "p " + holdFailure);
            }
            Require(mismatches.Count == 0, string.Join(", ", mismatches));
        }

        private static string ReplayRocciaHolds(long[] offsets, int[] charges)
        {
            var actor = new FighterMemory { Id = "Roccia" };
            var context = new TurnContext(actor, new[] { actor }, CombatSettings.Defaults(), false, null, 0);
            var hud = Snapshot();
            long mouseDown = -1;
            int holds = 0, index = 0;
            bool forwardHeld = false;
            using (var turn = new CharacterTurn(Rotations.RocciaAir(context), context))
            for (long time = 0; time < 5000 && !turn.Finished; time += 50)
            {
                while (index + 1 < offsets.Length && offsets[index + 1] <= time) index++;
                hud.AirCharges = charges[index];
                if (index < offsets.Length - 1) hud.Indicators.Add("mouse_forte");
                else hud.Indicators.Remove("mouse_forte");
                BattleAction action = turn.Observe(hud, time);
                if (action.Kind == ActionKind.Wait) continue;
                if (action.Button == BattleButton.Forward)
                    forwardHeld = action.Kind == ActionKind.Hold;
                if (action.Button == BattleButton.Attack && action.Kind == ActionKind.Hold) mouseDown = time;
                if (action.Button == BattleButton.Attack && action.Kind == ActionKind.Release)
                {
                    if (time - mouseDown != 400) return "ground-length hold in an aerial phase";
                    mouseDown = -1;
                    holds++;
                }
                turn.Sent(action, time);
            }
            return holds < 2 || index != offsets.Length - 1 || forwardHeld || mouseDown >= 0 ?
                "did not observe depletion and release W and mouse" : null;
        }

        private static HudSnapshot ReadMarkers(PixelFrame frame, string id)
        {
            var settings = CombatSettings.Defaults();
            settings.PartyCharacters[0] = id;
            HudSnapshot hud = Snapshot();
            var vision = new CombatVision(settings);
            vision.ReadParty(frame, hud);
            vision.Read(frame, hud, true);
            return hud;
        }
    }
}
