using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
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
            internal long FinishedAt { get { return clock; } }
            internal TurnContext Context { get { return turn.Context; } }
            internal FighterMemory[] Team { get { return turn.Context.Team; } }

            internal Simulation(string id, bool intro = false, string outro = null, CombatSettings settings = null,
                FighterMemory actor = null, bool fastPerform = false, long start = 1000,
                Func<TurnContext, IEnumerable<Move>> moves = null, bool? openWorldAutoCombat = null, bool farmEchoTask = false, int entryDelay = 0)
            {
                clock = start + entryDelay;
                Actor = actor ?? new FighterMemory { Id = id };
                Hud = Snapshot();
                var members = new[] { Actor, new FighterMemory { Id = "Verina" }, new FighterMemory { Id = "Sanhua" } };
                CombatSettings options = settings ?? CombatSettings.Defaults();
                CharacterKit previous = CharacterRoster.Find(outro);
                var context = new TurnContext(Actor, members, options, intro, outro, start,
                    previous != null && previous.RoleFor(options) == BattleRole.Secondary, (hud, now) => fastPerform,
                    openWorldAutoCombat: openWorldAutoCombat, farmEchoTask: farmEchoTask);
                turn = moves == null ? new CharacterTurn(CharacterRoster.Find(id), context) : new CharacterTurn(moves(context), context);
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
            check("task scene policy controls fire echo and Encore independently of the realm HUD", OpenWorldCombatPolicy);
            check("ordinary tasks allow liberation and preserve explicit account and farming policies", TaskLiberationPreference);
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
            check("Hsin preserves action priority and independent opening and finisher deadlines", HsinDeadlines);
            check("Hsin finishes the normal resonance retry before checking the next phase", HsinOrdinaryResonanceRetry);
            check("Hsin enhanced resonance retries only on confirmed prompts and remains time bounded", HsinPulseBoundaries);
            check("Hsin locks the finisher budget and retries a returning enhanced prompt", HsinFinisherRecovery);
            check("Hsin protects only an accepted H2 hold and resumes retargeting at four seconds", HsinTargetProtection);
            check("Hsin starts a fresh rotation after a completed second liberation", HsinFreshTurn);
            check("Phrolova hands off immediately after a confirmed opening liberation", PhrolovaOpeningHandoff);
            check("Yangyang SP uses one echo while holding and settles the release before handing off", YangyangSpHold);
            check("Hiyuki reasserts the held finisher through short cooldown and releases on animation", HiyukiHeldFinisher);
            check("Suoming support passes the free intro while DPS holds the enhanced skill", SuomingBranches);
            check("Suoming releases between E attempts and keeps the final hold through the ready wait", SuomingHoldSequence);
            check("Suoming orders liberation, ordinary E, attacks and the enhanced hold", SuomingForteTiming);
            check("Buling has separate turns and skips a heavy with one hexagram", DoulingPhases);
            check("Buling releases on two depleted reads and retries only valid heavy holds", DoulingHoldRules);
            check("Electro Rover requires full forte as well as the enhanced prompt", ElectroRover);
            check("Electro Rover confirms both visual signals before recording the enhanced cast", ElectroRoverEnhanced);
            check("Electro Rover charges for eight or twelve seconds and keeps liberation ordering", ElectroRoverOpening);
            check("Rover dispatches the recognized form and rebuilds Aero team state", RoverForms);
            check("Rover locks its battle form until reset", RoverFormLifetime);
            check("Aemeath respects buff setup, enhanced resonance, and immediate liberation-two handoff", AemeathRotation);
            check("Chisa uses the configured DPS or bounded support rotation", ChisaTurns);
            check("Chisa support retries normal resonance before ending the short turn", ChisaSupportRetry);
            check("Chisa DPS first sent-but-timed-out resonance reopens the phase", ChisaTimedOutReset);
            check("Denia remembers only the second liberation until the next battle", DeniaSecondLiberationLifetime);
            check("Verina permits her next heavy at the eight-second boundary", VerinaHeavyInterval);
            check("Calcharo gives a returning intro HUD its settle delay before the normal rotation", CalcharoIntroHudRecovery);
            check("Lily hands off Slumberland and releases a timed-out heavy", LilyStates);
            check("Lily finishes concerto if liberation is unavailable or the attempted cast fails", LilyConcertoTopUp);
            check("Roccia releases the movement key after aerial and ground branches", RocciaRelease);
            check("Roccia preserves a spare aerial charge without shortening ground wind-up", RocciaChargeBranches);
            check("Roccia starts a new plunge with no stored aerial readiness", RocciaFreshPlunge);
            check("Camellya restarts stalled heavy attack and ends on actual charge depletion", CamellyaCharge);
            check("Camellya starts liberation while holding in Budding and then reholds", CamellyaBudding);
            check("Qiuyuan extends field time only on sub-DPS intro", QiuyuanIntroRole);
            check("ShoreKeeper grants five airborne dodge responses and then expires", ShoreKeeperDodgeWindow);
            check("Xigelika selects mouse or resonance hold from the action bar", XigelikaHeavy);
            check("Linnai accepts Aemeath's longer acceleration window and waits between kicks", LinnaiAccelerate);
            check("Qingxiao confirms the second heavy, executes and waits for liberation", QingxiaoHeavyFinish);
            check("full buff setup requires intro and two active teammate timers", FullBuffEligibility);
            check("Luhesi clears an interrupted elbow strike and counts only three confirmed kicks", LuhesiKicks);
            check("Lucy holds the two enhanced heavies before sustained charge and eleven post-R attacks", LucyFinisher);
            check("Mornye keeps the 23-second grounded cooldown after a confirmed aerial heavy", MornyeCooldown);
            check("Galbrena releases the opening heavy early when a teammate has mandatory priority", GalbrenaPriority);
            check("execution alternates interaction and normal attack and then ends", BreakSequence);
            check("Carlotta stores her heavy attack for the Zhezhi outro window", CarlottaHandoff);
            check("4C Carlotta holds W for liberation without Zhezhi interlock", CarlottaFarmContext);
            check("Zhezhi keeps the attack held through three enhanced resonance casts for Carlotta", ZhezhiBlueChain);
            check("Ciaccona chooses wind or spectro recital by teammate slot order", CiacconaRecital);
            check("Cartethyia transforms and casts the Fleurdelys finisher before returning to resonance", CartethyiaForms);
            check("Changli starts a held attack at 1.5 seconds into liberation and releases after the gauge clears", ChangliLiberationHold);
            check("Phoebe keeps the first support turn intact and casts before leaving a full-Blaze Zani team", PhoebeSupportLink);
            check("Phoebe waits for the visible HUD to settle before prayer selection", PhoebePrayerSettle);
            check("Jinhsi preserves separate intro-cooldown and Incarnation branches", JinhsiIncarnation);
            check("Jinhsi accepts a delayed flight cooldown beyond the old six-second limit", JinhsiDelayedFlight);
            check("Augusta releases Majesty on animation, attacks during it and only then sends echo", AugustaMajesty);
            check("Zani distinguishes an almost full Blaze bar from the direct liberation threshold", ZaniThreshold);
            check("Zani cancels the last Nightfall smash and ends the liberated stance with raw R", ZaniNightfallAndFinisher);
            check("Zani gray resonance extends the rolling finisher deadline past nine seconds", ZaniRollingFinisher);
            check("Lucilla charges through short cooldown and stays out when the energy icon is already lit", LucillaCharge);
            check("Lucilla does not release liberation at or beyond the 7.2 second charge budget", LucillaChargeDeadline);
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
            Require(CharacterRoster.Find("Qingxiao").Role == BattleRole.Main &&
                CharacterKit.Ring("Qingxiao") == ConcertoColor.Aero, "Qingxiao's registered wind DPS role");
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

        private static void FullBuffEligibility()
        {
            var method = typeof(Rotations).GetMethod("HasTwoBuffs", BindingFlags.Static | BindingFlags.NonPublic);
            var options = CombatSettings.Defaults();
            var actor = new FighterMemory { Id = "Qingxiao" };
            var healer = new FighterMemory { Id = "Verina", BuffStarted = 1000, BuffDuration = 2000 };
            var support = new FighterMemory { Id = "Denia", BuffStarted = 1000, BuffDuration = 1000 };
            var team = new[] { actor, healer, support };
            foreach (bool intro in new[] { false, true })
            {
                var context = new TurnContext(actor, team, options, intro, null, 1600);
                context.Now = 1999;
                Require((bool)method.Invoke(null, new object[] { context }) == intro,
                    "both fresh buffs count only on intro");
                context.Now = 2000;
                Require(!(bool)method.Invoke(null, new object[] { context }), "one expired buff ends full eligibility");
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

        private static void CarlottaFarmContext()
        {
            using (var sim = new Simulation("Carlotta", farmEchoTask: true))
            {
                sim.Team[1].Id = "Zhezhi";
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                }, liberationMs: 400);
                BattleAction[] actions = sim.Actions.Select(x => x.Item2).ToArray();
                Require(actions[0].Kind == ActionKind.Hold && actions[0].Button == BattleButton.Forward &&
                    actions[1].Button == BattleButton.Liberation && actions.Any(a => a.Kind == ActionKind.Release &&
                    a.Button == BattleButton.Forward), "4C uses independent Carlotta, holding W across the R call");
                Require(!sim.Actor.LiberationPrimed, "4C must not prime interlock resources");
            }
            using (var sim = new Simulation("Zhezhi", farmEchoTask: true))
            {
                sim.Team[1].Id = "Carlotta";
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                }, liberationMs: 400);
                Require(sim.Actions[0].Item2.Button == BattleButton.Liberation,
                    "4C also skips Zhezhi's side of the Carlotta link");
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

        private static void PhoebePrayerSettle()
        {
            var method = typeof(Rotations).GetMethod("PhoebePrayer", BindingFlags.Static | BindingFlags.NonPublic);
            var state = new PhoebeMemory { Mode = 2 };
            using (var sim = new Simulation("Phoebe", moves: c =>
                (IEnumerable<Move>)method.Invoke(null, new object[] { c, state })))
            {
                long held = -1;
                sim.Run((action, time, h) =>
                {
                    if (action.Kind == ActionKind.Hold) held = time;
                }, (time, h) => h.ForteFullColor = time >= 1250 && (held < 0 || time < held + 450));
                Require(held == 1500 && state.Prayer == PhoebePrayerResult.Entered,
                    "wait 500ms then evaluate the newly visible prayer cue");
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

        private static void JinhsiDelayedFlight()
        {
            using (var sim = new Simulation("Jinhsi", intro: true))
            {
                sim.Hud.Skills[1] = sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Run(frame: (time, h) => h.Skills[0] = time >= 8000 ? SkillState.Cooling : SkillState.Ready);
                Require(sim.Actor.Phase == 2 && sim.Actions.Any(x => x.Item1 >= 7000 && x.Item2.Button == BattleButton.Resonance),
                    "continue checking delayed flight E then enter Incarnation rather than exit prematurely");
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

        private static void ZaniRollingFinisher()
        {
            var options = CombatSettings.Defaults();
            options.UseLiberation = false;
            using (var sim = new Simulation("Zani", settings: options))
            {
                sim.Hud.ZaniForm = 2;
                sim.Hud.ResonanceWhiteRatio = 0;
                sim.Run(frame: (time,h) => { if (time >= 11000) h.ZaniForm = 1; });
                Require(sim.Actions.Any(x => x.Item1 >= 10000 && x.Item2.Button == BattleButton.Liberation) && !sim.Actor.ZaniLiberated,
                    "each gray reading extends the reference rolling budget; return of normal form ends it");
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

        private static void LucillaChargeDeadline()
        {
            foreach (int delay in new[] { 0, 800 })
            foreach (long readyAfter in new[] { 6000L, 7200L, 7500L })
            using (var sim = new Simulation("Lucilla", start: 1000, entryDelay: delay))
            {
                sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Hud.Cooldowns[2] = 0;
                bool used = false;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation)
                    {
                        used = true;
                        hud.Skills[2] = SkillState.Unavailable;
                    }
                    if (action.Kind == ActionKind.Release && action.Button == BattleButton.Attack && used)
                        hud.Concerto = 0;
                }, (time, hud) =>
                {
                    if (!used && time - 1000 >= readyAfter) hud.Skills[2] = SkillState.Ready;
                });
                Require(used == (readyAfter < 7200), "a ready R outside the charge window must not start liberation: " + readyAfter + " after delayed HUD=" + delay);
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

        private static void HsinDeadlines()
        {
            foreach (bool intro in new[] { false, true })
            using (var sim = new Simulation("Hsin", intro))
            {
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                long end = 0;
                sim.Run(frame: (time, hud) => end = time);
                long budget = intro ? 12000 : 6000;
                Require(end >= 1000 + budget && end <= 1150 + budget, "Hsin opening budget: " + end);
                Require(sim.Actions.Count > 0 && sim.Actions.All(a => a.Item2.Button == BattleButton.Attack),
                    "without either phase marker, generic liberation must not override normal attacks");
            }
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Indicators.UnionWith(new[] { "hsin_h2", "hsin_lib2", "e_forte" });
                sim.Run(liberationMs: 400);
                Require(sim.Actions.First().Item2.Button == BattleButton.Liberation &&
                    sim.Actions.All(a => a.Item2.Button != BattleButton.Resonance && a.Item2.Kind != ActionKind.Hold),
                    "ready second liberation must take precedence over the heavy and enhanced skill");
            }
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Indicators.Add("hsin_lib2");
                sim.Hud.Skills[1] = sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Resonance) hud.Skills[0] = SkillState.Cooling;
                });
                Require(sim.Actions.Count(a => a.Item2.Button == BattleButton.Resonance) == 1 &&
                    sim.Actions.Any(a => a.Item2.Button == BattleButton.Attack) &&
                    sim.Actions.All(a => a.Item2.Button != BattleButton.Liberation),
                    "unavailable R2 must not starve ordinary resonance or normal attacks");
            }
            foreach (bool timeout in new[] { false, true })
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = sim.Hud.Skills[2] = SkillState.Cooling;
                long hold = -1, released = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind == ActionKind.Hold) hold = time;
                    if (action.Kind == ActionKind.Release) released = time;
                }, (time, hud) =>
                {
                    if (time < 6800) return;
                    if (hold < 0 || timeout || time - hold < 300 || time - hold >= 400 && time - hold < 600)
                        hud.Indicators.Add(timeout ? "hsin_h2" : "hsin_h1");
                    else hud.Indicators.Remove("hsin_h1");
                    if (released >= 0) hud.Skills[2] = SkillState.Ready;
                });
                Require(hold >= 6800 && released - hold == (timeout ? 1200 : 850) &&
                    sim.Actions.Count(a => a.Item2.Kind == ActionKind.Hold) == 1,
                    "Hsin late heavy retains its deadline and ignores a transient dropout");
                Require(sim.Actions.All(a => a.Item2.Button != BattleButton.Liberation),
                    "a timed-out heavy cannot enable generic second liberation");
            }
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                long firstCast = -1, secondCast = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button != BattleButton.Liberation) return;
                    if (firstCast < 0) firstCast = time;
                    else secondCast = time;
                }, (time, hud) =>
                {
                    if (firstCast < 0 && time >= 6750) hud.Indicators.Add("hsin_lib1");
                    if (firstCast >= 0) hud.Indicators.Remove("hsin_lib1");
                    if (firstCast >= 0 && time - firstCast >= 10500)
                    {
                        hud.Indicators.Add("hsin_lib2");
                        hud.Skills[2] = SkillState.Ready;
                    }
                }, liberationMs: 400);
                Require(firstCast >= 6750 && secondCast >= firstCast + 10500,
                    "a confirmed late R1 opens a fresh finisher budget");
                Require(sim.Actions.All(a => a.Item2.Button != BattleButton.Resonance),
                    "absent enhanced prompts cannot produce resonance during the finisher");
            }
        }

        private static void HsinOrdinaryResonanceRetry()
        {
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Skills[1] = SkillState.Cooling;
                int casts = 0;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button != BattleButton.Resonance) return;
                    if (++casts == 1) hud.Indicators.Add("hsin_lib2");
                    else hud.Skills[0] = SkillState.Cooling;
                }, liberationMs: 400);
                var beforeLiberation = sim.Actions.Select(item => item.Item2)
                    .TakeWhile(action => action.Button != BattleButton.Liberation).ToArray();
                Require(sim.Actions.Any(item => item.Item2.Button == BattleButton.Liberation) &&
                    beforeLiberation.Where(a => a.Button == BattleButton.Resonance).Count() == 2 &&
                    beforeLiberation.Select(a => a.Button).SequenceEqual(new[] {
                        BattleButton.Resonance, BattleButton.Attack, BattleButton.Resonance }),
                    "retry the still ready E after a separate normal attack and fresh observation");
            }
        }

        private static void HsinPulseBoundaries()
        {
            var method = typeof(Rotations).GetMethod("HsinEnhancedResonance", BindingFlags.Static | BindingFlags.NonPublic);
            Func<TurnContext, IEnumerable<Move>> program = context => (IEnumerable<Move>)method.Invoke(null, new object[] { context });
            foreach (string scenario in new[] { "swallowed", "flicker", "absent", "persistent" })
            using (var sim = new Simulation("Hsin", moves: program))
            {
                var sent = new List<long>();
                long end = 0;
                sim.Run((action, time, hud) =>
                {
                    Require(action.Button == BattleButton.Resonance && hud.Shows("e_forte"),
                        "enhanced resonance may only send on a visible prompt");
                    sent.Add(time);
                }, (time, hud) =>
                {
                    end = time;
                    bool show = scenario == "persistent" || scenario == "swallowed" && sent.Count < 2 ||
                        scenario == "flicker" && (time < 1100 || time >= 1200 && time < 1600);
                    if (show) hud.Indicators.Add("e_forte");
                    else hud.Indicators.Remove("e_forte");
                });
                int expected = scenario == "persistent" ? 6 : scenario == "flicker" ? 3 : scenario == "swallowed" ? 2 : 0;
                Require(sent.Count == expected && sent.Zip(sent.Skip(1), (first, second) => second - first).All(gap => gap >= 250),
                    scenario + " resonance retry spacing");
                if (scenario == "persistent") Require(end >= 2500 && end <= 2550, "a missed key must not exceed the enhanced retry window");
                if (scenario == "flicker") Require(end >= 1850 && end < 1970, "wait for stable prompt consumption");
                bool confirmed = scenario == "swallowed" || scenario == "flicker";
                Require(sim.Context.Succeeded == confirmed && (confirmed ? sim.Actor.LastSkill[0] == end : sim.Actor.LastSkill[0] == -60000),
                    scenario + " records a skill only after stable disappearance");
            }
        }

        private static void HsinFinisherRecovery()
        {
            foreach (string state in new[] { "returning", "delayed", "no-prompt", "no-r2", "both-r-fail" })
            using (var sim = new Simulation("Hsin"))
            {
                sim.Hud.Indicators.Add("hsin_lib1");
                sim.Hud.Skills[0] = SkillState.Cooling;
                long r1 = -1, r2 = -1, hideUntil = -1, hold = -1, released = -1;
                int liberations = 0;
                var resonance = new List<long>();
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation)
                    {
                        liberations++;
                        if (r1 < 0) r1 = time; else r2 = time;
                        hud.Skills[2] = SkillState.Cooling;
                        if (state != "both-r-fail") hideUntil = time + 350;
                    }
                    if (action.Button == BattleButton.Resonance)
                        resonance.Add(time);
                    if (action.Kind == ActionKind.Hold)
                        hold = time;
                    if (action.Kind == ActionKind.Release)
                        released = time;
                }, (time, hud) =>
                {
                    hud.Valid = time >= hideUntil;
                    if (r1 < 0) return;
                    long elapsed = time - r1;
                    if (state == "both-r-fail")
                    {
                        if (elapsed >= 1200 && r2 < 0)
                        {
                            hud.Indicators.Remove("hsin_lib1");
                            hud.Indicators.Add("hsin_lib2");
                            hud.Skills[2] = SkillState.Ready;
                        }
                        return;
                    }
                    hud.Skills[0] = SkillState.Ready;
                    if (state == "no-prompt" && elapsed >= 2000) hud.Skills[2] = SkillState.Ready;
                    if (state == "no-prompt" || state == "no-r2") hud.Skills[1] = SkillState.Ready;
                    bool prompt = state == "returning" && (elapsed >= 650 && elapsed < 750 ||
                        elapsed >= 1350 && resonance.Count < 2) ||
                        state == "delayed" && elapsed >= 2000 && resonance.Count == 0;
                    if (prompt) hud.Indicators.Add("e_forte"); else hud.Indicators.Remove("e_forte");
                    bool consumed = state == "no-r2" && elapsed >= 750 ||
                        state == "delayed" && resonance.Count == 1 && time >= resonance[0] + 650 ||
                        state == "returning" && resonance.Count == 2 && time >= resonance[1] + 650;
                    if (consumed && (hold < 0 || time < hold + 300)) hud.Indicators.Add("hsin_h2");
                    else hud.Indicators.Remove("hsin_h2");
                    if ((state == "returning" || state == "delayed") && hold >= 0 && time >= hold + 2800 && r2 < 0)
                        hud.Skills[2] = SkillState.Ready;
                });
                Require(r1 >= 0 && sim.Context.ContinueAfterLockLossUntil == 0, "turn cleanup " + state);
                if (state == "both-r-fail")
                {
                    Require(r2 > r1 && sim.Actor.LastLiberation == -1 && sim.FinishedAt - r1 < 6300,
                        "an R without a confirmed animation must not open or complete the finisher");
                    continue;
                }
                if (state == "returning" || state == "delayed")
                {
                    int expected = state == "returning" ? 2 : 1;
                    Require(resonance.Count == expected && resonance.Last() - r1 >= (expected == 2 ? 1350 : 2000) &&
                        released - hold >= 550 && r2 >= hold + 2800, state + " enhanced E, complete H2, then delayed R2");
                }
                else
                {
                    Require(resonance.Count == 0 && r2 < 0 && liberations == 1 &&
                        sim.FinishedAt - r1 >= 18000 && sim.FinishedAt - r1 < 18600 &&
                        !sim.Actions.Any(a => a.Item2.Button == BattleButton.Echo),
                        state + " finishes the original budget without falling back to E or Q");
                    Require((released >= 0) == (state == "no-r2"), "only a confirmed H2 enables generic R2");
                }
            }
        }

        private static void HsinFreshTurn()
        {
            var actor = new FighterMemory { Id = "Hsin" };
            using (var first = new Simulation("Hsin", actor: actor))
            {
                first.Hud.Skills[0] = first.Hud.Skills[1] = SkillState.Cooling;
                first.Hud.Indicators.Add("hsin_lib2");
                first.Run(liberationMs: 400);
                Require(first.Actions.Count(action => action.Item2.Button == BattleButton.Liberation) == 1 &&
                    actor.LastLiberation > 0 && first.Context.ContinueAfterLockLossUntil == 0,
                    "complete R2 and clear the current turn");
            }
            using (var subsequent = new Simulation("Hsin", actor: actor, start: 20000))
            {
                subsequent.Hud.Skills[1] = SkillState.Cooling;
                subsequent.Hud.Indicators.Add("hsin_lib1");
                subsequent.Run((action, at, hud) =>
                {
                    if (action.Button == BattleButton.Resonance) hud.Skills[0] = SkillState.Cooling;
                }, liberationMs: 400);
                Require(subsequent.Actions.Count(action => action.Item2.Button == BattleButton.Liberation) == 1 &&
                    subsequent.Actions.First(action => action.Item2.Button == BattleButton.Resonance).Item1 <
                    subsequent.Actions.First(action => action.Item2.Button == BattleButton.Liberation).Item1 &&
                    subsequent.Context.ContinueAfterLockLossUntil == 0,
                    "the next turn performs its own opening E and R1 rather than resuming the prior finisher");
            }
        }

        private static void PhrolovaOpeningHandoff()
        {
            using (var sim = new Simulation("Phrolova"))
            {
                sim.Run(liberationMs: 400);
                var commands = sim.Actions.Where(a => !a.Item2.DuringAnimation).ToArray();
                Require(commands.Length == 1 && commands[0].Item2.Button == BattleButton.Liberation &&
                    sim.Actor.LastLiberation >= commands[0].Item1 + 400,
                    "the confirmed opening R hands off without extra skills or attacks that cancel it");
            }
        }

        private static void HsinTargetProtection()
        {
            foreach (string state in new[] { "h1", "h2-before-hold", "h2", "h2-to-r2" })
            {
                var settings = CombatSettings.Defaults();
                settings.SupportOpener = settings.RotateParty = false;
                var planner = new BattlePlanner(settings);
                var hud = Snapshot();
                hud.Fighters[0] = "Hsin";
                for (int skill = 0; skill < 3; skill++) hud.Skills[skill] = SkillState.Cooling;
                planner.Observe(hud, 0);
                planner.Sent(planner.Observe(hud, 100), 100);
                hud.Indicators.Add(state == "h1" ? "hsin_h1" : "hsin_h2");
                if (state == "h2-before-hold") hud.Locked = hud.EnemyHealth = false;
                BattleAction next = planner.Observe(hud, 200);
                if (state == "h2-before-hold")
                {
                    Require(next.Kind == ActionKind.Target, "without a valid combat target, H2 cannot start protection");
                    continue;
                }
                Require(next.Kind == ActionKind.Hold, state + " starts only while combat is confirmed");
                planner.Sent(next, 200);
                hud.Locked = hud.EnemyHealth = false;
                hud.Indicators.Clear();
                if (state == "h1")
                {
                    Require(planner.Observe(hud, 250).Kind == ActionKind.Target && planner.TakeReleaseRequest(),
                        "H1 does not protect a lost target and releases its hold");
                    continue;
                }
                bool r2Sent = false;
                for (long now = 250; now < 4200; now += 50)
                {
                    if (state == "h2-to-r2" && now >= 3000 && !r2Sent) hud.Skills[2] = SkillState.Ready;
                    BattleAction action = planner.Observe(hud, now);
                    Require(action.Kind != ActionKind.Target, "accepted H2 protects the target through the handoff");
                    if (action.Kind != ActionKind.Wait) planner.Sent(action, now);
                    if (action.Kind != ActionKind.Button || action.Button != BattleButton.Liberation) continue;
                    Require(state == "h2-to-r2" && now >= 3000, "no second liberation before the delayed ready state");
                    r2Sent = true;
                    hud.Skills[2] = SkillState.Cooling;
                    planner.Observe(new HudSnapshot { TeamHudHidden = true }, now + 100);
                    planner.Observe(hud, now + 350);
                    var field = typeof(BattlePlanner).GetField("turn",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var current = (CharacterTurn)field.GetValue(planner);
                    var rosterField = typeof(BattlePlanner).GetField("roster",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var roster = (FighterMemory[])rosterField.GetValue(planner);
                    Require(roster[0].LastLiberation == now + 350,
                        "the delayed R2 must enter its animation and be recorded on return");
                    planner.Invalidate(now + 400);
                    Require(current == null || current.Context.ContinueAfterLockLossUntil == 0,
                        "stopping after R2 removes target protection");
                    break;
                }
                if (state == "h2-to-r2") Require(r2Sent, "H2 can reach a delayed R2 after the target disappears");
                else Require(planner.Observe(hud, 4200).Kind == ActionKind.Target, "H2 protection expires after four seconds");
            }
        }

        private static void YangyangSpHold()
        {
            foreach (bool intro in new[] { false, true })
            using (var sim = new Simulation("YangYangSp", intro))
            {
                sim.Hud.Skills[0] = sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Echo) hud.Skills[1] = SkillState.Cooling;
                }, (time, hud) =>
                {
                    if (time >= 1600) hud.Skills[1] = SkillState.Ready;
                });
                Require(sim.Actions.Count == 3, "polling with no E or R must send one echo and keep one continuous hold");
                var down = sim.Actions[0];
                var echo = sim.Actions[1];
                var up = sim.Actions[2];
                Require(down.Item2.Button == BattleButton.Attack && down.Item2.Kind == ActionKind.Hold &&
                    echo.Item2.Button == BattleButton.Echo && echo.Item1 >= down.Item1 + 50 &&
                    up.Item2.Button == BattleButton.Attack && up.Item2.Kind == ActionKind.Release &&
                    up.Item1 - down.Item1 >= (intro ? 8000 : 3200) && sim.FinishedAt >= up.Item1 + 100,
                    "wait between polls and settle the release before permitting a switch");
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

        private static void SuomingForteTiming()
        {
            using (var shortTurn = new Simulation("Suoming"))
            {
                shortTurn.Hud.Skills[2] = SkillState.Cooling;
                shortTurn.Run();
                Require(shortTurn.Actions.Count == 11 && shortTurn.Actions.All(a => a.Item2.Button == BattleButton.Attack) &&
                    shortTurn.FinishedAt == 2150, "without intro or liberation, use exactly 1.1 seconds of attacks");
            }
            var settings = CombatSettings.Defaults();
            settings.SuomingDps = true;
            foreach (bool noReady in new[] { false, true })
            using (var sim = new Simulation("Suoming", settings: settings))
            {
                long liberation = -1, plainE = -1, hold = -1, released = -1, echo = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button == BattleButton.Liberation) liberation = time;
                    else if (action.Button == BattleButton.Resonance && action.Kind == ActionKind.Button)
                    {
                        plainE = time;
                        hud.Skills[0] = SkillState.Cooling;
                    }
                    else if (action.Button == BattleButton.Resonance && action.Kind == ActionKind.Hold) hold = time;
                    else if (action.Button == BattleButton.Resonance && action.Kind == ActionKind.Release) released = time;
                    else if (action.Button == BattleButton.Echo)
                    {
                        echo = time;
                        hud.Skills[1] = SkillState.Cooling;
                    }
                }, (time, hud) =>
                {
                    if (plainE >= 0 && time >= plainE + 600 && hold < 0) hud.Indicators.Add("suoming_forte_e");
                    if (hold >= 0 && time >= hold + 400) hud.Indicators.Remove("suoming_forte_e");
                    if (!noReady && hold >= 0 && time >= hold + 5500) hud.Skills[0] = SkillState.Ready;
                }, liberationMs: 400);
                Require(liberation >= 0 && plainE > liberation && hold >= plainE + 600 &&
                    sim.Actions.Any(a => a.Item2.Button == BattleButton.Attack && a.Item1 > plainE && a.Item1 < hold),
                    "DPS must attack while waiting for enhanced resonance after R and ordinary E");
                long earliestRelease = hold + (noReady ? 13000 : 5500);
                Require(released >= earliestRelease && released <= earliestRelease + 50 && echo >= released,
                    "both the ready signal and the eight-second timeout must release E before echo: " +
                    string.Join(", ", sim.Actions.Where(a => a.Item2.Button == BattleButton.Resonance || a.Item2.Button == BattleButton.Echo)
                        .Select(a => a.Item1 + " " + a.Item2.Kind + " " + a.Item2.Button)));
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
                second.Hud.Skills[2] = SkillState.Cooling; // this fixture isolates the heavy branch
                second.Run();
                Require(memory.Phase == 0 && second.Actions.Any(a => a.Item2.Button == BattleButton.Jump) &&
                    !second.Actions.Any(a => a.Item2.Kind == ActionKind.Hold), "single hexagram does not authorize a hold");
            }
        }

        private static void DoulingHoldRules()
        {
            foreach (string scenario in new[] { "depletion", "one-left", "unreadable-retry", "unreadable" })
            using (var sim = new Simulation("Douling", actor: new FighterMemory { Id = "Douling", Phase = 1 }))
            {
                bool interrupted = scenario == "one-left" || scenario == "unreadable-retry";
                int holds = 0;
                long first = -1;
                sim.Hud.GuaCount = scenario == "unreadable" ? 0 : 4;
                sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Run((action, time, hud) =>
                {
                    if (action.Kind != ActionKind.Hold || action.Button != BattleButton.Attack) return;
                    if (++holds == 1) first = time;
                }, (time, hud) =>
                {
                    if (first < 0) return;
                    long elapsed = time - first;
                    if (interrupted && elapsed >= 50)
                    {
                        hud.Airborne = elapsed < 250;
                        hud.GuaCount = scenario == "one-left" ? 1 : 0;
                    }
                    if (scenario == "depletion" && elapsed >= 50)
                        hud.GuaCount = elapsed < 100 ? 1 : elapsed < 250 ? 2 : elapsed < 350 ? 0 : 2;
                });
                var holdsAndReleases = sim.Actions.Where(a => a.Item2.Button == BattleButton.Attack &&
                    (a.Item2.Kind == ActionKind.Hold || a.Item2.Kind == ActionKind.Release)).ToArray();
                int expected = scenario == "unreadable-retry" ? 2 : 1;
                Require(holds == expected && holdsAndReleases.Length == expected * 2, scenario + " hold count");
                for (int i = 0; i < holdsAndReleases.Length; i += 2)
                {
                    var down = holdsAndReleases[i];
                    var up = holdsAndReleases[i+1];
                    long elapsed = up.Item1 - down.Item1;
                    if (i == 0 && interrupted)
                        Require(elapsed == 50, scenario + " release immediately when knocked up");
                    else if (scenario == "depletion")
                        Require(elapsed >= 300 && elapsed < 350, "a single depleted read does not release the hold");
                    else
                        Require(elapsed == 2500, scenario + " keep the fixed hold when the hexagrams are unreadable, duration=" + elapsed);
                }
            }
        }

        private static void ElectroRover()
        {
            using (var sim = new Simulation("RoverElectro", true))
            {
                sim.Hud.Indicators.Add("e_forte");
                sim.Hud.Forte = .45;
                sim.Hud.Skills[0] = SkillState.Cooling;
                sim.Hud.Skills[2] = SkillState.Cooling;
                sim.Run();
                Require(!sim.Actions.Any(a => a.Item2.Button == BattleButton.Resonance), "prompt alone authorized E");
            }
            using (var sim = new Simulation("RoverElectro", true))
            {
                sim.Hud.Indicators.Add("e_forte");
                sim.Hud.Forte = 1;
                sim.Hud.ForteFullColor = true;
                sim.Hud.Skills[0] = SkillState.Cooling;
                sim.Hud.Skills[2] = SkillState.Cooling; // do not model a permanently failed R before enhanced E
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

        private static void ElectroRoverEnhanced()
        {
            MethodInfo method = typeof(Rotations).GetMethod("RoverEnhancedResonance", BindingFlags.Static | BindingFlags.NonPublic);
            Func<TurnContext, IEnumerable<Move>> enhanced = ctx => (IEnumerable<Move>)method.Invoke(null, new object[] { ctx });
            foreach (string state in new[] { "ready", "retry", "flicker", "hint-only", "missed", "no-key", "absent" })
            {
                using (var sim = new Simulation("RoverElectro", moves: enhanced))
                {
                    bool ready = state != "absent";
                    sim.Hud.ForteFullColor = ready;
                    if (ready) sim.Hud.Indicators.Add("e_forte");
                    if (state == "no-key") sim.Hud.Keys[0] = '\0';
                    sim.Hud.ConcertoFull = true;
                    sim.Hud.Skills[0] = SkillState.Cooling;
                    var presses = new List<long>();
                    sim.Run((action, time, hud) =>
                    {
                        Require(action.Button == BattleButton.Resonance && action.DownMilliseconds == 50,
                            "enhanced resonance sends only a 50 ms E press");
                        presses.Add(time);
                    }, (time, hud) =>
                    {
                        if (presses.Count == 0) return;
                        long elapsed = time - presses.Last();
                        bool clear = (state == "ready" || state == "hint-only" || state == "retry" && presses.Count == 2) &&
                            elapsed >= 2100 || state == "flicker" && elapsed >= 1600 && elapsed < 1700;
                        hud.ForteFullColor = !clear || state == "hint-only";
                        if (clear) hud.Indicators.Remove("e_forte"); else hud.Indicators.Add("e_forte");
                    });
                    bool confirmed = state == "ready" || state == "retry";
                    int expected = state == "no-key" || state == "absent" ? 0 : state == "ready" || state == "hint-only" ? 1 : 2;
                    Require(presses.Count == expected, state + " enhanced press count");
                    Require(confirmed ? sim.Actor.LastSkill[0] - presses.Last() == 2300 : sim.Actor.LastSkill[0] == -60000,
                        state + " only a stable, simultaneous disappearance records resonance");
                    if (presses.Count == 2)
                        Require(presses[1] - presses[0] == 2600 && sim.FinishedAt - presses[0] < 5500,
                            state + " uses one retry within the two-attempt limit");
                }
            }
        }

        private static void ElectroRoverOpening()
        {
            var disabled = CombatSettings.Defaults();
            disabled.UseLiberation = false;
            foreach (bool intro in new[] { false, true })
            {
                using (var sim = new Simulation("RoverElectro", intro, settings: disabled))
                {
                    sim.Hud.LevitatorKnown = true;
                    sim.Hud.Skills[0] = sim.Hud.Skills[1] = SkillState.Cooling;
                    sim.Run();
                    int limit = intro ? 8000 : 12000;
                    Require(sim.Actions.Count == limit / 100 && sim.Actions.All(a => a.Item2.Button == BattleButton.Attack) &&
                        sim.FinishedAt == 1000 + limit + 50, "bounded forte fill with no other available skill");
                }
            }
            foreach (string state in new[] { "both", "fallback", "incomplete-r", "missed-e", "dim", "no-energy", "cooldown", "disabled" })
            {
                using (var sim = new Simulation("RoverElectro", settings: state == "disabled" ? disabled : null))
                {
                    sim.Hud.ForteFullColor = true;
                    sim.Hud.Indicators.Add("e_forte");
                    sim.Hud.Skills[0] = SkillState.Cooling;
                    if (state == "dim" || state == "no-energy") sim.Hud.Skills[2] = SkillState.Unavailable;
                    if (state == "cooldown") sim.Hud.Skills[2] = SkillState.Cooling;
                    long firstE = -1, hiddenUntil = -1;
                    int liberation = 0;
                    sim.Run((action, time, hud) =>
                    {
                        if (action.Button == BattleButton.Echo)
                            hud.Skills[1] = SkillState.Cooling;
                        if (action.Button == BattleButton.Liberation)
                        {
                            liberation++;
                            if (state != "no-energy" && (state == "dim" || liberation == 1 || state == "both"))
                            {
                                hiddenUntil = time + 850;
                                hud.Skills[2] = SkillState.Cooling;
                                if (state == "both" && liberation == 2)
                                    hud.ConcertoFull = true;
                            }
                            else if (state == "incomplete-r")
                                hud.Skills[2] = SkillState.Unavailable;
                        }
                        if (action.Button == BattleButton.Resonance)
                            firstE = time;
                    }, (time, hud) =>
                    {
                        hud.Valid = time >= hiddenUntil;
                        if (firstE < 0 || time - firstE < 2100 || state == "missed-e") return;
                        hud.ForteFullColor = false;
                        hud.Indicators.Remove("e_forte");
                        if (state == "both" || state == "incomplete-r" && liberation < 2)
                            hud.Skills[2] = liberation < 2 ? SkillState.Ready : SkillState.Cooling;
                        if (state == "dim" || state == "no-energy" || state == "cooldown" || state == "disabled")
                            hud.ConcertoFull = true;
                    });
                    BattleButton[] buttons = sim.Actions.Select(a => a.Item2.Button).ToArray();
                    Require(buttons.First() == BattleButton.Echo &&
                        buttons.Count(button => button == BattleButton.Echo) == 1 &&
                        !buttons.Contains(BattleButton.Interact), state + " keeps the standard echo and suppresses animation F");
                    int expectedR = state == "both" || state == "incomplete-r" ? 2 :
                        state == "disabled" || state == "cooldown" ? 0 : 1;
                    Require(liberation == expectedR && firstE > 0, state + " liberation order");
                    if (liberation > 0)
                        Require(buttons[1] == BattleButton.Liberation, "opening R comes before enhanced E");
                    if (state == "no-energy")
                        Require(sim.Actor.LastLiberation == -1 && firstE - sim.Actions[1].Item1 <= 200,
                            "failed dim R does not stall enhanced E or record a liberation");
                    int attacks = buttons.Count(button => button == BattleButton.Attack);
                    Require(attacks == (state == "fallback" || state == "incomplete-r" ? 20 : 0),
                        state + " only top up with two seconds of attacks after confirmed E and incomplete concerto");
                    if (state == "missed-e")
                        Require(sim.Actor.LastSkill[0] == -60000 && buttons.Count(b => b == BattleButton.Resonance) == 2,
                            "unconfirmed E must skip the follow-up");
                }
            }
        }

        private static void RoverForms()
        {
            string[] forms = { "RoverElectro", "RoverSpectro", "RoverAero", "RoverHavoc", null, "Rover" };
            var planner = new BattlePlanner(CombatSettings.Defaults());
            var roster = (FighterMemory[])typeof(BattlePlanner).GetField("roster", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(planner);
            MethodInfo role = typeof(BattlePlanner).GetMethod("RoleAt", BindingFlags.Instance | BindingFlags.NonPublic);
            var hud = Snapshot();
            roster[0].Id = "Rover";
            Require(CharacterRoster.Find("RoverElectro").Name == "漂泊者·导电", "the Electro form has its own display name");
            Require((BattleRole)role.Invoke(planner, new object[] { hud, 0 }) == BattleRole.Secondary,
                "unrecognized Rover is scheduled as sub DPS");
            foreach (string form in forms.Take(4))
            {
                roster[0].RoverForm = form;
                for (int repeat = 0; repeat < 2; repeat++) roster[0].ResetForNextBattle();
                Require(roster[0].RoverForm == null && roster[0].RoverLastKnownForm == form &&
                    (BattleRole)role.Invoke(planner, new object[] { hud, 0 }) ==
                        (form == "RoverElectro" ? BattleRole.Secondary : BattleRole.Main),
                    "the last known role is retained while the active form is cleared");
            }
            var settings = CombatSettings.Defaults();
            settings.UseLiberation = false;
            foreach (string form in forms)
            {
                using (var sim = new Simulation("Rover", settings: settings))
                {
                    sim.Hud.RoverForm = form;
                    sim.Hud.LevitatorKnown = true;
                    sim.Hud.ConcertoFull = true;
                    sim.Hud.ForteFullColor = form == "RoverElectro";
                    if (sim.Hud.ForteFullColor) sim.Hud.Indicators.Add("e_forte");
                    long e = -1;
                    sim.Run((action, time, frame) =>
                    {
                        if (action.Button != BattleButton.Resonance) return;
                        if (e < 0) e = time;
                        frame.Skills[0] = SkillState.Cooling;
                    }, (time, frame) =>
                    {
                        if (form == "RoverElectro" && e >= 0 && time - e >= 2100)
                        {
                            frame.ForteFullColor = false;
                            frame.Indicators.Remove("e_forte");
                        }
                    });
                    BattleAction first = sim.Actions[0].Item2;
                    if (form == "RoverSpectro" || form == null)
                        Require(first.Kind == ActionKind.Hold && sim.Actor.RoverForm == "RoverSpectro",
                            "Spectro and a missing ring color both use the Spectro aftertune heavy");
                    else if (form == "RoverHavoc") Require(first.Button == BattleButton.Resonance &&
                        !sim.Actions.Any(a => a.Item2.Button == BattleButton.Echo), "Havoc stops after a successful E");
                    else Require(first.Button == BattleButton.Echo, "other forms open using the echo helper");
                }
            }
            foreach (bool hasBoth in new[] { false, true })
            {
                using (var sim = new Simulation("Rover", settings: settings))
                {
                    sim.Hud.RoverForm = "RoverAero";
                    sim.Hud.LevitatorKnown = true;
                    sim.Team[1].Id = "Cartethyia";
                    sim.Team[2].Id = hasBoth ? "Phoebe" : "Sanhua";
                    long launched = -1;
                    int resonance = 0;
                    sim.Run((action, time, frame) =>
                    {
                        if (action.Button != BattleButton.Resonance) return;
                        if (resonance++ == 0) launched = time;
                        else frame.Skills[0] = SkillState.Cooling;
                    }, (time, frame) => frame.Airborne = launched >= 0 && time < launched + 1900);
                    Require(resonance == (hasBoth ? 2 : 1), "Skyfall Severance needs both team members");
                }
            }
        }

        private static void RoverFormLifetime()
        {
            var memory = new FighterMemory { Id = "Rover", RoverForm = "RoverSpectro" };
            var settings = CombatSettings.Defaults();
            settings.UseLiberation = false;
            foreach (bool reset in new[] { false, true })
            {
                if (reset) memory.ResetForNextBattle();
                using (var sim = new Simulation("Rover", actor: memory, settings: settings))
                {
                    sim.Hud.RoverForm = "RoverHavoc";
                    sim.Hud.LevitatorKnown = true;
                    sim.Run((action, time, h) =>
                    {
                        int skill = TurnContext.SkillIndex(action.Button);
                        if (skill >= 0) h.Skills[skill] = SkillState.Cooling;
                    });
                    Require(memory.RoverForm == (reset ? "RoverHavoc" : "RoverSpectro"),
                        "only a new battle can change a resolved Rover form");
                }
            }
        }

        private static void AemeathRotation()
        {
            using (var missing = new Simulation("Aemeath"))
            {
                missing.Hud.Indicators.Add("aemeath_lib2");
                missing.Run();
                Require(missing.Actions.Count == 0, "skip the full turn when a sub DPS is present and intro is missing");
            }
            using (var heavy = new Simulation("Aemeath"))
            {
                heavy.Hud.LongActionLayout = 1;
                long held = -1;
                heavy.Run((action, time, frame) => { if (action.Kind == ActionKind.Hold) held = time; },
                    (time, frame) => { if (held >= 0 && time >= held + 350) frame.LongActionLayout = 0; });
                Require(heavy.Actions.Count == 2 && heavy.Actions[0].Item2.Kind == ActionKind.Hold &&
                    heavy.Actions[1].Item2.Kind == ActionKind.Release && heavy.Actions[1].Item1 - held == 350,
                    "perform only the highlighted heavy and release as soon as the highlight disappears");
            }

            foreach (string state in new[] { "direct", "two-healers", "enhanced", "first-liberation" })
            {
                using (var sim = new Simulation("Aemeath", state != "two-healers"))
                {
                    if (state == "two-healers") sim.Team[2].Id = "ShoreKeeper";
                    else foreach (FighterMemory ally in sim.Team.Skip(1))
                    {
                        ally.BuffStarted = 0;
                        ally.BuffDuration = 14000;
                    }
                    sim.Hud.Skills[2] = state == "enhanced" ? SkillState.Unavailable : SkillState.Ready;
                    if (state == "direct" || state == "two-healers") sim.Hud.Indicators.Add("aemeath_lib2");
                    sim.Hud.Indicators.Add("aemeath_e1");
                    int releases = 0;
                    long hidden = -1, enhancedAt = -1;
                    sim.Run((action, time, frame) =>
                    {
                        if (action.Button == BattleButton.Liberation && !action.DuringAnimation)
                        {
                            releases++;
                            hidden = time + 900;
                            frame.Skills[2] = SkillState.Cooling;
                        }
                        else if (action.Button == BattleButton.Resonance && !action.DuringAnimation)
                        {
                            enhancedAt = time;
                            hidden = time + 800;
                            frame.Skills[0] = SkillState.Cooling;
                            frame.Indicators.Remove("aemeath_e1");
                        }
                    }, (time, frame) =>
                    {
                        frame.Valid = time >= hidden;
                        if (enhancedAt >= 0 && time >= enhancedAt + 800 && releases == (state == "first-liberation" ? 1 : 0))
                        {
                            frame.Skills[2] = SkillState.Ready;
                            frame.Indicators.Add("aemeath_lib2");
                        }
                    });
                    var actions = sim.Actions.Where(a => !a.Item2.DuringAnimation &&
                        (a.Item2.Button == BattleButton.Liberation || a.Item2.Button == BattleButton.Resonance ||
                         a.Item2.Button == BattleButton.Echo)).Select(a => a.Item2.Button).ToArray();
                    var expected = new List<BattleButton>();
                    if (state == "first-liberation") expected.Add(BattleButton.Liberation);
                    if (state == "enhanced" || state == "first-liberation")
                    {
                        expected.Add(BattleButton.Resonance);
                        expected.Add(BattleButton.Echo);
                    }
                    expected.Add(BattleButton.Liberation);
                    Require(actions.SequenceEqual(expected), state + " expected skill order: " + string.Join(",", actions));
                    var last = sim.Actions.Last(a => a.Item2.Button == BattleButton.Liberation && !a.Item2.DuringAnimation);
                    Require(sim.FinishedAt == last.Item1 + 950, "end the turn as soon as liberation two finishes");
                    if (state == "direct")
                    {
                        Require(sim.Actions[0].Item2.Button == BattleButton.Attack &&
                            last.Item1 - sim.Actions[0].Item1 == 2100, "full-buff intro uses the complete attack opener");
                    }
                }
            }
        }

        private static void ChisaTimedOutReset()
        {
            var options = CombatSettings.Defaults();
            options.ChisaDps = true;
            using (var sim = new Simulation("Chisa", settings: options))
            {
                sim.Hud.Skills[2] = SkillState.Cooling;
                long first = -1;
                sim.Run((action, time, h) =>
                {
                    if (action.Button == BattleButton.Echo) h.Skills[1] = SkillState.Cooling;
                    if (action.Button == BattleButton.Resonance && first < 0) first = time;
                }, (time, h) => { if (first >= 0 && time > first + 15000) h.Skills[0] = SkillState.Cooling; });
                Require(first >= 0 && sim.FinishedAt >= first + 16500,
                    "a sent E still resets the phase on its first timeout, as in click_resonance()[0]");
            }
        }

        private static void ChisaSupportRetry()
        {
            using (var sim = new Simulation("Chisa"))
            {
                sim.Hud.Skills[2] = SkillState.Cooling;
                int resonance = 0;
                sim.Run((action, time, h) =>
                {
                    if (action.Button == BattleButton.Echo) h.Skills[1] = SkillState.Cooling;
                    if (action.Button == BattleButton.Resonance && ++resonance == 2) h.Skills[0] = SkillState.Cooling;
                });
                var trace = sim.Actions.Select(x => x.Item2.Button).ToArray();
                int first = Array.IndexOf(trace, BattleButton.Resonance);
                Require(resonance == 2 && first >= 0 && trace[first + 1] == BattleButton.Attack &&
                    trace[first + 2] == BattleButton.Resonance, "finish the helper retry before the short turn resumes");
            }
        }

        private static void DeniaSecondLiberationLifetime()
        {
            var memory = new FighterMemory { Id = "Denia" };
            for (int phase = 0; phase < 2; phase++)
            {
                using (var sim = new Simulation("Denia", actor: memory, start: 1000 + 6000 * phase))
                {
                    sim.Hud.Skills[0] = SkillState.Cooling;
                    if (phase == 1) sim.Hud.Indicators.Add("denia_end_lib");
                    sim.Run(liberationMs: 400);
                    Require(memory.Phase == (phase == 0 ? 1 : 0) &&
                        (memory.DeniaSecondLiberation >= 0) == (phase == 1), "only R2 updates its timestamp");
                }
            }
            using (var sim = new Simulation("Denia", actor: memory, start: 14000))
            {
                sim.Hud.ConcertoFull = true;
                sim.Run();
                Require(sim.Actions.Count == 0, "same battle with recent R2 and full concerto exits");
            }
            memory.ResetForNextBattle();
            using (var sim = new Simulation("Denia", actor: memory, start: 14500))
            {
                sim.Hud.ConcertoFull = true;
                sim.Run((action, time, h) =>
                {
                    if (action.Button == BattleButton.Resonance) h.Skills[0] = SkillState.Cooling;
                }, liberationMs: 400);
                Require(sim.Actions.Any(a => a.Item2.Button == BattleButton.Resonance),
                    "new encounter cannot inherit the previous R2 switch condition");
            }
        }

        private static void ChisaTurns()
        {
            foreach (string state in new[] { "long", "no-intro", "buffed", "full", "dps" })
            {
                var settings = CombatSettings.Defaults();
                settings.ChisaDps = state == "dps";
                using (var sim = new Simulation("Chisa", state != "no-intro", settings: settings))
                {
                    if (state == "buffed")
                    {
                        sim.Actor.BuffStarted = 0;
                        sim.Actor.BuffDuration = 20000;
                    }
                    if (state == "dps")
                    {
                        sim.Hud.Skills[0] = sim.Hud.Skills[2] = SkillState.Cooling;
                    }
                    long echo = -1, hidden = -1;
                    sim.Run((action, time, frame) =>
                    {
                        if (action.Button == BattleButton.Echo)
                        {
                            echo = time;
                            frame.Skills[1] = SkillState.Cooling;
                        }
                        if (action.Button == BattleButton.Liberation && !action.DuringAnimation)
                        {
                            hidden = time + 800;
                            frame.Skills[2] = SkillState.Cooling;
                        }
                        if (action.Button == BattleButton.Resonance)
                            frame.Skills[0] = SkillState.Cooling;
                    }, (time, frame) =>
                    {
                        frame.Valid = time >= hidden;
                        if (state == "full" && echo >= 0 && time >= echo + 250)
                            frame.ConcertoFull = true;
                    });
                    Require(echo - 1000 == (state == "dps" ? 800 : state == "no-intro" ? 0 : 2000),
                        state + " prep before the normal echo helper");
                    if (state == "dps")
                    {
                        Require(CharacterRoster.Find("Chisa").RoleFor(settings) == BattleRole.Main &&
                            sim.Actor.ExecuteWhenSwitching == true, "the DPS setting keeps Chisa's DPS program");
                        continue;
                    }
                    Require(sim.Actor.BuffStarted == (state == "buffed" ? 0 : -1) &&
                        sim.Actor.ExecuteWhenSwitching == false, "casting a liberation does not record a support outro");
                    Require(sim.Actions.Count(a => a.Item2.Button == BattleButton.Liberation) == 1,
                        state + " tries the available opening liberation");
                    if (state == "full")
                    {
                        Require(sim.FinishedAt - echo < 1500 && !sim.Actions.Any(a => a.Item2.Button == BattleButton.Resonance),
                            "full concerto stops the support turn before another action");
                        continue;
                    }
                    Require(sim.Actions.Count(a => a.Item2.Button == BattleButton.Resonance) == 1,
                        state + " uses the immediate resonance attempt exactly once");
                    long duration = sim.FinishedAt - echo;
                    Require(state == "long" ? duration >= 10000 && duration < 11500 : duration >= 1200 && duration < 2450,
                        state + " support action budget");
                }
            }
        }

        private static void VerinaHeavyInterval()
        {
            const long start = 20000;
            foreach (int gap in new[] { 7999, 8000 })
            {
                var healer = new FighterMemory { Id = "Verina", LastHeavy = start + 1400 - gap };
                using (var sim = new Simulation("Verina", actor: healer, start: start))
                {
                    for (int skill = 0; skill < 3; skill++)
                        sim.Hud.Skills[skill] = SkillState.Cooling;
                    sim.Hud.Indicators.Add("mouse_forte");
                    sim.Run();
                    Require(sim.Actions.Count(a => a.Item2.Kind == ActionKind.Hold) == (gap == 8000 ? 1 : 0),
                        "Verina only repeats heavy when eight seconds have elapsed");
                }
            }
        }

        private static void CalcharoIntroHudRecovery()
        {
            using (var sim = new Simulation("Calcharo", intro: true))
            {
                sim.Run(frame: (time, hud) => hud.Valid = time < 1950 || time >= 2400);
                long echo = sim.Actions.First(action => action.Item2.Button == BattleButton.Echo).Item1;
                Require(echo >= 2900, "wait for the returning team HUD and its 500 ms settle before casting");
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

        private static void RocciaChargeBranches()
        {
            var settings = CombatSettings.Defaults();
            var kit = CharacterRoster.Find("Roccia");
            Require(kit.RoleFor(settings) == BattleRole.Secondary, "default Roccia role");
            settings.RocciaC6 = true;
            Require(kit.RoleFor(settings) == BattleRole.Main, "configured C6 Roccia role");
            foreach (string scenario in new[] { "spare", "last", "ground", "air-no-generic" })
            using (var sim = new Simulation("Roccia", moves: Rotations.RocciaAir))
            {
                sim.Hud.AirCharges = scenario == "ground" ? 0 : scenario == "last" ? 1 : 2;
                if (scenario != "air-no-generic") sim.Hud.Indicators.Add("mouse_forte");
                var durations = new List<long>();
                long down = -1;
                sim.Run((action, time, hud) =>
                {
                    if (action.Button != BattleButton.Attack) return;
                    if (action.Kind == ActionKind.Hold) down = time;
                    if (action.Kind != ActionKind.Release) return;
                    durations.Add(time - down);
                    hud.AirCharges = scenario == "air-no-generic" ? hud.AirCharges - 1 : 0;
                    if (scenario == "ground" || durations.Count == 2) hud.Indicators.Remove("mouse_forte");
                });
                long[] expected = scenario == "ground" ? new long[]{1600} : scenario == "last"
                    ? new long[]{400,1600} : new long[]{400,400};
                Require(durations.SequenceEqual(expected), scenario + " force holds " + string.Join(",", durations));
                Require(sim.Actions.Last().Item2.Kind == ActionKind.Release &&
                    sim.Actions.Last().Item2.Button == BattleButton.Forward, "release forward before leaving aerial rotation");
            }
        }

        private static void RocciaFreshPlunge()
        {
            var actor = new FighterMemory { Id = "Roccia" };
            using (var first = new Simulation("Roccia", actor: actor, moves: Rotations.RocciaAir))
            {
                first.Hud.AirCharges = 1;
                first.Run(frame: (time, hud) => { if (time >= 1400) hud.AirCharges = 0; });
                Require(first.Actions.Count(a => a.Item2.Kind == ActionKind.Hold && a.Item2.Button == BattleButton.Attack) == 1,
                    "the first plunge consumes its single aerial charge");
            }
            using (var next = new Simulation("Roccia", actor: actor, start: 4000, moves: Rotations.RocciaAir))
            {
                next.Run();
                Require(next.Actions.All(a => a.Item2.Button != BattleButton.Attack),
                    "the next plunge requires fresh visual evidence before attacking");
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
                var kit = CharacterRoster.Find("Hiyuki");
                Require(kit.Role == BattleRole.Main && CharacterKit.Ring("Hiyuki") == ConcertoColor.Glacio,
                    "alternate portraits must keep Hiyuki's base role and element");
                var cached = new CombatVision(CombatSettings.Defaults());
                using (var reader = new FrameReader())
                foreach (string name in new[] { "hiyuki1.png", "hiyuki-skin.png", "hiyuki-skin-inactive.png" })
                using (var image = new Bitmap(Path.Combine(reference, "ok_templates", name)))
                {
                    var hud = Snapshot();
                    cached.ReadParty(reader.Read(image), hud);
                    Require(hud.Fighters[0] == "Hiyuki", "cached portrait must survive the alternate skin " + name);
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
                    var kit = CharacterRoster.Find("Suoming");
                    var options = CombatSettings.Defaults();
                    Require(kit.RoleFor(options) == BattleRole.Secondary && CharacterKit.Ring("Suoming") == ConcertoColor.Electro &&
                        kit.BuffDuration(options) == 8000, "Suoming's registered support role and element");
                    options.SuomingDps = true;
                    Require(kit.RoleFor(options) == BattleRole.Main && kit.BuffDuration(options) == 14000,
                        "DPS preference changes both role and buff window");
                    HudSnapshot hud = new HudVision(CombatSettings.Defaults()).Read(reader.Read(combat));
                    Require(hud.Valid && hud.Locked && hud.Fighters[0] == "Iuno" && hud.Fighters[1] == "Roccia",
                        "normal capture failed mapping recognized portraits to slots");
                }
            });
            check("reference character changes, Aemeath readiness and Luhesi cooldown", delegate
            {
                var readerVision = new HudVision(CombatSettings.Defaults());
                using (var reader = new FrameReader())
                {
                    Func<string,HudSnapshot> capture = name =>
                    {
                        using (var image = new Bitmap(Path.Combine(reference, name)))
                            return readerVision.Read(reader.Read(image));
                    };
                    HudSnapshot aemeath = capture("tests/images/aemeath_lib.png");
                    Require(aemeath.Valid && aemeath.Locked && aemeath.Skills[2] == SkillState.Ready,
                        "Aemeath's reference liberation is ready in a recognized fight");
                    Require(capture("tests/images/combat_has_cd.png").Fighters[0] == "Aemeath" &&
                        capture("ok_templates/char_iuno.png").Fighters[0] == "Iuno",
                        "a fresh portrait must replace the cached Aemeath slot with Iuno");
                    HudSnapshot luhesi = capture("tests/images/luhesi_lib_in_cd.png");
                    Require(luhesi.Valid && luhesi.Fighters[0] == "Luhesi" && luhesi.Skills[2] == SkillState.Cooling,
                        "Luhesi's reference liberation must be read as cooling");
                }
            });
            check("Hsin's highlighted enhanced prompt is read without treating a plain background as readiness",
                () => HsinPromptFrames(reference));
            check("Suisui's central and right-aligned indicators follow four reference sizes",
                () => SuisuiPromptFrames(reference));
            check("all enabled portrait templates resolve on the C# registry and Lingyang is recognized",
                () => PortraitTemplates(reference));
            check("portrait cache retains uncertain portraits only within a continuous party",
                () => PortraitCacheContinuity(reference));
        }

        private static void HsinPromptFrames(string reference)
        {
            using (var reader = new FrameReader())
            using (var source = new Bitmap(Path.Combine(reference, "ok_templates", "xigelika.png")))
            using (var plain = new Bitmap(1920, 1080))
            {
                Require(ReadMarkers(reader.Read(source), "Hsin").Shows("e_forte"),
                    "the real enhanced E prompt must be recognized");
                foreach (Color color in new[] { Color.Black, Color.FromArgb(220, 190, 80) })
                {
                    using (Graphics graphics = Graphics.FromImage(plain)) graphics.Clear(color);
                    Require(!ReadMarkers(reader.Read(plain), "Hsin").Shows("e_forte"),
                        "plain gold and black are not enhanced E prompts");
                }
            }
        }

        private static void SuisuiPromptFrames(string reference)
        {
            using (var reader = new FrameReader())
            foreach (string file in new[] { "23.png", "39.png" })
            using (var source = new Bitmap(Path.Combine(reference, "ok_templates", file)))
            foreach (Size size in new[] { new Size(1920, 1080), new Size(2560, 1080), new Size(3440, 1440), new Size(5120, 2160) })
            {
                int width = (int)Math.Round(source.Width * size.Height / (double)source.Height);
                using (var scaled = new Bitmap(source, width, size.Height))
                using (var frame = new Bitmap(size.Width, size.Height))
                {
                    var expected = file == "23.png" ? new[] { "suisui_forte2", "suisui_forte3" } : new[] { "suisui_e1" };
                    using (Graphics graphics = Graphics.FromImage(frame))
                    {
                        graphics.Clear(Color.Black);
                        graphics.DrawImageUnscaled(scaled, (size.Width - width) / 2, 0);
                        int right = width * 2 / 3;
                        var crop = new Rectangle(right, 0, width - right, size.Height);
                        graphics.DrawImage(scaled, new Rectangle(size.Width - width + right, 0, crop.Width, crop.Height),
                            crop, GraphicsUnit.Pixel);
                    }
                    HudSnapshot current = ReadMarkers(reader.Read(frame), "Suisui");
                    Require(expected.All(current.Shows), "Suisui's signals must move with their anchor " + file + " " + size);
                    using (Graphics graphics = Graphics.FromImage(frame)) graphics.Clear(Color.Black);
                    Require(expected.All(mark => !ReadMarkers(reader.Read(frame), "Suisui").Shows(mark)),
                        "black areas must not authorize Suisui's skills");
                }
            }
        }

        private static void PortraitTemplates(string reference)
        {
            Require(PatternBank.Portraits.All(pattern => CharacterRoster.Find(pattern.Fighter) != null),
                "each loaded portrait must map to a registered fighter");
            foreach (CharacterKit kit in CharacterRoster.All.Where(kit => kit.Id == "Rover" ||
                !kit.Id.StartsWith("Rover", StringComparison.Ordinal)))
                Require(PatternBank.Portraits.Any(pattern => pattern.Fighter == kit.Id), "missing portrait for " + kit.Id);
            using (var reader = new FrameReader())
            using (var source = new Bitmap(Path.Combine(reference, "ok_templates", "lingyang.png")))
            {
                PixelFrame frame = reader.Read(source);
                var vision = new CombatVision(CombatSettings.Defaults());
                VisualPattern[] cached = PatternBank.Portraits;
                Require(Enumerable.Range(0, 3).Count(slot => vision.Identify(frame, slot) == "Lingyang") == 1,
                    "recognize Lingyang's restored source portrait in exactly one slot");
                var second = new CombatVision(CombatSettings.Defaults());
                Require(Enumerable.Range(0, 3).Count(slot => second.Identify(frame, slot) == "Lingyang") == 1 &&
                    ReferenceEquals(cached, PatternBank.Portraits),
                    "independent battle vision sessions share the loaded character templates");
            }
        }

        private static void OpenWorldCombatPolicy()
        {
            foreach (bool? policy in new bool?[] { null, true, false })
            foreach (bool inRealm in new[] { false, true })
            {
                bool restricted = policy ?? !inRealm;
                foreach (bool instant in new[] { false, true })
                using (var echo = new Simulation("Encore", moves: c => new[] { Move.Echo(instant ? 0 : 1000) },
                    openWorldAutoCombat: policy))
                {
                    echo.Hud.InRealm = inRealm;
                    echo.Run((action, time, hud) => hud.Skills[1] = SkillState.Cooling);
                    Require(echo.Actions.Any(x => x.Item2.Button == BattleButton.Echo) == (instant || !restricted),
                        "instant-ready echo bypasses the restriction while timed fire echo honors task policy");
                }

                using (var encore = new Simulation("Encore", openWorldAutoCombat: policy))
                {
                    encore.Hud.InRealm = inRealm;
                    encore.Hud.Skills[0] = encore.Hud.Skills[1] = SkillState.Cooling;
                    encore.Run(liberationMs: 400);
                    Require(encore.Actions.Any(x => x.Item2.Button == BattleButton.Liberation) == !restricted,
                        "Encore's liberation branch follows task policy and falls back to the physical HUD when unassigned");
                }
            }
        }

        private static void TaskLiberationPreference()
        {
            var settings = CombatSettings.Defaults();
            settings.UseLiberation = settings.SupportOpener = false;
            var account = AccountConfig.ForRun(settings);
            var accountConfig = new Dictionary<string, object>
            {
                { "Combat", new Dictionary<string, object> { { "UseLiberation", false } } }
            };
            Require(AccountConfig.Apply(account, accountConfig) == 1 && account.HasAccountLiberationOverride,
                "the explicit account setting is distinguished from the standalone preference");

            foreach (bool inWorld in new[] { true, false })
            {
                Require(StartsWithLiberation(CombatRunner.CreateTaskPlanner(settings, cloud: false), inWorld),
                    "ordinary one-time tasks use liberation regardless of the standalone world toggle");
                Require(StartsWithLiberation(CombatRunner.CreateTaskPlanner(account, cloud: false), inWorld) == !inWorld,
                    "an explicit account override preserves the prior world-only restriction");
                Require(StartsWithLiberation(BattleLoop.CreateStandalonePlanner(settings, cloud: false), inWorld) == !inWorld,
                    "standalone combat continues honoring its world-only preference");

                var farmOptions = FarmOptions.Defaults();
                farmOptions.UseLiberation = farmOptions.SwitchHealer = false;
                var farm = new Farm4C(settings, farmOptions);
                Require(!StartsWithLiberation(farm.Planner, inWorld),
                    "the dedicated farming toggle can disable liberation both in world and in realm");
            }
        }

        private static bool StartsWithLiberation(BattlePlanner planner, bool inWorld)
        {
            HudSnapshot hud = Snapshot();
            hud.OpenWorld = inWorld;
            hud.InRealm = !inWorld;
            hud.Fighters[0] = "Hsin";
            hud.Skills[0] = hud.Skills[1] = SkillState.Cooling;
            hud.Skills[2] = SkillState.Ready;
            hud.Indicators.Add("hsin_lib1");
            for (int time = 0; time <= 600; time += 100)
            {
                BattleAction action = planner.Observe(hud, time);
                if (action.Kind == ActionKind.Button && action.Button == BattleButton.Liberation) return true;
                if (action.Kind != ActionKind.Wait) planner.Sent(action, time);
            }
            return false;
        }

        private static void PortraitCacheContinuity(string reference)
        {
            using (var reader = new FrameReader())
            using (var hiyuki = new Bitmap(Path.Combine(reference, "ok_templates", "hiyuki1.png")))
            using (var lingyang = new Bitmap(Path.Combine(reference, "ok_templates", "lingyang.png")))
            {
                var settings = CombatSettings.Defaults();
                var vision = new CombatVision(settings);
                PixelFrame first = reader.Read(hiyuki);
                var blank = new PixelFrame(new byte[first.Width * first.Height * 4], first.Width, first.Height);
                PixelFrame third = CopyPortraitToSlot(first, 0, 2);
                Require(ReadPortraitParty(vision, first, 3).Fighters[0] == "Hiyuki",
                    "establish the initial portrait");
                Require(vision.Identify(blank, 0) == null &&
                    ReadPortraitParty(vision, blank, 3).Fighters[0] == "Hiyuki",
                    "an inconclusive observation retains the last confirmed identity");

                PixelFrame other = reader.Read(lingyang);
                int otherSlot = Enumerable.Range(0, 3).Single(slot => vision.Identify(other, slot) == "Lingyang");
                Require(ReadPortraitParty(vision, CopyPortraitToSlot(other, otherSlot, 0), 3).Fighters[0] == "Lingyang",
                    "a confidently changed portrait replaces the cached identity");

                Require(ReadPortraitParty(vision, third, 3).Fighters[2] == "Hiyuki",
                    "establish the third portrait");
                Require(ReadPortraitParty(vision, blank, 2).Fighters[2] == null &&
                    ReadPortraitParty(vision, blank, 3).Fighters[2] == null,
                    "an absent slot clears its identity even when its portrait is obscured on return");

                vision.ResetParty();
                Require(ReadPortraitParty(vision, blank, 3).Fighters[0] == null,
                    "an explicitly reset party cannot regain a previous identity from a blank portrait");
                settings.PartyCharacters[0] = "Verina";
                Require(ReadPortraitParty(vision, blank, 3).Fighters[0] == "Verina",
                    "manual assignment works while the portrait is obscured");
                settings.PartyCharacters[0] = null;
                Require(ReadPortraitParty(vision, blank, 3).Fighters[0] == null,
                    "clearing a manual assignment does not create a cached identity");
            }
        }

        private static HudSnapshot ReadPortraitParty(CombatVision vision, PixelFrame frame, int partySize)
        {
            HudSnapshot hud = Snapshot();
            hud.PartySize = partySize;
            vision.ReadParty(frame, hud);
            return hud;
        }

        private static PixelFrame CopyPortraitToSlot(PixelFrame frame, int sourceSlot, int destinationSlot)
        {
            const int width = 120, height = 80;
            int left = frame.Width - 160;
            var portrait = new byte[width * height * 4];
            frame.CopyBgra(new Rectangle(left, 165 + sourceSlot * 110, width, height), portrait);
            var pixels = new byte[frame.Width * frame.Height * 4];
            int top = 165 + destinationSlot * 110;
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(portrait, row * width * 4, pixels, ((top + row) * frame.Width + left) * 4, width * 4);
            return new PixelFrame(pixels, frame.Width, frame.Height);
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
