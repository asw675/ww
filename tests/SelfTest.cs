using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class SelfTest
    {
        private static int failed;
        private static readonly List<string> skip = new List<string>();

        [STAThread]
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            foreach (string argument in args)
                if (argument.StartsWith("--skip=", StringComparison.Ordinal))
                    skip.Add(argument.Substring(7));
            if (args.Length == 1 && args[0] == "--local-window-fixture")
                return LocalTaskWindowChecks.RunFixture();
            if (args.Length == 2 && args[0] == "--render")
            {
                RenderWindows(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--browser-fixture")
            {
                Check("headless browser captures an offline page and routes input events", () => BrowserFixture(args[1]));
                return failed == 0 ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "--browser-stream-fixture")
            {
                Check("headless browser reads current video tracks and falls back for interactive overlays",
                    () => BrowserFixture(args[1], VerifyBrowserVideo));
                return failed == 0 ? 0 : 1;
            }
            if (args.Length == 3 && args[0] == "--browser-protocol")
            {
                using (var browser = new CloudBrowser(args[1], address => address == args[2]))
                    Check("loopback browser captures an offline frame and routes input", () => VerifyBrowserEvents(browser, () => true));
                return failed == 0 ? 0 : 1;
            }
            Check("two-frame lock confirmation and stop on HUD loss", LockConfirmation);
            Check("auto-target is gated by repeated enemy evidence", AutoTarget);
            Check("standalone PC combat requires a lock before attacking", StandaloneRequiresLock);
            Check("standalone combat clears a dead character turn before resuming", StandaloneStopsOnDeath);
            Check("task combat requires a PC lock and accepts cloud enemy health", TaskCombatEntry);
            Check("blind search does not replace evidence-based targeting or advance combat capture", TaskBlindSearchAndCapture);
            Check("combat clearance needs sustained valid frames and resets when enemies return", CombatClearance);
            Check("active fights retry targets at the reference cadence and stop when the health bar disappears", ActiveRetarget);
            Check("cooldowns, disabled liberation, and detected skill key", SkillSelection);
            Check("an unrecognized character uses the source default echo-to-liberation sequence", SourceFallback);
            Check("world liberation preference follows the confirmed battle through marker flicker", WorldLiberationPreference);
            Check("rotation waits for concerto and verifies switches", RotationAndSwitch);
            Check("switching honors party size and living teammates below eight percent", SmallPartyRotation);
            Check("challenge interaction distinguishes F and marker transitions", ChallengeTransitions);
            Check("standalone Abyss entry is gated by pickup and uses a cached OCR clock", StandaloneAbyssClock);
            Check("failed switch backs off and resumes safely", FailedSwitch);
            Check("low-health support follows the learned character", LowHealthSupport);
            Check("fight cleanup runs the character's own handoff before its healer switch", FightEndHandoff);
            Check("task fight waits for all character handoffs and a settled HUD", TaskFightEndHandoff);
            Check("configuration round-trip and validation", SettingsRoundTrip);
            Check("automatic local mode follows focus without changing fixed modes", GameSourceChecks.AutomaticMode);
            Check("automatic local mode serializes without renumbering saved modes", GameSourceChecks.SavedSource);
            Check("diagnostic logs preserve severity, UTF-8, concurrent writes and bounded retention", RunLogChecks.WritesAndRotates);
            Check("4C farm signatures load from the embedded table", FarmChecks.Signatures);
            Check("4C farm walks approach, challenge, combat and pickup", FarmChecks.StateMachine);
            Check("4C guide from terminal and alternate world entrance", FarmChecks.GuideEntrance);
            Check("4C farm teleport opens the guidebook and picks a boss", FarmChecks.TeleportFlow);
            Check("4C overworld recovery dismisses the death prompt", FarmChecks.ReviveFlow);
            Check("4C searches the known boss and confirms the map transition", FarmChecks.NamedBossSearch);
            Check("4C weekly guide row selects level before solo and team start", FarmChecks.WeeklyLevelAfterBookRow);
            Check("4C walk-in entry selects the level and confirms low stamina before starting", FarmChecks.WalkInBossLevel);
            Check("4C realm and open-world death routes honor the entry setting", FarmChecks.RealmRecoveryRoutes);
            Check("4C limits completed recoveries and clears the heal point on walk-in routes", FarmChecks.RealmRecoveryLimit);
            Check("4C re-enters once after a death and counts only the two cleared rounds", FarmChecks.DeathThenSuccessfulRounds);
            Check("4C farm retries map opening and travels only by the confirmed healing waypoint", FarmChecks.HealWaypointRecovery);
            Check("4C farm closes claim popups and restarts the challenge", FarmChecks.ClaimRestart);
            Check("4C farm teleport clicks the configured boss row", FarmChecks.TeleportBossRow);
            Check("4C farm uses the reference PC drop condition and cloud health grace", FarmChecks.EchoEndsCombat);
            Check("4C farm probes from a standstill without a reliable route", FarmChecks.StationaryTargetSearch);
            Check("4C farm releases movement when the verified scene disappears or a prompt appears; a missing marker follows zero angle", FarmChecks.ApproachStopsOnLostEvidence);
            Check("4C farm caps unguided forward movement after a confirmed teleport", FarmChecks.TravelWalkIsBounded);
            Check("4C farm can start on the team screen without an exploration HUD", FarmChecks.ManualStartAcceptsTeamScreen);
            Check("4C farm starts a verified interaction while skill HUD is incomplete", FarmChecks.ManualStartRecognizesVerifiedPartialHud);
            Check("4C farm approaches briefly when manually started inside a confirmed instance", FarmChecks.ManualRealmStartupWalkIsBounded);
            Check("4C farm stops loot movement and resumes the same fight when the target returns", FarmChecks.TargetReturnsDuringLoot);
            Check("4C farm starts at the settlement marker and selects rechallenge without claiming", FarmChecks.RechallengeFromSettlement);
            Check("4C farm approaches Nameless Explorer after verified rechallenge", FarmChecks.NamelessRechallengeApproach);
            Check("4C farm distinguishes selected and stacked rechallenge text from the reward objective", FarmChecks.RechallengeChoiceUsesSelectedRow);
            Check("4C farm opens the instance restart dialog when the only interaction is claim reward", FarmChecks.RechallengeFromRewardOnly);
            Check("4C farm keeps the confirmed instance when the treasure and realm markers disappear", FarmChecks.RechallengeKeepsConfirmedSettlement);
            Check("4C farm waits in place when the settlement interaction is intermittent", FarmChecks.RechallengeWaitsAtIntermittentPrompt);
            Check("4C farm waits for the absorb receipt before opening the restart dialog", FarmChecks.RechallengeWaitsForAbsorbReceipt);
            Check("4C farm clicks the OCR-matched boss level", FarmChecks.LevelOcr);
            Check("4C farm runs the Fenrico and Fallacy special flows", FarmChecks.BossScripts);
            Check("4C farm requires a pickup receipt and stops at its repeat limit", FarmChecks.PickupAndRepeatLimit);
            Check("4C farm retries pickup input and rechallenges after unconfirmed pickup", FarmChecks.UnconfirmedDrop);
            Check("4C farm steers toward a detected echo and tolerates short YOLO interruptions", FarmChecks.YoloPickupRecovery);
            Check("monthly card waits after each accepted click before confirming the team", WorldAssistChecks.MonthlyCardTiming);
            Check("4C dismisses blocking claim popups at most three times per task", FarmChecks.PopupRetryBudget);
            Check("4C echo rate updates on receipts and preserves its start across retries with drops", FarmChecks.EchoRateAfterRetry);
            Check("echo model preprocessing matches independent RGB tensors and preserves source crop", FarmChecks.EchoInputPixels);
            Check("4C searches one world view or four timed realm views for a detected echo", FarmChecks.YoloViews);
            Check("4C checks a direct drop before the timed walk and two-circle pickup paths", FarmChecks.WalkAndCirclePickup);
            Check("4C farm waits before dispatching any combat action", FarmChecks.WaitBeforeCombat);
            Check("4C farm skips the five nests and confirms the real challenge menu", FarmChecks.SpecialNightmareAndEntry);
            Check("echo enhancement rules match the reference", EnhanceChecks.Rules);
            Check("OCR matches detector and recognizer input pixels", OcrChecks.Inputs);
            Check("OCR excludes the slanted decoration above the activity counter", OcrChecks.DetectionScore);
            Check("OCR filters narrow rotated components with tied bounding-box areas", OcrChecks.NarrowRotatedBox);
            Check("OCR preserves reference box order, source bounds and rectified recognition batch width", OcrChecks.ReferenceOrdering);
            Check("OCR normalizes traditional Chinese with the system converter while preserving raw text", OcrChecks.TextNormalization);
            OcrDetectionChecks.Run(Check);
            OcrRecognitionChecks.Run(Check);
            Check("login rechecks the current button and phone form after four seconds", WorldAssistChecks.LoginSettle);
            Check("login privacy, quadrant, switch-account and disconnect controls", WorldAssistChecks.LoginControls);
            Check("client update confirmation waits, relaunch and cancellation", WorldAssistChecks.UpdateRestart);
            Check("game update recovery finds the attached, recent or registered launcher in order", WorldAssistChecks.LauncherFallback);
            Check("relative launcher resolution requires one verified install root and rejects ambiguity", WorldAssistChecks.TrustedRelativeLauncher);
            Check("client update reads the current installation's launcher package", WorldAssistChecks.UpdatePackage);
            Check("echo enhancement parses OCR stats", EnhanceChecks.Parsing);
            Check("echo enhancement removes stray marks from complete stat values", EnhanceChecks.NumberNoise);
            Check("echo enhancement first-crit uses the first valid crit type", EnhanceChecks.FirstCheckedCrit);
            Check("echo enhancement state machine starts from the bag", EnhanceChecks.Flow);
            Check("echo enhancement confirms the selected card and its actual level", EnhanceChecks.CandidateSelectionFeedback);
            Check("echo enhancement verifies ascending order and records why the list ends", EnhanceChecks.CandidateExhaustion);
            Check("echo enhancement confirms cultivation and retries once with another input path", EnhanceChecks.CultivateConfirmation);
            Check("echo enhancement preserves player settings and per-account overrides", EnhanceChecks.SettingsRoundTrip);
            Check("echo enhancement UI distinguishes only-save, defaults, start and per-account editing", EnhanceChecks.SettingsUi);
            Check("echo enhancement UI shows substat values, rejection reason and its screenshot link", EnhanceChecks.ProgressUi);
            Check("echo enhancement verifies insertion, dialog actions, marks and long pause recovery", EnhanceChecks.TimingAndMarks);
            Check("echo enhancement confirms materials and waits for the combined tuning button", EnhanceChecks.MaterialAndTuneRecognition);
            Check("echo enhancement waits for material selection and bounds retries after it clears", EnhanceChecks.MaterialPreparationRetries);
            Check("echo enhancement confirms the game accepted submission before counting a stage", EnhanceChecks.SubmissionConfirmation);
            Check("echo enhancement dismisses a result with both a heading and a return instruction", EnhanceChecks.ResultDismissal);
            Check("echo enhancement waits for captured feedback before repeating a result click", EnhanceChecks.ResultCloseConfirmation);
            Check("echo enhancement safely retries once if the result stays visible", EnhanceChecks.ResultCloseRetry);
            Check("echo enhancement waits for late tuning results before starting the next stage", EnhanceChecks.DelayedResultAfterVisibleStats);
            Check("echo enhancement requires a new substat before spending on another stage", EnhanceChecks.StageProgress);
            Check("domain stamina and scroll math", DomainChecks.Arithmetic);
            Check("domain rechallenge text and nearest-waypoint order", DomainChecks.NavigationAndRechallenge);
            Check("waveplate over-cap balance and daily counter isolation", DomainChecks.HeaderBounds);
            Check("guidebook scrollbar ignores bright artifacts and follows wide-screen anchors", DomainChecks.GuideScrollbar);
            Check("domain options validate", DomainChecks.Options);
            Check("Rahai solver clears intersecting lines and plans across pieces", RahaiChecks.Solver);
            Check("cultivation recognizes the target page, quantities and echo rows", CultivationChecks.ReadRows);
            Check("cultivation verifies reward inventory and the remaining weekly claims", CultivationChecks.Rewards);
            Check("cultivation verifies partial inventories and waveplate deductions before credit", CultivationChecks.PartialInventory);
            Check("battle assist respects interaction limits and restores only cursor jumps", WorldAssistChecks.Rules);
            Check("cursor restoration skips cloud and disabled sessions and throttles native restores", WorldAssistChecks.CursorRestore);
            Check("Second Sola recognizes the date, event marker and exam answer", SecondSolaChecks.Decisions);
            Check("nightmare nest counts and cache keys", NightmareChecks.Counts);
            Check("nightmare nest cache keys tolerate jitter", NightmareChecks.Keys);
            Check("nightmare options fall back to both categories when empty", NightmareChecks.Options);
            Check("nightmare skips clipped buttons and tries the next visible nest", NightmareChecks.VisibleVisitButtons);
            Check("named nightmare nests are identified across scroll positions without merging other nests", NightmareChecks.NamedTargetsAcrossPages);
            Check("purification associates direct challenge buttons with the correct visible nest", NightmareChecks.PurificationTargets);
            Check("nightmare reads partial and full progress on the same named row", NightmareChecks.SameNestProgress);
            Check("nightmare pickup looks for the absorb interaction", NightmareChecks.AbsorbLabels);
            Check("nightmare requires a new pickup receipt after F", NightmareChecks.ReceiptMustBeNew);
            Check("garden progress detection", GardenChecks.Progress);
            Check("task indicator signatures load", GardenChecks.Signatures);
            Check("merge full-batch detection", MergeChecks.FullBatch);
            Check("echo change OCR text fixes", ChangeChecks.TextFixes);
            Check("echo change options validate", ChangeChecks.Options);
            Check("daily plan decisions", DailyChecks.Plan);
            Check("daily options validate", DailyChecks.Options);
            Check("custom daily choices and ordering survive a settings round-trip", DailyChecks.SavesOrderedPlan);
            Check("older daily settings preserve the original plan and toggles", DailyChecks.ReadsLegacySettings);
            Check("daily evidence snapshots frame colors and retains recent runs safely",
                DailyEvidenceChecks.SavesAndKeepsRecentRuns);
            Check("battle pass matches the verified page and claim controls",
                BattlePassChecks.ConfirmAnchorsAndClaimControl);
            Check("battle pass compares the same claim button pixels",
                BattlePassChecks.CompareClaimButtonPixels);
            Check("daily additional task options are validated before any task starts", DailyChecks.AdditionalConfig);
            Check("account key normalisation", AccountChecks.Keys);
            Check("account dropdown, duplicate list entries and scoped login button", AccountChecks.LoginLabels);
            Check("account plans reject malformed types and nested account tasks before execution", AccountChecks.InvalidPlans);
            Check("already-cancelled account runners never start account operations", AccountChecks.CancelledPlans);
            Check("account editor retains isolated task drafts and unregistered references", AccountChecks.EditorDrafts);
            Check("account editor displays previous success, failure and cancellation", AccountChecks.EditorHistory);
            Check("account store round-trip and validation", AccountChecks.Store);
            Check("per-account config applies and restores", AccountChecks.Config);
            Check("account daily plans override and restore the global plan safely", AccountChecks.DailyConfig);
            Check("configured accounts resolve registered tasks and isolate imported options", AccountChecks.ReferencePlan);
            Check("input cannot run without a verified foreground window", MissingWindow);
            Check("local game window is recognized without a readable title or installation path", LocalWindowIdentity);
            if (LocalTaskWindowChecks.CanStartFixture())
                Check("automatic task mode finds and restores an unfocused game window behind its splash",
                    LocalTaskWindowChecks.DiscoversBackgroundWindow);
            else
                Console.WriteLine("SKIP automatic task window fixture (TideCombat or the game is already running)");
            Check("input privilege preflight detects elevated clients and reads native process tokens", InputPrivilegePreflight);
            Check("administrator restart preserves domain and nightmare tasks", TaskResumeRouting);
            Check("the command line resolves one daily task and rejects unknown options", DailyCommandLineRouting);
            Check("daily command-line exit is scoped to successful runs", DailyCommandLineExit);
            Check("cloud combat uses health evidence and a bounded grace period", CloudGrace);
            Check("retargeting keeps the long health window and uses the short window without health", HealthRetargetWindows);
            Check("visible cloud health starts combat without sending a needless lock", CloudEntry);
            Check("cloud control accepts only the official endpoint", CloudPageScope);
            Check("challenge start without a visible timer and instance liberation", ChallengeOpening);
            Check("known character switch tolerates hidden HUD and retries without losing intro", SwitchTransition);
            Check("full concerto refreshes the next character before a switch is confirmed", RecheckConcerto);
            Check("execution is ordered around the confirmed change of slot", SwitchExecutionOrder);
            Check("full concerto locks both known and configured healers for sixteen combat seconds", HealerSwitchLock);
            Check("rotation priority respects roles, buffs, cooldowns, and character overrides", SwitchSelectionRules);
            Check("switch priority ranks preserve all integer bands and within-band offsets", SwitchPriorityOffsets);
            Check("character preferences keep base roles, portrait aliases and variant buff durations", CharacterPreferences);
            Check("party recognition reports only stable roster changes and clears after leaving world", PartyRecognitionReports);
            Check("Phrolova and healer handoffs respect execution and switch-lockout boundaries", PhrolovaHandoff);
            Check("Phrolova defers a non-intro execution to the character that actually arrives", PhrolovaDeferredExecution);
            Check("explicit execution is separate from Phrolova's automated switch gate", PhrolovaExplicitExecution);
            Check("Shorekeeper's intro and airborne states survive loss of lock until the guard ends", ShoreKeeperCombatGrace);
            Check("a rotation waits five seconds for a hidden party HUD before ending the failed switch", SwitchTimeout);
            Check("a rotation gives visible party slots ten seconds to confirm the switch", VisiblePartySwitchTimeout);
            Check("task-specific healer settings override global opening and closing switches", ConfiguredHealerSwitch);
            Check("freeze-adjusted buffs and Hecate execution windows mirror the reference", FreezeAdjustedDecisions);
            Check("confirmed hidden-HUD animation records its duration in the shared clock", AnimationFreezeObservation);
            Check("animation clicks and F have independent reference cadence and honor the delay and opt-out", AnimationKeyCadence);
            Check("only a fresh hidden-team HUD frame can emit input from the animation path", PlannerAnimationInputs);
            Check("liberation helper retries bright R and requires animation after it dims", LiberationRetryFlow);
            Check("dim liberation gets the short retry window and skips true cooldown", DimLiberationFlow);
            Check("liberation varies attack inputs before and during the animation", LiberationAttackFlow);
            Check("resonance retries with normal attack and delays F only inside its animation", ResonanceAnimationFlow);
            Check("liberation timeout terminates the task rotation rather than starting another", LiberationAbort);
            Check("the final 0.2 seconds of a visible cooldown are not cooling", SkillCooldownBoundary);
            Check("a persistently shifted PC target layout gets one timed pair of menu presses", RecoverShiftedTarget);
            Check("a valid Abyss countdown keeps combat active beyond five seconds without a lock", ChallengeTimerCombat);
            Check("challenge clock rejects health and invalid seconds and accepts both fraction separators", ChallengeClockFormat);
            Check("the countdown parser distinguishes time, zero and invalid punctuation", ChallengeClockRead);
            Check("off-center enemy bars use the source color, width and height rules", EnemyColorStrip);
            Check("Roccia's confirmed intro gives the recipient one toolbox use before handing off", RocciaToolboxHandoff);
            Check("background client crop and key flags", BackgroundCoordinates);
            Check("background capture fallback reads an offline test window", BackgroundFallback);
            Check("background keyboard and mouse releases reach only the verified test window", WindowMessageDelivery);
            Check("menu clicks use the visible child window and its local coordinates", MenuClickDelivery);
            Check("pickup F reaches the verified window with both key transitions", PickupKeyDelivery);
            Check("background menu actions deliver buttons, wheel, Alt and text to the verified window",
                MenuInputChecks.BackgroundDispatch);
            Check("a direct combat-end handoff chooses a living teammate", CombatEndCleanup);
            Check("Camellya visual charge separates striped fill from interference", VisualCharge);
            Check("concerto measures the connected arc against the last full ring of its element", ConcertoCalibration);
            RotationChecks.Run(Check);
            CombatReferenceChecks.Run(Check);
            if (args.Length == 2 && args[0] == "--reference")
            {
                Check("echo enhancement replays bag, stat rows and the real overflow dialog",
                    () => EnhanceChecks.ReplayReference(args[1]));
                Check("4C farm recognizes guidebook rows and distinguishes team, travel and confirmation menus",
                    () => FarmChecks.ReplayMenus(args[1]));
                Check("4C farm localizes the settlement marker in the reference frame",
                    () => FarmChecks.ReplaySettlementMarker(args[1]));
                Check("4C farm reads left-side rechallenge on the reference exit dialog and excludes reward dialogs",
                    () => FarmChecks.ReplayRechallengeDialog(args[1]));
                Check("OCR reads native screenshot pixels and projects click bounds", () => OcrChecks.NativeCrop(args[1]));
                Check("echo model matches independent detections from raw 1080p and 4K captures", () => FarmChecks.ReplayEchoDetector(args[1]));
                Check("overlapping echo rows favor the left target after suppression", () => FarmChecks.MultiEchoOrder(args[1]));
                Check("nightmare recognizes selected and unselected Boss Guidebook tabs from the reference",
                    () => NightmareChecks.ReplayGuidebook(args[1]));
                Check("guidebook detects layout shifts and rejects occluded Boss tabs",
                    () => GuidebookVisionChecks.ReplayShifted(args[1]));
                Check("terminal recognition locates the guide shortcut and rejects other screens",
                    () => GuidebookVisionChecks.ReplayTerminal(args[1]));
                Check("battle pass Terminal entry is read on the upstream screenshot",
                    () => BattlePassChecks.VerifyTerminalEntryReference(args[1]));
                Check("nightmare distinguishes the guidebook, team start and travel screens",
                    () => NightmareChecks.ReplayNestEntry(args[1]));
                Check("nightmare OCR reads the challenge button and aligns visit buttons to the correct rows",
                    () => NightmareChecks.ReplayNestButtons(args[1]));
                Check("nightmare detects displaced and stacked absorb prompts without accepting unrelated interactions",
                    () => NightmareChecks.ReplayAbsorbPrompt(args[1]));
                Check("nightmare detects the reference pickup receipt glyphs only in the loot notice",
                    () => NightmareChecks.ReplayPickupReceipt(args[1]));
                ReplayReference(args[1]);
                RotationChecks.Replay(args[1], Check);
            }
            Console.WriteLine(failed == 0 ? "All checks passed." : failed + " check(s) failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string name, Action test)
        {
            foreach (string filter in skip)
            {
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Console.WriteLine("SKIP " + name);
                return;
            }
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("FAIL " + name + " | " + error.Message);
            }
        }

        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        private static HudSnapshot BattleHud()
        {
            var hud = new HudSnapshot { Valid = true, Locked = true, EnemyHealth = true, Current = 0, OpenWorld = true };
            for (int slot = 0; slot < 3; slot++)
                hud.Health[slot] = 1;
            hud.Keys[0] = 'E';
            hud.Keys[1] = 'R';
            hud.Keys[2] = 'Q';
            return hud;
        }

        private static CombatSettings CustomSlotOptions()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            settings.Slots[0].FieldSeconds = 4.1;
            settings.Slots[1].FieldSeconds = 2.8;
            settings.Slots[2].FieldSeconds = 3.0;
            return settings;
        }

        private static void LockConfirmation()
        {
            var planner = new BattlePlanner(CustomSlotOptions());
            var hud = BattleHud();
            Require(planner.Observe(hud, 0).Kind == ActionKind.Wait, "first frame must wait");
            Require(planner.Observe(hud, 100).Kind == ActionKind.Attack, "stable lock should attack");
            hud.Valid = false;
            Require(planner.Observe(hud, 300).Kind == ActionKind.Wait, "an invalid HUD must suppress actions");
            hud.Valid = true;
            Require(planner.Observe(hud, 400).Kind == ActionKind.Wait, "re-entry requires a new confirmation");
            hud.Locked = false;
            hud.EnemyHealth = false;
            Require(planner.Observe(hud, 2200).Kind == ActionKind.Target && planner.Fighting, "short loss should start retargeting");
            Require(planner.Observe(hud, 5300).Kind == ActionKind.Wait && !planner.Fighting, "retarget timeout should clear the rotation");
        }

        private static void AutoTarget()
        {
            CombatSettings settings = CombatSettings.Defaults();
            var planner = new BattlePlanner(settings);
            HudSnapshot hud = BattleHud();
            hud.Locked = false;
            Require(planner.Observe(hud, 50).Kind == ActionKind.Wait, "one hit on a health bar is insufficient");
            planner.Invalidate(100);
            Require(planner.Observe(hud, 150).Kind == ActionKind.Wait, "focus loss must clear health evidence");
            BattleAction target = planner.Observe(hud, 250);
            Require(target.Kind == ActionKind.Target, "an unlocked visible enemy should be targeted");
            planner.Sent(target, 250);
            Require(planner.Observe(hud, 400).Kind == ActionKind.Wait, "allow time for the lock before retrying");
            settings.AutoTarget = false;
            Require(planner.Observe(hud, 1400).Kind == ActionKind.Wait, "manual lock mode must not click the middle button");
        }

        private static void ActiveRetarget()
        {
            var settings = CombatSettings.Defaults();
            settings.AutoTarget = false;
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            planner.Observe(hud, 0);
            planner.Observe(hud, 100);
            hud.Locked = false;
            BattleAction attempt = planner.Observe(hud, 200);
            Require(attempt.Kind == ActionKind.Target, "auto-target setting only gates entry into combat");
            planner.Sent(attempt, 200);
            Require(planner.Observe(hud, 399).Kind == ActionKind.Wait && planner.Observe(hud, 400).Kind == ActionKind.Target,
                "local retarget interval should be 200 ms");
            hud.EnemyHealth = false;
            planner.Observe(hud, 500);
            Require(planner.Observe(hud, 1701).Kind == ActionKind.Wait && !planner.Fighting,
                "a health-led retry should end after 1.2 seconds with no health bar");
        }

        private static void StandaloneRequiresLock()
        {
            CombatSettings settings = CustomSlotOptions();
            settings.RotateParty = false;
            var planner = BattleLoop.CreateStandalonePlanner(settings, cloud: false);
            HudSnapshot hud = BattleHud();
            hud.Locked = false;
            Require(planner.Observe(hud, 0).Kind == ActionKind.Wait && !planner.Fighting,
                "one health-bar observation should not start the fight");
            BattleAction target = planner.Observe(hud, 100);
            Require(target.Kind == ActionKind.Target && !planner.Fighting,
                "a visible enemy without a lock should receive a middle-click attempt first");
            planner.Sent(target, 100);
            Require(planner.Observe(hud, 299).Kind == ActionKind.Wait && !planner.Fighting,
                "no attack while the lock is unconfirmed");
            Require(planner.Observe(hud, 300).Kind == ActionKind.Target && !planner.Fighting,
                "retry targeting without starting to attack");
            Require(planner.Observe(hud, 3100).Kind == ActionKind.Wait && !planner.Fighting,
                "a targeting timeout must not fall back to attacking");

            settings.AutoTarget = false;
            Require(planner.Observe(hud, 3400).Kind == ActionKind.Wait && !planner.Fighting,
                "manual lock mode should not send input before the player locks on");
            hud.Locked = true;
            Require(planner.Observe(hud, 3500).Kind == ActionKind.Wait && !planner.Fighting,
                "wait for a stable lock");
            Require(planner.Observe(hud, 3600).Kind == ActionKind.Attack && planner.Fighting,
                "start attacking after the confirmed lock");
        }

        private static void StandaloneStopsOnDeath()
        {
            CombatSettings settings = CustomSlotOptions();
            var planner = BattleLoop.CreateStandalonePlanner(settings, cloud: false);
            HudSnapshot hud = BattleHud();
            planner.Observe(hud, 0);
            Require(planner.Observe(hud, 100).Kind == ActionKind.Attack && planner.Fighting,
                "start a turn on a stable lock before testing the defeat");

            var defeated = new HudSnapshot { Dead = true };
            Require(planner.Observe(defeated, 200).Kind == ActionKind.Wait && !planner.Fighting,
                "seeing the defeat prompt must end the turn without input");
            Require(planner.Observe(defeated, 400).Kind == ActionKind.Wait && !planner.Fighting,
                "an ongoing defeat prompt must not trigger another action");
            Require(planner.Observe(hud, 500).Kind == ActionKind.Wait && !planner.Fighting,
                "manual recovery must start a fresh lock confirmation");
            Require(planner.Observe(hud, 600).Kind == ActionKind.Attack && planner.Fighting,
                "after manual recovery, a new confirmed lock may start another turn");
        }

        private static void TaskCombatEntry()
        {
            CombatSettings settings = CustomSlotOptions();
            settings.AutoTarget = settings.RotateParty = false;
            var planner = CombatRunner.CreateTaskPlanner(settings, cloud: false);
            HudSnapshot hud = BattleHud();
            hud.Locked = false;
            Require(planner.Observe(hud, 0).Kind == ActionKind.Wait && !planner.Fighting,
                "first frame of visible enemy health should only start confirmation");
            BattleAction target = planner.Observe(hud, 100);
            Require(target.Kind == ActionKind.Target && !planner.Fighting,
                "one-time PC tasks should try the lock even when standalone auto-target is disabled");
            planner.Sent(target, 100);
            Require(planner.Observe(hud, 299).Kind == ActionKind.Wait && !planner.Fighting,
                "give the lock time to land without attacking");
            target = planner.Observe(hud, 300);
            Require(CombatRunner.AddBlindTargetProbe(target, hud, false).Kind == ActionKind.Target && !planner.Fighting,
                "the blind search timer must not throttle the 200 ms health-backed retry");
            planner.Sent(target, 300);
            Require(planner.Observe(hud, 3100).Kind == ActionKind.Wait && !planner.Fighting,
                "an unconfirmed lock must time out without opening a rotation");
            hud.Locked = true;
            Require(planner.Observe(hud, 3200).Kind == ActionKind.Wait && !planner.Fighting,
                "a single lock observation does not yet confirm entry");
            Require(planner.Observe(hud, 3300).Kind == ActionKind.Attack && planner.Fighting,
                "a stable PC lock starts combat");

            var cloud = CombatRunner.CreateTaskPlanner(settings, cloud: true);
            hud = BattleHud();
            hud.Locked = false;
            Require(cloud.Observe(hud, 0).Kind == ActionKind.Wait && !cloud.Fighting,
                "cloud starts with a confirmation frame");
            Require(cloud.Observe(hud, 100).Kind == ActionKind.Attack && cloud.Fighting,
                "cloud tasks may use enemy health directly with standalone auto-target disabled");
        }

        private static void TaskBlindSearchAndCapture()
        {
            HudSnapshot hud = BattleHud();
            hud.Locked = hud.EnemyHealth = false;
            BattleAction wait = default(BattleAction);
            Require(CombatRunner.AddBlindTargetProbe(wait, hud, false).Kind == ActionKind.Wait &&
                CombatRunner.AddBlindTargetProbe(wait, hud, true).Kind == ActionKind.Target,
                "only enabled, due blind search sends an input without enemy evidence");
            hud.ChallengePrompt = true;
            Require(CombatRunner.AddBlindTargetProbe(wait, hud, true).Kind == ActionKind.Wait,
                "a challenge interaction suppresses blind probes");
            hud.ChallengePrompt = false;
            hud.EnemyHealth = true;
            Require(CombatRunner.AddBlindTargetProbe(wait, hud, true).Kind == ActionKind.Wait,
                "health-backed entry waits for the planner instead of blind search");
            hud.EnemyHealth = hud.Valid = false;
            Require(CombatRunner.AddBlindTargetProbe(wait, hud, true).Kind == ActionKind.Wait,
                "blind probes require a valid HUD");

            int captures = 0;
            Func<long, bool> capture = now => { captures++; return now == 300; };
            Require(!CombatRunner.TryCaptureDuringCombat(true, false, capture, 100) &&
                !CombatRunner.TryCaptureDuringCombat(false, true, capture, 200) && captures == 0,
                "unconfirmed combat or an unusable HUD cannot invoke the capture callback");
            Require(CombatRunner.TryCaptureDuringCombat(true, true, capture, 300) && captures == 1,
                "a confirmed fight can still exit as soon as the capture is verified");
        }

        private static void CombatClearance()
        {
            var check = new CombatClearCheck();
            check.Observe(true, true, 0);
            check.Observe(true, false, 300);
            check.Observe(true, false, 800);
            Require(!check.IsQuiet(1000) && check.SawEnemy, "brief target loss is not a cleared fight");
            check.Observe(false, false, 2300);
            Require(!check.IsQuiet(4000), "an unavailable game frame cannot prove clearance");
            check.Observe(true, false, 4500);
            check.Observe(true, false, 5400);
            check.Observe(true, false, 6250);
            Require(check.IsQuiet(6250), "three stable scene observations over the interval confirm quiet");
            check.Observe(true, true, 6300);
            Require(!check.IsQuiet(6300), "an enemy appearing after a wave cancels the clearance");
        }

        private static void SkillSelection()
        {
            CombatSettings settings = CustomSlotOptions();
            settings.UseLiberation = false;
            settings.RotateParty = false;
            var planner = new BattlePlanner(settings);
            HudSnapshot hud = BattleHud();
            hud.Skills[(int)Skill.Resonance] = SkillState.Cooling;
            hud.Skills[(int)Skill.Echo] = SkillState.Ready;
            hud.Skills[(int)Skill.Liberation] = SkillState.Ready;
            planner.Observe(hud, 100);
            BattleAction action = planner.Observe(hud, 200);
            Require(action.Kind == ActionKind.Cast && action.Skill == Skill.Echo && action.Key == 'R',
                "cast the detected echo binding and skip cooling resonance and disabled liberation");
            planner.Sent(action, 200);
            Require(planner.Observe(hud, 400).Kind == ActionKind.Wait, "allow echo animation time");
            Require(planner.Observe(hud, 900).Kind == ActionKind.Attack, "do not repeat the same skill immediately");
            hud.Keys[(int)Skill.Echo] = '\0';
            Require(planner.Observe(hud, 2100).Kind == ActionKind.Attack, "ambiguous key must not be used");
        }

        private static void SourceFallback()
        {
            var planner = new BattlePlanner(CombatSettings.Defaults());
            var hud = BattleHud();
            hud.Skills[0] = SkillState.Cooling;
            hud.Skills[1] = hud.Skills[2] = SkillState.Ready;
            planner.Observe(hud, 0);
            BattleAction first = planner.Observe(hud, 100);
            Require(first.Kind == ActionKind.Button && first.Button == BattleButton.Echo && first.NativeKey == 'R',
                "an automatic slot without a recognized portrait opens with the raw echo helper");
            planner.Sent(first, 100);
            hud.Skills[1] = SkillState.Cooling;
            BattleAction second = planner.Observe(hud, 200);
            Require(second.Kind == ActionKind.Button && second.Button == BattleButton.Liberation && second.NativeKey == 'Q',
                "the unrecognized character continues to liberation before resonance");
        }

        private static void WorldLiberationPreference()
        {
            var settings = CombatSettings.Defaults();
            settings.UseLiberation = settings.SupportOpener = false;
            foreach (bool inWorld in new[] { true, false })
            {
                var planner = new BattlePlanner(settings);
                var hud = BattleHud();
                hud.OpenWorld = inWorld;
                hud.Fighters[0] = "Hsin";
                hud.Skills[0] = hud.Skills[1] = SkillState.Cooling;
                hud.Skills[2] = SkillState.Ready;
                hud.Indicators.Add("hsin_lib1");
                planner.Observe(hud, 0);
                bool sawLiberation = false;
                for (int time = 100; time <= 600; time += 100)
                {
                    BattleAction action = planner.Observe(hud, time);
                    if (action.Kind == ActionKind.Button && action.Button == BattleButton.Liberation) sawLiberation = true;
                    if (action.Kind != ActionKind.Wait) planner.Sent(action, time);
                    hud.OpenWorld = !inWorld;
                }
                Require(sawLiberation == !inWorld, "only the battle confirmed outside the world uses liberation");
            }
        }

        private static void RotationAndSwitch()
        {
            var planner = new BattlePlanner(CustomSlotOptions());
            HudSnapshot hud = BattleHud();
            planner.Observe(hud, 100);
            planner.Observe(hud, 200);
            Require(planner.Observe(hud, 4300).Kind == ActionKind.Attack, "main should briefly wait for full concerto");
            hud.ConcertoFull = true;
            BattleAction next = planner.Observe(hud, 4500);
            Require(next.Kind == ActionKind.Switch && next.Slot == 2, "main should pass to support");
            planner.Sent(next, 4500);
            Require(planner.Observe(hud, 4900).Kind == ActionKind.Wait, "wait while the old slot is still shown");
            hud.Current = 2;
            Require(planner.Observe(hud, 5000).Kind == ActionKind.Wait, "one new-slot frame is not enough");
            Require(planner.Observe(hud, 5100).Kind == ActionKind.Wait, "allow the new character to enter");
            Require(planner.Observe(hud, 5500).Kind == ActionKind.Attack, "resume after confirming the switch");
        }

        private static void SmallPartyRotation()
        {
            for (int size = 1; size <= 3; size++)
                foreach (double health in new[] { 0, 1 / 65.0, .08, .081, 1 })
                {
                    var planner = new BattlePlanner(CustomSlotOptions());
                    HudSnapshot hud = BattleHud();
                    hud.ConcertoFull = true;
                    hud.PartySize = size;
                    hud.Health[1] = health;
                    planner.Observe(hud, 0);
                    planner.Observe(hud, 100);
                    BattleAction choice = planner.Observe(hud, 4500);
                    int target = size == 3 ? 2 : size == 2 && health > 0 ? 1 : -1;
                    Require(target < 0 ? choice.Kind != ActionKind.Switch : choice.Kind == ActionKind.Switch && choice.Slot == target,
                        "party size " + size + ", teammate HP " + health + ", expected slot " + target);
                }
        }

        private static void ChallengeTransitions()
        {
            foreach (int branch in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var planner = new BattlePlanner(CustomSlotOptions());
                HudSnapshot hud = BattleHud();
                hud.OpenWorld = hud.Locked = hud.EnemyHealth = false;
                hud.ChallengePrompt = hud.ChallengeMarker = hud.ChallengeInteractVisible = true;
                planner.Observe(hud, 100);
                hud.ChallengePrompt = false;
                hud.ChallengeInteractVisible = branch == 2;
                hud.ChallengeMarker = branch == 0 || branch == 1;
                if (branch == 3) hud.Valid = false;
                if (branch == 4) planner.CancelChallengePrompt();
                if (branch == 6) hud.Current = 1;
                if (branch == 8) hud.OpenWorld = true;
                long seen = branch == 5 ? 5100 : branch == 7 ? 5099 : 200;
                planner.Observe(hud, seen);
                bool started = hud.InChallenge;
                if (branch <= 4)
                {
                    hud.InChallenge = hud.ChallengeInteractVisible = hud.ChallengeMarker = false;
                    hud.Valid = true;
                    planner.Observe(hud, seen + (branch == 1 ? 501 : 500));
                    started = hud.InChallenge;
                }
                Require(started == (branch == 0 || branch == 6 || branch == 7), "challenge transition " + branch);
            }
            var settings = CustomSlotOptions();
            settings.AutoTarget = false;
            var refreshed = new BattlePlanner(settings);
            var prompt = BattleHud();
            prompt.OpenWorld = prompt.Locked = prompt.EnemyHealth = false;
            prompt.ChallengePrompt = prompt.ChallengeMarker = true;
            refreshed.Observe(prompt, 100);
            refreshed.Observe(prompt, 2100);
            prompt.ChallengePrompt = prompt.ChallengeMarker = false;
            refreshed.Observe(prompt, 2400);
            Require(prompt.InChallenge, "the last joint sighting starts combat");
            prompt.InChallenge = false;
            refreshed.Observe(prompt, 7099);
            Require(prompt.InChallenge, "the five-second window comes from the last joint sighting");
            prompt.InChallenge = false;
            refreshed.Observe(prompt, 7100);
            Require(!prompt.InChallenge, "the temporary challenge opening expires exactly at five seconds");
            prompt.ChallengePrompt = prompt.ChallengeMarker = true;
            refreshed.Observe(prompt, 9000);
            refreshed.Invalidate(9100);
            prompt.ChallengePrompt = prompt.ChallengeMarker = false;
            refreshed.Observe(prompt, 9200);
            Require(!prompt.InChallenge, "reset clears a stale challenge prompt");

            var ordinary = BattleHud();
            ordinary.OpenWorld = ordinary.Locked = ordinary.EnemyHealth = false;
            var unseen = new BattlePlanner(settings);
            unseen.Observe(ordinary, 100);
            unseen.Observe(ordinary, 200);
            Require(!ordinary.InChallenge && !unseen.Fighting, "no prompt or joint sighting cannot start combat");
            var nextFrame = new BattlePlanner(settings);
            prompt.ChallengePrompt = prompt.ChallengeMarker = true;
            nextFrame.Observe(prompt, 100);
            nextFrame.Observe(ordinary, 200);
            Require(ordinary.InChallenge, "replacing only the captured frame preserves a previously verified prompt");
            ordinary.InChallenge = false;
            var unreadableTeam = new BattlePlanner(settings);
            unreadableTeam.Observe(prompt, 100);
            unreadableTeam.Observe(new HudSnapshot(), 200);
            unreadableTeam.Observe(ordinary, 300);
            Require(!ordinary.InChallenge && !unreadableTeam.Fighting, "an unverified team clears the active challenge candidate");

            var shared = new ChallengeStartObservation();
            shared.Observe(prompt, 5000, consume: false);
            shared.Observe(ordinary, 5100, consume: false);
            var nextCombat = new BattlePlanner(settings, challengeObservation: shared, challengeClock: () => 5150);
            nextCombat.Observe(ordinary, 50);
            Require(ordinary.InChallenge, "a new task planner can consume the preceding passive sighting in the same window session");
            ordinary.InChallenge = false;
            var afterConsumption = new BattlePlanner(settings, challengeObservation: shared, challengeClock: () => 5200);
            afterConsumption.Observe(ordinary, 100);
            Require(!ordinary.InChallenge, "a second planner cannot consume the same start twice");
        }

        private static void StandaloneAbyssClock()
        {
            CombatSettings settings = CustomSlotOptions();
            settings.AutoTarget = false;
            settings.WorldAssist.PickUp = false;
            var planner = BattleLoop.CreateStandalonePlanner(settings, cloud: false);
            HudSnapshot hud = BattleHud();
            hud.OpenWorld = hud.Locked = hud.EnemyHealth = false;
            hud.ChallengePrompt = hud.ChallengeMarker = true;
            planner.Observe(hud, 100);
            hud.ChallengePrompt = hud.ChallengeMarker = false;
            planner.Observe(hud, 200);
            Require(!hud.InChallenge && !planner.Fighting, "disabled pickup must not force an Abyss start");

            int reads = 0;
            double seconds = 42;
            Func<double> read = () => { reads++; return seconds; };
            hud.Locked = hud.Countdown = true;
            planner.Observe(hud, 300, read);
            planner.Observe(hud, 400, read);
            hud.Locked = false;
            Require(planner.Observe(hud, 500, read).Kind == ActionKind.Target && reads == 0 && !hud.InChallenge,
                "a manually locked fight does not let a glyph countdown activate Abyss protection");

            settings.WorldAssist.PickUp = true;
            planner = BattleLoop.CreateStandalonePlanner(settings, cloud: false);
            hud.ChallengePrompt = hud.ChallengeMarker = true;
            planner.Observe(hud, 100, read);
            hud.ChallengePrompt = hud.ChallengeMarker = hud.Countdown = false;
            planner.Observe(hud, 200, read);
            planner.Observe(hud, 300, read);
            Require(planner.Fighting && hud.InChallenge && reads == 0,
                "confirmed disappearance starts the challenge without OCR within the initial window");
            hud.InChallenge = false;
            planner.Observe(hud, 5100, read);
            Require(reads == 1 && hud.InChallenge, "positive OCR maintains a confirmed challenge");

            seconds = 0;
            hud.InChallenge = false;
            planner.Observe(hud, 5499, read);
            Require(reads == 1 && hud.InChallenge, "reuse the read result for 400 milliseconds");
            hud.InChallenge = false;
            planner.Observe(hud, 5500, read);
            Require(reads == 2 && !hud.InChallenge, "zero clears countdown evidence");
            hud.InChallenge = false;
            planner.Observe(hud, 5501, read);
            Require(reads == 2 && !hud.InChallenge, "an expired timer is also cached");

            seconds = double.NaN;
            hud.InChallenge = false;
            planner.Observe(hud, 5900, read);
            planner.Observe(hud, 5999, read);
            Require(reads == 3 && !hud.InChallenge, "unreadable OCR stays unconfirmed for the cache interval");
        }

        private static void FailedSwitch()
        {
            var planner = new BattlePlanner(CustomSlotOptions());
            HudSnapshot hud = BattleHud();
            hud.ConcertoFull = true;
            hud.Health[1] = 0;
            planner.Observe(hud, 0);
            planner.Observe(hud, 100);
            BattleAction next = planner.Observe(hud, 4500);
            Require(next.Kind == ActionKind.Switch && next.Slot == 2, "only a living character can be chosen");
            planner.Sent(next, 4500);
            Require(planner.Observe(hud, 5700).Kind == ActionKind.Attack, "unconfirmed switch must back off");
        }

        private static void LowHealthSupport()
        {
            var options = CombatSettings.Defaults();
            options.SwitchAtLowHealth = true;
            var planner = new BattlePlanner(options);
            HudSnapshot hud = BattleHud();
            hud.Health[0] = 0.25;
            hud.Health[2] = 0.10;
            hud.Profiles[1] = new CharacterProfile { Name = "测试角色", Rotation = Rotation.ForRole(BattleRole.Support) };
            planner.Observe(hud, 0);
            BattleAction action = planner.Observe(hud, 100);
            Require(action.Kind == ActionKind.Switch && action.Slot == 1, "trained support in a new slot should be selected");
        }

        private static void FightEndHandoff()
        {
            foreach (string fighter in new[] { "Augusta", "Camellya" })
            {
                var options = CombatSettings.Defaults();
                options.SupportOpener = false;
                var planner = new BattlePlanner(options);
                var hud = BattleHud();
                hud.Fighters[0] = fighter;
                hud.Fighters[1] = "Chixia";
                hud.Fighters[2] = "Verina";
                planner.Observe(hud, 0);
                planner.Observe(hud, 100);
                options.SupportOpener = fighter == "Augusta";
                hud.Locked = hud.EnemyHealth = false;
                planner.Observe(hud, 200);
                BattleAction first = planner.Observe(hud, 3200);
                if (fighter == "Camellya")
                {
                    Require(first.Kind == ActionKind.Wait, "Camellya's shared helper does not force a slot in Auto Combat");
                    continue;
                }
                Require(first.Kind == ActionKind.Switch && first.Slot == 1, "Augusta's post-combat action selects the next slot");
                planner.Sent(first, 3200);
                hud.Current = 1;
                Require(planner.Observe(hud, 3299).Kind == ActionKind.Wait, "wait for the initial switch to settle");
                BattleAction healer = planner.Observe(hud, 3400);
                Require(healer.Kind == ActionKind.Switch && healer.Slot == 2, "then switch to the configured healer");
                planner.Sent(healer, 3400);
                hud.Current = 2;
                Require(planner.Observe(hud, 3600).Kind == ActionKind.Wait, "stop after confirming the healer");
            }
        }

        private static void TaskFightEndHandoff()
        {
            foreach (string fighter in new[] { "Camellya", "Iuno", "Linnai", "Mornye", "Aemeath", "Ciaccona" })
            {
                var options = CombatSettings.Defaults();
                options.SupportOpener = false;
                var planner = CombatRunner.CreateTaskPlanner(options, cloud: false);
                HudSnapshot hud = BattleHud();
                hud.Fighters[0] = fighter;
                hud.Fighters[1] = "Chixia";
                hud.Fighters[2] = "Verina";
                planner.Observe(hud, 0);
                planner.Observe(hud, 100);
                var roster = (FighterMemory[])typeof(BattlePlanner).GetField("roster", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(planner);
                roster[0].InRecital = fighter == "Ciaccona";
                hud.Locked = hud.EnemyHealth = false;
                planner.Observe(hud, 200);
                BattleAction first = planner.Observe(hud, 3200);
                Require(first.Kind == ActionKind.Switch && first.Slot == 2 && planner.RecoveringAfterBattle,
                    fighter + " must do its task-only handoff even with healer opener disabled");
                planner.Sent(first, 3200);
                Require(planner.Observe(hud, 3399).Kind == ActionKind.Wait && planner.RecoveringAfterBattle,
                    "wait for the 200ms retry");
                BattleAction retry = planner.Observe(hud, 3400);
                Require(retry.Kind == ActionKind.Switch && retry.Slot == 2, "continue until the original slot changes");
                planner.Sent(retry, 3400);
                hud.Current = 1;
                Require(planner.Observe(hud, 3600).Kind == ActionKind.Wait && !planner.RecoveringAfterBattle,
                    "switch_other_char confirms departure, not only the requested target");
            }
            var check = new CombatPostFightCheck();
            Require(!check.Observe(true, true, true, 1000), "the first handoff frame is not success");
            Require(!check.Observe(true, true, true, 3200), "active recovery delays the world settle clock");
            Require(!check.Observe(true, false, true, 3400) && !check.Observe(true, false, true, 3899),
                "allow 500ms after recovery");
            Require(check.Observe(true, false, true, 3900), "settled team HUD completes the task");
            check.Observe(false, false, false, 4000);
            Require(!check.Active && !check.Expired(50000), "reappearing enemies reset the completion state");
            check.Observe(true, false, false, 5000);
            Require(!check.Expired(14999) && check.Expired(15000), "unverified HUD has a bounded ten-second grace");
        }

        private static void SettingsRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "TideCombat-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var settings = CombatSettings.Defaults();
                Require(settings.CheckLevitator, "reference compatibility checkbox defaults to on");
                settings.Characters.Add(new CharacterProfile { Name = "治疗角色", Portrait = new byte[PortraitReader.SampleLength],
                    Rotation = Rotation.ForRole(BattleRole.Support) });
                settings.Domain.Kind = DomainKind.Forgery;
                settings.Domain.Index = 18;
                settings.Nightmare.TacetNest = false;
                settings.Nightmare.CaptureOnce = true;
                settings.Change.Target = "暴击伤害";
                settings.Cultivation.Nightmare = false;
                settings.JumpKey = "G";
                settings.DodgeKey = "ALT";
                settings.WheelKey = "F3";
                SettingsFile.Save(path, settings);
                settings.AutoTarget = false;
                settings.CheckLevitator = false;
                SettingsFile.Save(path, settings);
                CombatSettings saved = SettingsFile.Load(path);
                Require(!saved.AutoTarget && !saved.CheckLevitator && saved.Characters[0].Name == "治疗角色", "independent settings were not persisted");
                Require(saved.Domain.Kind == DomainKind.Forgery && saved.Domain.Index == 18 &&
                    !saved.Nightmare.TacetNest && saved.Nightmare.CaptureOnce && saved.Change.Target == "暴击伤害" &&
                    !saved.Cultivation.Nightmare,
                    "validation must retain domain selection, nightmare mode, and main-stat target");
                Require(saved.JumpVirtualKey() == 'G' && saved.DodgeVirtualKey() == 0x12 && saved.WheelVirtualKey() == 0x72 &&
                    CombatSettings.Defaults().JumpVirtualKey() == 0x20 && CombatSettings.Defaults().DodgeVirtualKey() == 0xa0 &&
                    CombatSettings.Defaults().WheelVirtualKey() == 0x09, "movement hotkeys persist and resolve the reference defaults");
                saved.EchoKey = saved.LiberationKey;
                bool denied = false;
                try { SettingsFile.Save(path, saved); }
                catch (InvalidDataException) { denied = true; }
                Require(denied && SettingsFile.Load(path).EchoKey != saved.EchoKey, "invalid config overwrote the saved config");
                string legacy = File.ReadAllText(path);
                string withoutField = System.Text.RegularExpressions.Regex.Replace(legacy,
                    "\\\"CheckLevitator\\\"\\s*:\\s*false\\s*,", string.Empty);
                Require(withoutField != legacy, "compatibility option was missing from the saved fixture");
                File.WriteAllText(path, withoutField);
                Require(SettingsFile.Load(path).CheckLevitator, "legacy configurations without the option keep the reference default");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void MissingWindow()
        {
            var output = new WindowInput(new GameWindow());
            Require(!output.Send(BattleAction.Attack(), CancellationToken.None), "unexpected input without a verified game HWND");
        }

        private static void LocalWindowIdentity()
        {
            Require(GameWindow.MatchesLocalGame("UnrealWindow", "Client-Win64-Shipping"), "local shipping window rejected");
            Require(!GameWindow.MatchesLocalGame("UnrealWindow", "AnotherGame") &&
                !GameWindow.MatchesLocalGame("#32770", "Client-Win64-Shipping"), "non-game HWND accepted");
        }

        private static void InputPrivilegePreflight()
        {
            Require(InputIntegrity.IsBlocked(0x2000, 0x3000), "medium input must not proceed into a high-integrity window");
            Require(!InputIntegrity.IsBlocked(0x3000, 0x3000) && !InputIntegrity.IsBlocked(0x3000, 0x2000),
                "administrator input should not be blocked by an equal or lower target");
            Require(!InputIntegrity.IsBlocked(0x2000, 0), "an unread token is not evidence of mismatched permissions");
            using (Process self = Process.GetCurrentProcess())
            {
                Require(InputIntegrity.GetProcessLevel((uint)self.Id) > 0, "native self-token could not be read");
                Require(!InputIntegrity.IsBlocked((uint)self.Id), "equal native tokens should not trigger elevation");
            }
        }

        private static void TaskResumeRouting()
        {
            MainForm.WorkMode mode;
            Require(Program.TryReadResume(new[] { Program.ResumeTask, "Domain" }, out mode) &&
                mode == MainForm.WorkMode.Domain, "restart lost the selected domain task");
            Require(Program.TryReadResume(new[] { Program.ResumeTask, "Nightmare" }, out mode) &&
                mode == MainForm.WorkMode.Nightmare, "restart lost the selected nightmare task");
            Require(Program.TryReadResume(new[] { Program.ResumeBattle }, out mode) &&
                mode == MainForm.WorkMode.Battle, "legacy battle restart no longer works");
            Require(!Program.TryReadResume(new[] { Program.ResumeTask }, out mode) &&
                !Program.TryReadResume(new[] { Program.ResumeTask, "Preview" }, out mode) &&
                !Program.TryReadResume(new[] { Program.ResumeTask, "999" }, out mode),
                "restart accepted an invalid task");
        }

        private static void DailyCommandLineRouting()
        {
            bool exitAfter;
            string error;
            MainForm.WorkMode mode;
            DomainKind? domain;
            Require(Program.TryReadTaskStart(new[] { "-t", "DailyTask", "-e" }, out mode, out domain, out exitAfter, out error) &&
                mode == MainForm.WorkMode.Daily && domain == null && exitAfter && error == null, "documented daily launch was rejected");
            Require(Program.TryReadTaskStart(new[] { "--exit", "--task", "src.task.DailyTask.DailyTask" },
                out mode, out domain, out exitAfter, out error) && exitAfter && error == null, "scheduler’s stable task name was rejected");
            Require(Program.TryReadTaskStart(new[] { "--task=📅 Daily Task" }, out mode, out domain, out exitAfter, out error) &&
                !exitAfter && error == null, "the daily display name was rejected or inherited a previous exit flag");
            Require(Program.TryReadTaskStart(new[] { Program.ResumeDaily, "--exit" }, out mode, out domain, out exitAfter, out error) &&
                mode == MainForm.WorkMode.Daily && exitAfter && error == null, "administrator restart lost the daily exit request");
            Require(Program.TryReadTaskStart(new[] { Program.ResumeCommandTask, "-t", "5", "-e" }, out mode, out domain,
                out exitAfter, out error) && mode == MainForm.WorkMode.Domain && domain == DomainKind.Forgery && exitAfter &&
                error == null, "administrator restart lost the selected domain kind or exit request");
            Require(!Program.TryReadTaskStart(new string[0], out mode, out domain, out exitAfter, out error) && error == null,
                "normal GUI launch must not require a task");

            var names = new[] { "DailyTask", "FarmEchoTask", "NightmareNestTask", "TacetTask", "ForgeryTask", "SimulationTask",
                "MultiAccountDailyTask", "MultiAccountConfigTask", "MergeEchoTask", "EnhanceEchoTask", "ChangeEchoTask", "GardenTask" };
            for (int i = 0; i < names.Length; i++)
            {
                Require(Program.TryReadTaskStart(new[] { "-t", (i + 1).ToString() }, out mode, out domain,
                    out exitAfter, out error) && Program.ReferenceTaskNumber(mode, domain) == i + 1 && error == null,
                    "reference registration number did not route correctly: " + (i + 1));
                MainForm.WorkMode numberMode = mode;
                DomainKind? numberDomain = domain;
                Require(Program.TryReadTaskStart(new[] { "--task=" + names[i] }, out mode, out domain, out exitAfter, out error) &&
                    mode == numberMode && domain == numberDomain && error == null, "registration name and number disagree: " + names[i]);
            }

            var names3814 = new[] { "DailyTask", "GalGameTask", "RahaiBlocksTask", "FarmEchoTask", "NightmareNestTask",
                "TacetTask", "ForgeryTask", "SimulationTask", "CultivationTask", "MultiAccountDailyTask",
                "MultiAccountConfigTask", "MergeEchoTask", "EnhanceEchoTask", "ChangeEchoTask", "GardenTask" };
            for (int i = 0; i < names3814.Length; i++)
            {
                Require(Program.TryReadTaskStart(new[] { "-t", (i + 1).ToString(), "--task-version=3.8.14" },
                    out mode, out domain, out exitAfter, out error) && error == null &&
                    Program.ReferenceTaskName(mode, domain) == names3814[i], "new snapshot numeric order: " + (i + 1));
                MainForm.WorkMode newMode = mode;
                DomainKind? newDomain = domain;
                Require(Program.TryReadTaskStart(new[] { Program.ResumeCommandTask, "--task", "src.task." + names3814[i] + "." + names3814[i], "-e" },
                    out mode, out domain, out exitAfter, out error) && error == null && exitAfter &&
                    mode == newMode && domain == newDomain, "stable named restart differs from version-specific numeric command");
            }
            Require(Program.TryReadTaskStart(new[] { "--task-version", "3.8.4", "-t", "2" },
                out mode, out domain, out exitAfter, out error) && mode == MainForm.WorkMode.Farm && error == null,
                "explicit old number must not be silently repurposed");
            Require(Program.TryReadTaskStart(new[] { "-t", "GalGameTask" }, out mode, out domain, out exitAfter, out error) &&
                mode == MainForm.WorkMode.SecondSola && !exitAfter && error == null, "named Second Sola entry");
            Require(Program.TryReadTaskStart(new[] { "-t", "RahaiBlocksTask" }, out mode, out domain, out exitAfter, out error) &&
                mode == MainForm.WorkMode.Rahai && !exitAfter && error == null, "named Rahai entry");
            Require(MainForm.SupportsRahaiSource(GameSource.AutoLocal) && MainForm.SupportsRahaiSource(GameSource.Foreground) &&
                !MainForm.SupportsRahaiSource(GameSource.Background) && !MainForm.SupportsRahaiSource(GameSource.Cloud) &&
                !MainForm.SupportsRahaiSource(GameSource.ManagedCloud), "CLI must preserve the activity input route restriction");

            foreach (string[] invalid in new[]
            {
                new[] { "-e" },
                new[] { "-t" },
                new[] { "-t", "--exit" },
                new[] { "-t", "0" }, new[] { "-t", "13" }, new[] { "-t", "AutoCombatTask" },
                new[] { "-t", "other.package.DailyTask" },
                new[] { "--task-version", "3.8.14" },
                new[] { "--task-version", "3.8.99", "-t", "2" },
                new[] { "--task-version=3.8.14", "-t", "16" },
                new[] { "--task-version=3.8.4", "--task-version=3.8.14", "-t", "2" },
                new[] { "-t", "DailyTask", "--task=DailyTask" },
                new[] { "-t", "DailyTask", "--typo" }
            })
                Require(!Program.TryReadTaskStart(invalid, out mode, out domain, out exitAfter, out error) && error != null,
                    "invalid CLI unexpectedly started a task or normal GUI: " + string.Join(" ", invalid));
        }

        private static void DailyCommandLineExit()
        {
            var settings = CombatSettings.Defaults();
            settings.Daily.ExitAfterTask = false;
            Require(!MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.Daily, false, true) &&
                MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.Daily, true, true),
                "-e should opt a successful daily run into exit");
            Require(!MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.Daily, true, false) &&
                MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.MultiDaily, true, true) &&
                MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.MultiConfig, true, true) &&
                !MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.Battle, true, true),
                "-e should work for successful registered one-time tasks only");
            Require(!settings.Daily.ExitAfterTask, "-e mutated the saved daily setting");
            foreach (MainForm.WorkMode activity in new[] { MainForm.WorkMode.SecondSola, MainForm.WorkMode.Rahai })
                Require(MainForm.ShouldExitAfterTask(settings, activity, true, true) &&
                    !MainForm.ShouldExitAfterTask(settings, activity, true, false) &&
                    !MainForm.ShouldExitAfterTask(settings, activity, false, true),
                    "activity CLI exit applies only to an explicitly requested, verified success");
            settings.Daily.ExitAfterTask = true;
            Require(MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.Daily, false, true) &&
                !MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.MultiDaily, false, true) &&
                !MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.MultiConfig, false, true) &&
                !MainForm.ShouldExitAfterTask(settings, MainForm.WorkMode.Daily, true, false),
                "standalone daily saved exit does not leak into either multi-account mode");
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(CombatSettings));
            string oldJson;
            using (var memory = new MemoryStream())
            {
                serializer.WriteObject(memory, settings);
                oldJson = Encoding.UTF8.GetString(memory.ToArray()).Replace("\"MultiAccountExitAfterTask\":false,", "");
            }
            CombatSettings legacy;
            using (var memory = new MemoryStream(Encoding.UTF8.GetBytes(oldJson)))
                legacy = (CombatSettings)serializer.ReadObject(memory);
            Require(!legacy.MultiAccountExitAfterTask.HasValue, "fixture omits the new scope field");
            legacy.Validate();
            Require(legacy.ExitAfterMultiAccount, "a missing old multi-account exit setting inherits the previous daily preference once");
            legacy.Daily.ExitAfterTask = false;
            Require(!MainForm.ShouldExitAfterTask(legacy, MainForm.WorkMode.Daily, false, true) &&
                MainForm.ShouldExitAfterTask(legacy, MainForm.WorkMode.MultiDaily, false, true) &&
                MainForm.ShouldExitAfterTask(legacy, MainForm.WorkMode.MultiConfig, false, true) &&
                !MainForm.ShouldExitAfterTask(legacy, MainForm.WorkMode.MultiDaily, true, false) &&
                !MainForm.ShouldExitAfterTask(legacy, MainForm.WorkMode.MultiConfig, true, false),
                "migrated preference remains independent, and explicit CLI does not bypass completion");
            legacy.MultiAccountExitAfterTask = false;
            Require(!MainForm.ShouldExitAfterTask(legacy, MainForm.WorkMode.MultiDaily, false, true) &&
                MainForm.ShouldExitAfterTask(legacy, MainForm.WorkMode.MultiConfig, true, true) &&
                !legacy.MultiAccountExitAfterTask.Value, "CLI exit applies only to this run and never mutates the saved option");
        }

        private static void HealthRetargetWindows()
        {
            foreach (bool hasHealth in new[] { false, true })
            {
                var settings = CombatSettings.Defaults();
                settings.RotateParty = settings.SupportOpener = false;
                var planner = new BattlePlanner(settings);
                var hud = BattleHud();
                hud.Fighters[0] = "Chixia";
                hud.Locked = hud.EnemyHealth = false;
                planner.Observe(hud, 0);
                Require(SwitchRoster(planner)[0].Id == "Chixia" && !planner.Fighting,
                    "the party is recognized before the first combat target is available");
                hud.Locked = hud.EnemyHealth = true;
                planner.Observe(hud, 100);
                planner.Observe(hud, 200);
                Require(planner.Fighting, "two frames establish combat");
                hud.Locked = false;
                hud.EnemyHealth = hasHealth;
                int deadline = 300 + (hasHealth ? 30000 : 3000);
                BattleAction target = planner.Observe(hud, 300);
                Require(target.Kind == ActionKind.Target, "start reacquiring when the lock vanishes");
                planner.Sent(target, 300);
                Require(planner.Observe(hud, deadline - 1).Kind == ActionKind.Target && planner.Fighting,
                    "keep trying until the window closes");
                planner.Observe(hud, deadline);
                Require(!planner.Fighting, "close at the appropriate health-dependent deadline");
            }

            var cloudSettings = CombatSettings.Defaults();
            cloudSettings.AutoTarget = cloudSettings.RotateParty = cloudSettings.SupportOpener = false;
            var cloud = new BattlePlanner(cloudSettings, cloud: true);
            var cloudHud = BattleHud();
            cloudHud.EnemyHealth = false;
            cloud.Observe(cloudHud, 0);
            cloud.Observe(cloudHud, 100);
            Require(cloud.Fighting && cloud.Observe(cloudHud, 35000).Kind != ActionKind.Target && cloud.Fighting,
                "the cloud lock alone is sufficient to maintain combat");
        }

        private static void CloudGrace()
        {
            var settings = CustomSlotOptions();
            settings.RotateParty = false;
            var planner = new BattlePlanner(settings, true);
            var hud = BattleHud();
            planner.Observe(hud, 0);
            planner.Observe(hud, 100);
            hud.Locked = false;
            Require(planner.Observe(hud, 1000).Kind == ActionKind.Attack, "keep fighting with a visible cloud health bar");
            hud.EnemyHealth = false;
            Require(planner.Observe(hud, 1100).Kind == ActionKind.Attack, "keep fighting through a short stream dropout");
            BattleAction retry = planner.Observe(hud, 4600);
            Require(retry.Kind == ActionKind.Target, "after cloud grace, attempt reacquisition");
            planner.Sent(retry, 4600);
            Require(planner.Observe(hud, 5399).Kind == ActionKind.Wait && planner.Observe(hud, 5400).Kind == ActionKind.Target,
                "cloud retarget interval should be 800 ms");
            Require(planner.Observe(hud, 7600).Kind == ActionKind.Wait && !planner.Fighting, "cloud retarget must expire");
        }

        private static void CloudEntry()
        {
            foreach (bool automatic in new[] { false, true })
            {
                var settings = CustomSlotOptions();
                settings.AutoTarget = automatic;
                var planner = new BattlePlanner(settings, true);
                var hud = BattleHud();
                hud.Locked = false;
                Require(planner.Observe(hud, 0).Kind == ActionKind.Wait, "first HUD capture confirms the party");
                BattleAction second = planner.Observe(hud, 100);
                Require(second.Kind == (automatic ? ActionKind.Attack : ActionKind.Wait) && planner.Fighting == automatic,
                    "cloud health takes the place of a lock only for automatic entry");
            }
        }

        private static void CloudPageScope()
        {
            Require(CloudBrowser.IsOfficialPage(CloudBrowser.OfficialPage), "official cloud page was denied");
            foreach (string url in new[] { "https://kurogames.com/cloud/", "https://mc.kurogames.com.example.net/cloud/",
                "http://mc.kurogames.com/cloud/", "https://mc.kurogames.com:81/cloud/", "https://mc.kurogames.com/other" })
                Require(!CloudBrowser.IsOfficialPage(url), "unrelated browsing page was accepted");
        }

        private static void BrowserFixture(string fixture)
        {
            BrowserFixture(fixture, VerifyBrowserEvents);
        }

        private static void BrowserFixture(string fixture, Action<CloudBrowser, Func<bool>> verify)
        {
            string browser = CloudBrowser.FindBrowser();
            Require(browser != null, "Edge or Chrome is required for this isolated offline browser check");
            string path = Path.GetFullPath(fixture);
            string page = new Uri(path).AbsoluteUri;
            string profile = Path.Combine(Path.GetTempPath(), "TideCombat-offline-browser-" + Guid.NewGuid().ToString("N"));
            var start = new ProcessStartInfo(browser, CloudBrowser.LaunchArguments(profile, page, true))
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (Process process = Process.Start(start))
            using (var session = new CloudBrowser(profile, address => address == page))
            {
                try
                {
                    verify(session, () => !process.HasExited);
                }
                finally
                {
                    if (!process.HasExited) process.Kill();
                    process.WaitForExit(2000);
                    try { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static void VerifyBrowserEvents(CloudBrowser session, Func<bool> isRunning)
        {
            using (var time = new CancellationTokenSource(20000))
            {
                PixelFrame frame = default(PixelFrame);
                Size size;
                while (!time.IsCancellationRequested && isRunning() && !session.TryRead(time.Token, out frame, out size))
                    time.Token.WaitHandle.WaitOne(125);
                Require(frame.Width > 0 && CenterMatches(frame, 220, 35, 20), "fixture screenshot unavailable or unexpected");
                Require(session.Send(BattleAction.Press(BattleButton.Forward, 'W', true), time.Token), "hold key failed");
                Require(session.TryRead(time.Token, out frame, out size) && CenterMatches(frame, 17, 186, 38),
                    "browser did not see the key down event");
                Require(session.ReleaseAll(), "key release failed");
                Require(session.TryRead(time.Token, out frame, out size) && CenterMatches(frame, 220, 35, 20),
                    "browser did not see the key up event");
                Require(session.Send(BattleAction.Attack(), time.Token), "click failed");
                Require(session.TryRead(time.Token, out frame, out size) && CenterMatches(frame, 38, 60, 218),
                    "browser did not see the mouse event");
                Require(session.Scroll(1, time.Token), "wheel step failed");
                Require(session.TryRead(time.Token, out frame, out size) && CenterMatches(frame, 242, 148, 19),
                    "browser did not see the upward wheel event");
            }
        }

        private static bool CenterMatches(PixelFrame frame, int red, int green, int blue)
        {
            int r, g, b;
            frame.Rgb(frame.Width / 2, frame.Height / 2, out r, out g, out b);
            return Math.Abs(r - red) <= 8 && Math.Abs(g - green) <= 8 && Math.Abs(b - blue) <= 8;
        }

        private static void VerifyBrowserVideo(CloudBrowser session, Func<bool> isRunning)
        {
            using (var time = new CancellationTokenSource(20000))
            {
                PixelFrame frame = default(PixelFrame);
                Size size;
                Func<int, int, int, bool> awaitColor = (red, green, blue) =>
                {
                    while (!time.IsCancellationRequested && isRunning())
                    {
                        if (session.TryRead(time.Token, out frame, out size) && CenterMatches(frame, red, green, blue))
                            return true;
                        time.Token.WaitHandle.WaitOne(80);
                    }
                    return false;
                };
                Require(awaitColor(220, 35, 20), "decoded video should exclude the decorative blue DOM overlay");
                var sizeInFrame = new Size(frame.Width, frame.Height);
                Require(session.Click(new Point(frame.Width / 4, frame.Height / 4), sizeInFrame, time.Token),
                    "input should still map into the offset video");
                Require(awaitColor(0, 0, 255), "interactive covering element should switch back to the page screenshot");
                Require(session.Send(new BattleAction { Kind = ActionKind.Cast, Key = 'W' }, time.Token), "replace track input");
                Require(awaitColor(17, 186, 38), "the replacement track should be read without stale content");
                Require(session.Send(new BattleAction { Kind = ActionKind.Cast, Key = 'Q' }, time.Token), "stop track input");
                Require(awaitColor(0, 0, 255), "an ended track should switch back to the page screenshot");
                Require(session.Send(new BattleAction { Kind = ActionKind.Cast, Key = 'C' }, time.Token), "start canvas receiver input");
                Require(awaitColor(192, 62, 12), "a canvas renderer should read its one live receiver track");
                Require(session.Send(new BattleAction { Kind = ActionKind.Cast, Key = 'X' }, time.Token), "stop canvas receiver input");
                Require(awaitColor(24, 42, 208), "an ended canvas receiver should switch back to the page screenshot");
            }
        }

        private static void ChallengeOpening()
        {
            var settings = CustomSlotOptions();
            settings.UseLiberation = false;
            settings.RotateParty = false;
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            hud.Locked = hud.EnemyHealth = false;
            hud.OpenWorld = false;
            hud.ChallengePrompt = true;
            Require(planner.Observe(hud, 100).Kind == ActionKind.Wait, "await the player's challenge interaction");
            hud.ChallengePrompt = false;
            hud.Skills[(int)Skill.Liberation] = SkillState.Ready;
            planner.Observe(hud, 200);
            BattleAction action = planner.Observe(hud, 300);
            Require(action.Kind == ActionKind.Cast && action.Skill == Skill.Liberation,
                "the confirmed transition permits instance liberation before the timer appears");
        }

        private static void SwitchTransition()
        {
            var planner = new BattlePlanner(CombatSettings.Defaults());
            var hud = BattleHud();
            hud.Fighters[0] = "Camellya";
            hud.Fighters[1] = "Verina";
            planner.Observe(hud, 0);
            BattleAction swap = planner.Observe(hud, 100);
            Require(swap.Kind == ActionKind.Switch && swap.Slot == 1, "known healer opens the battle");
            planner.Sent(swap, 100);
            Require(planner.Observe(hud, 400).Kind == ActionKind.Switch, "retry while the original slot is still locked");
            planner.Sent(swap, 400);
            hud.Valid = false;
            Require(planner.Observe(hud, 2400).Kind == ActionKind.Wait && planner.Fighting,
                "hidden HUD must preserve the requested slot during the transition");
            hud.Valid = true;
            hud.Current = 1;
            Require(planner.Observe(hud, 2500).Kind == ActionKind.Wait, "one new-slot frame does not confirm the switch");
            BattleAction opening = planner.Observe(hud, 2600);
            Require(opening.Kind == ActionKind.Button && opening.Button == BattleButton.Attack,
                "the confirmed healer begins her opening attacks");
            planner.Sent(opening, 2600);
            Require(planner.Observe(hud, 2930).Kind != ActionKind.Switch, "confirmed slot clears the old request");
        }

        private static void RecheckConcerto()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            var hud = BattleHud();
            hud.Fighters[0] = "Chixia";
            hud.Fighters[1] = "Danjin";
            hud.Fighters[2] = "Camellya";
            hud.Concerto = .92;
            var first = new BattlePlanner(settings);
            first.Observe(hud, 0);
            Require(first.Observe(hud, 100).Kind == ActionKind.Wait, "near-full concerto should be sampled again");
            BattleAction swap = first.Observe(hud, 155);
            Require(swap.Kind == ActionKind.Switch && swap.Slot == 1 && swap.FollowWithAttack, "choose Danjin without intro");
            first.Sent(swap, 155);
            hud.ConcertoFull = true;
            swap = first.Observe(hud, 300);
            Require(swap.Kind == ActionKind.Switch && swap.Slot == 2 && swap.FollowWithAttack,
                "choose Camellya when concerto fills before the swap is confirmed");

            var beforeSend = new BattlePlanner(settings);
            hud.ConcertoFull = false;
            hud.BreakPrompt = true;
            beforeSend.Observe(hud, 0);
            Require(beforeSend.Observe(hud, 100).Kind == ActionKind.Wait, "no slot key before refreshing near-full concerto");
            hud.ConcertoFull = true;
            BattleAction execute = beforeSend.Observe(hud, 155);
            Require(execute.Kind == ActionKind.Button && execute.Button == BattleButton.Interact, "execute before the first confirmed intro key");
            beforeSend.Sent(execute, 155);
            hud.BreakPrompt = false;
            for (int now = 255; now <= 955; now += 100)
            {
                swap = beforeSend.Observe(hud, now);
                if (swap.Kind == ActionKind.Switch) break;
                beforeSend.Sent(swap, now);
            }
            Require(swap.Kind == ActionKind.Switch && swap.Slot == 2, "the first key must use the refreshed intro priority");

            var blocked = new BattlePlanner(settings);
            hud.Fighters[1] = "Zani";
            hud.Health[2] = 0;
            hud.ConcertoFull = false;
            blocked.Observe(hud, 0);
            Require(blocked.Observe(hud, 100).Kind == ActionKind.Wait, "wait before choosing the only teammate");
            SwitchRoster(blocked)[1].ZaniCrisisAt = 100;
            hud.ConcertoFull = true;
            Require(blocked.Observe(hud, 155).Kind == ActionKind.Attack, "do not send a key to a newly blocked target");
        }

        private static void SwitchExecutionOrder()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            foreach (bool intro in new[] { true, false })
            {
                var planner = new BattlePlanner(settings);
                var hud = BattleHud();
                hud.Fighters[0] = "Chixia";
                hud.Fighters[1] = "Danjin";
                hud.Health[2] = 0;
                hud.ConcertoFull = intro;
                hud.BreakPrompt = true;
                planner.Observe(hud, 0);
                BattleAction action = planner.Observe(hud, 100);
                if (intro)
                {
                    Require(action.Kind == ActionKind.Button && action.Button == BattleButton.Interact, "execute before an intro switch");
                    planner.Sent(action, 100);
                    hud.BreakPrompt = false;
                    for (int time = 200; time <= 700; time += 100)
                    {
                        action = planner.Observe(hud, time);
                        if (action.Kind == ActionKind.Switch) break;
                        planner.Sent(action, time);
                    }
                    Require(action.Kind == ActionKind.Switch, "send the slot after execution stops");
                }
                else
                {
                    Require(action.Kind == ActionKind.Switch, "switch first without intro");
                    planner.Sent(action, 100);
                    hud.BreakPrompt = false;
                    hud.Current = 1;
                    planner.Observe(hud, 200);
                    action = planner.Observe(hud, 300);
                    Require(action.Kind == ActionKind.Button && action.Button == BattleButton.Interact,
                        "defer execution until the new slot is confirmed when there was no intro");
                    FighterMemory leaving = SwitchRoster(planner)[0];
                    Require(leaving.LastExit < 0, "do not start the swap cooldown before the pending execution");
                    planner.Sent(action, 300);
                    for (int now = 400; now <= 1600 && leaving.LastExit < 0; now += 100)
                        planner.Sent(planner.Observe(hud, now), now);
                    Require(leaving.LastExit >= 300, "record the swap cooldown after execution has been sent");
                }
            }
        }

        private static FighterMemory[] SwitchRoster(BattlePlanner planner)
        {
            return (FighterMemory[])typeof(BattlePlanner).GetField("roster",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(planner);
        }

        private static void HealerSwitchLock()
        {
            foreach (string healerId in new[] { "Verina", "Chisa", null })
            foreach (bool concertoFull in new[] { true, false })
            {
                var settings = CombatSettings.Defaults();
                settings.RotateParty = false;
                var planner = new BattlePlanner(settings);
                var hud = BattleHud();
                if (healerId != null) hud.Fighters[0] = healerId;
                else hud.Profiles[0] = new CharacterProfile { Rotation = Rotation.ForRole(BattleRole.Support) };
                hud.Fighters[1] = "Chixia";
                hud.ConcertoFull = concertoFull;
                planner.Observe(hud, 0);
                planner.Observe(hud, 100);
                hud.Current = 1;
                planner.Observe(hud, 200);
                planner.Observe(hud, 300);
                FighterMemory healer = SwitchRoster(planner)[0];
                Require(healer.LastExit >= 200, "the new slot must be confirmed");
                Require(healer.LastFullHealerSwitch == (concertoFull ? healer.LastExit : -1),
                    "only a full concerto switch records the healer lockout");
                var remaining = typeof(BattlePlanner).GetMethod("HealerLockRemaining", BindingFlags.NonPublic | BindingFlags.Instance);
                if (concertoFull)
                {
                    Require(healer.BuffDuration == (healerId == "Chisa" ? 20000 : 28000) &&
                        healer.BuffStarted == healer.LastExit, "healer outro buff");
                    Require((long)remaining.Invoke(planner, new object[] { 0, healer.LastExit + 15999 }) == 1 &&
                        (long)remaining.Invoke(planner, new object[] { 0, healer.LastExit + 16000 }) == 0, "sixteen-second boundary");
                }
                else Require(healer.BuffStarted == -1, "a switch without full concerto does not register an outro buff");
            }
        }

        private static void SwitchSelectionRules()
        {
            const long now = 50000;
            var planner = new BattlePlanner(CustomSlotOptions());
            var hud = BattleHud();
            var roster = SwitchRoster(planner);
            var next = typeof(BattlePlanner).GetMethod("NextSlot", BindingFlags.NonPublic | BindingFlags.Instance);
            var active = typeof(BattlePlanner).GetField("activeSlot", BindingFlags.Instance | BindingFlags.NonPublic);
            Func<int> choose = () => (int)next.Invoke(planner, new object[] { hud, now });
            Action<int, string> character = (slot, id) => { roster[slot].Id = id; hud.Fighters[slot] = id; };
            active.SetValue(planner, 0);
            character(0, "Chixia");
            character(1, "Danjin");
            character(2, "Verina");
            roster[1].BuffDuration = 14000;
            roster[2].BuffDuration = 28000;
            roster[1].BuffStarted = now - 1000;
            roster[2].BuffStarted = now - 15900;
            Require(choose() == 2, "main DPS chooses the buffer with the shorter remaining buff");
            roster[2].LastExit = now - 1000;
            Require(choose() == 1, "an equally eligible candidate in switch cooldown is skipped");
            roster[2].LastExit = now - 1001;
            Require(choose() == 2, "a cooldown expires after one second");

            roster[1].BuffStarted = roster[2].BuffStarted = -1;
            hud.ConcertoFull = true;
            Require(choose() == 2, "intro selects an unbuffed healer first");
            roster[2].BuffStarted = now;
            Require(choose() == 1, "intro selects the unbuffed sub DPS next");
            character(1, "Jinhsi");
            Require(choose() == 1, "a required character takes priority");
            hud.ConcertoFull = false;
            Require(choose() == 2, "Jinhsi outside an active phase is blocked without intro");

            character(0, "Aemeath");
            character(1, "Denia");
            character(2, "Mornye");
            roster[1].BuffStarted = -1;
            roster[2].LastFullHealerSwitch = now - 15999;
            hud.ConcertoFull = true;
            Require(choose() == 1, "dynamic healer lockout overrides Mornye's required priority");
            roster[1].BuffStarted = now;
            Require(choose() == 1, "buffed Denia can accept intro when the healer is locked");
            roster[2].LastFullHealerSwitch--;
            Require(choose() == 2, "Mornye regains priority at sixteen seconds");
            character(0, "Qingxiao");
            roster[2].LastFullHealerSwitch = now;
            roster[1].BuffStarted = -1;
            Require(choose() == 1, "Qingxiao also chooses Denia while the healer is locked");

            character(0, "Danjin");
            character(1, "Chixia");
            character(2, "Verina");
            roster[2].LastFullHealerSwitch = -1;
            roster[2].BuffStarted = -1;
            roster[1].LastExit = roster[2].LastExit = -1;
            Require(choose() == 2, "intro from sub DPS chains to an unbuffed healer");
            roster[0].LastTurn = now;
            hud.ConcertoFull = false;
            Require(choose() == 2, "non-intro sub DPS also chains to an unbuffed healer");
            roster[2].BuffStarted = now;
            Require(choose() == 1, "return to main DPS once supports are buffed");
            roster[2].BuffStarted = -1;
            roster[2].LastExit = now;
            Require(choose() == 1, "an unbuffed support in cooldown cannot preempt a ready main DPS");
            character(0, "Verina");
            character(1, "ShoreKeeper");
            character(2, "Chixia");
            roster[2].LastExit = -1;
            Require(choose() == 1, "ordinary battle may switch from one unbuffed healer to another");
            planner.PreferMainAfterHealer = true;
            Require(choose() == 2, "4C returns to main DPS when the other live slot is a healer");
            character(1, "Danjin");
            Require(choose() == 1, "4C still allows an unbuffed sub DPS after its healer");
            planner.PreferMainAfterHealer = false;
            hud.ConcertoFull = true;
            roster[2].LastExit = now;
            Require(choose() == 1, "intro from a healer selects the unbuffed sub DPS before main DPS");
            hud.ConcertoFull = false;
            character(0, "Ciaccona");
            roster[0].InRecital = true;
            roster[0].LastLiberation = now;
            roster[1].LastExit = now;
            hud.Health[2] = 0;
            Require(choose() == 1, "Ciaccona can still switch to the only living teammate during its cooldown");
            roster[0].InRecital = false;

            character(0, "Chixia");
            character(1, "Suisui");
            var priority = typeof(BattlePlanner).GetMethod("SwitchingPriority", BindingFlags.NonPublic | BindingFlags.Instance);
            Func<bool, int> rank = intro => (int)priority.Invoke(planner, new object[] { hud, 1, now, intro });
            roster[1].LastFullHealerSwitch = now - 15900;
            Require(rank(true) == 0, "Suisui's forte lockout");
            roster[1].LastFullHealerSwitch = now - 16000;
            Require(rank(true) == 400 && rank(false) == 200, "Suisui prioritizes intro from main DPS after her lockout");
            character(0, "Danjin");
            Require(rank(true) == 200, "intro from a sub DPS does not force Suisui");
            roster[1].LastFullHealerSwitch = now - 40000;
            Require(rank(false) == 200, "idle Suisui at exactly forty seconds");
            roster[1].LastFullHealerSwitch--;
            Require(rank(false) == 400, "idle Suisui above forty seconds");
            typeof(BattlePlanner).GetMethod("FinishBattle", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(planner, new object[] { now });
            Require(roster[1].LastFullHealerSwitch == -1, "end of battle resets an off-field Suisui");

            active.SetValue(planner,0);
            hud.Health[2] = 1;
            character(0, "Chixia");
            character(1, "Jinhsi");
            character(2, "Encore");
            roster[1].Phase = 1;
            roster[1].LastExit = now;
            roster[2].LastHeavy = now - 2000;
            hud.ConcertoFull = false;
            Require(choose() == 1, "a mandatory ready character overrides cooldown and an explicit prohibition");
            var makeRoom = typeof(BattlePlanner).GetMethod("MustMakeRoomForTeammate", BindingFlags.NonPublic | BindingFlags.Instance);
            Require((bool)makeRoom.Invoke(planner, new object[] { hud, now }),
                "the current rotation can hand off early when an actual teammate requires the slot");
            roster[1].Phase = 0;
            Require(choose() == -1 && !(bool)makeRoom.Invoke(planner, new object[] { hud, now }),
                "when all candidates prohibit entry, do not switch or shorten the turn");
            hud.ConcertoFull = true;
            Require(choose() == 1, "an intro makes Jinhsi mandatory over another blocked target");
            character(1, "Phrolova");
            roster[1].LastLiberation = now - 5000;
            Require(choose() == -1, "the intro does not override Phrolova's active Hecate lock");
            character(2, "Denia");
            roster[2].BuffStarted = now;
            var rank2 = (int)priority.Invoke(planner, new object[] { hud, 2, now, true });
            Require(rank2 == 1 && choose() == 2, "a one-point deadlock fallback beats a prohibited slot");
            character(1, "Danjin");
            Require(choose() == 1, "ordinary priority beats the one-point fallback");
        }

        private static void CharacterPreferences()
        {
            var settings = CombatSettings.Defaults();
            Require(CharacterRoster.Unrecognized.Role == BattleRole.Main &&
                CharacterRoster.Unrecognized.BuffMilliseconds == CharacterKit.DefaultBuffDuration(BattleRole.Main) &&
                CharacterKit.DefaultBuffDuration(BattleRole.Support) == 28000, "default roles and buff lifetimes");
            foreach (var item in new[] {
                Tuple.Create("Aalto", BattleRole.Secondary, ConcertoColor.Aero),
                Tuple.Create("Lingyang", BattleRole.Main, ConcertoColor.Glacio),
                Tuple.Create("Lumi", BattleRole.Secondary, ConcertoColor.Electro),
                Tuple.Create("Yangyang", BattleRole.Secondary, ConcertoColor.Aero),
                Tuple.Create("Mortefi", BattleRole.Secondary, ConcertoColor.Fusion),
                Tuple.Create("Linnai", BattleRole.Secondary, ConcertoColor.Spectro),
                Tuple.Create("Lucilla", BattleRole.Secondary, ConcertoColor.Glacio),
                Tuple.Create("Lucy", BattleRole.Main, ConcertoColor.Spectro),
                Tuple.Create("Rebecca", BattleRole.Secondary, ConcertoColor.Electro),
                Tuple.Create("Suisui", BattleRole.Support, ConcertoColor.Unknown),
                Tuple.Create("YangYangSp", BattleRole.Main, ConcertoColor.Havoc)
            })
                Require(CharacterRoster.Find(item.Item1).Role == item.Item2 && CharacterKit.Ring(item.Item1) == item.Item3,
                    "role or element for " + item.Item1);
            var chisa = CharacterRoster.Find("Chisa");
            var iuno = CharacterRoster.Find("Iuno");
            Require(CharacterRoster.FromPortrait("char_chisa2") == "Chisa" &&
                CharacterRoster.FromPortrait("char_linnai2") == "Linnai" &&
                chisa.RoleFor(settings) == BattleRole.Support && chisa.BuffDuration(settings) == 20000 &&
                iuno.RoleFor(settings) == BattleRole.Secondary &&
                iuno.BuffDuration(settings) == CharacterKit.DefaultBuffDuration(BattleRole.Secondary), "base variants and portrait aliases");
            settings.ChisaDps = settings.IunoC6 = true;
            Require(chisa.RoleFor(settings) == BattleRole.Main && iuno.RoleFor(settings) == BattleRole.Main &&
                chisa.BuffDuration(settings) == CharacterKit.DefaultBuffDuration(BattleRole.Main) &&
                iuno.BuffDuration(settings) == CharacterKit.DefaultBuffDuration(BattleRole.Main), "DPS preferences change the effective roles");
            settings.IunoC6 = false;
            Require(iuno.RoleFor(settings) == BattleRole.Secondary, "removing C6 restores the normal role");
        }

        private static void SwitchPriorityOffsets()
        {
            int[] priorities = { BattlePlanner.SwitchBlocked, BattlePlanner.SwitchLow, BattlePlanner.SwitchNormal,
                BattlePlanner.SwitchHigh, BattlePlanner.SwitchRequired };
            Require(priorities.SequenceEqual(new[] { 0, 100, 200, 300, 400 }), "integer bands");
            foreach (int boosted in new[] { BattlePlanner.SwitchHigh + 1, BattlePlanner.SwitchBlocked })
            {
                var eligible = new List<int>();
                int top = int.MinValue;
                BattlePlanner.AddPriorityCandidate(eligible, ref top, 0, BattlePlanner.SwitchHigh);
                BattlePlanner.AddPriorityCandidate(eligible, ref top, 1, boosted);
                BattlePlanner.AddPriorityCandidate(eligible, ref top, 2, BattlePlanner.SwitchLow - 1);
                Require(eligible.SequenceEqual(new[] { boosted > BattlePlanner.SwitchHigh ? 1 : 0 }),
                    "honor the in-band adjustment and ignore a prohibited candidate");
            }
        }

        private static void PartyRecognitionReports()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ww-party-" + Guid.NewGuid().ToString("N"));
            try
            {
                Require(RunLog.Start(directory), "capture native roster log");
                var settings = CombatSettings.Defaults();
                settings.RotateParty = settings.SupportOpener = false;
                var planner = new BattlePlanner(settings);
                var hud = BattleHud();
                hud.Fighters[0] = "Chixia";
                hud.Fighters[1] = "Danjin";
                hud.Fighters[2] = "Verina";
                planner.Observe(hud, 0);
                planner.Observe(hud, 100);
                hud.Fighters[1] = null;
                planner.Observe(hud, 200);
                Require(SwitchRoster(planner)[1].Id == "Danjin", "one unreadable portrait does not invalidate a roster");
                hud.Fighters[1] = "ShoreKeeper";
                planner.Observe(hud, 300);
                planner.Observe(hud, 400);
                planner.Observe(new HudSnapshot(), 2300);
                Require(!planner.Fighting && SwitchRoster(planner).All(actor => actor.Id == null),
                    "leaving the world expires battle and invalidates all character slots");
                RunLog.Stop();
                string[] parties = Directory.GetFiles(directory,"*.log").SelectMany(File.ReadLines)
                    .Where(line => line.Contains("队伍识别 · ")).ToArray();
                Require(parties.Length == 2 && parties[0].Contains("丹瑾") && parties[1].Contains("守岸人"),
                    "one report per roster, with the new name only on the changed team");
            }
            finally
            {
                RunLog.Stop();
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.GetFiles(directory,"*.log")) File.Delete(file);
                    Directory.Delete(directory);
                }
            }
        }

        private static void SwitchTimeout()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            foreach (int limit in new[] { 3000, 5000 })
            {
                var planner = new BattlePlanner(settings) { SwitchTimeoutMs = limit };
                var hud = BattleHud();
                hud.Fighters[0] = "Chixia";
                hud.Fighters[1] = "Danjin";
                hud.Health[2] = 0;
                planner.Observe(hud, 0);
                BattleAction swap = planner.Observe(hud, 100);
                Require(swap.Kind == ActionKind.Switch && swap.FollowWithAttack, "begin a known-character rotation switch");
                planner.Sent(swap, 100);
                hud.Valid = false;
                hud.TeamHudHidden = true;
                Require(planner.Observe(hud, 100 + limit - 1).Kind == ActionKind.Wait && planner.Fighting, "allow the HUD to return until the deadline");
                Require(planner.Observe(hud, 100 + limit).Kind == ActionKind.Wait && !planner.Fighting, "abort an unconfirmed switch at the deadline");
            }
        }

        private static void VisiblePartySwitchTimeout()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            foreach (bool skillsVisible in new[] { false, true })
            {
                var planner = new BattlePlanner(settings);
                var hud = BattleHud();
                hud.PartySize = 2;
                hud.Fighters[0] = "Chixia";
                hud.Fighters[1] = "Danjin";
                planner.Observe(hud, 0);
                BattleAction swap = planner.Observe(hud, 100);
                Require(swap.Kind == ActionKind.Switch && swap.FollowWithAttack, "begin a routine switch");
                planner.Sent(swap, 100);

                hud.Valid = skillsVisible;
                ActionKind retry = skillsVisible ? ActionKind.Switch : ActionKind.Wait;
                Require(planner.Observe(hud, 5100).Kind == retry && planner.Fighting,
                    "the shorter missing-party timeout does not apply to visible party slots");
                Require(planner.Observe(hud, 10099).Kind == retry && planner.Fighting,
                    "keep confirming the eligible slot until the visible-party deadline");
                Require(planner.Observe(hud, 10100).Kind == ActionKind.Wait && !planner.Fighting,
                    "clear the unconfirmed switch after ten seconds");
            }
        }

        private static void PhrolovaHandoff()
        {
            const long now = 50000;
            var settings = CustomSlotOptions();
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            FighterMemory[] roster = SwitchRoster(planner);
            Type type = typeof(BattlePlanner);
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo active = type.GetField("activeSlot", members);
            MethodInfo next = type.GetMethod("NextSlot", members);
            MethodInfo routine = type.GetMethod("RoutineSwitch", members);
            var clock = (FreezeClock)type.GetField("clock", members).GetValue(planner);
            Action<int, string> fighter = (slot, id) => { roster[slot].Id = id; hud.Fighters[slot] = id; };
            Func<int> choose = () => (int)next.Invoke(planner, new object[] { hud, now });
            fighter(0, "Phrolova");
            fighter(1, "Cantarella");
            fighter(2, "ShoreKeeper");
            active.SetValue(planner, 0);
            roster[0].LastLiberation = now - 1000;
            roster[1].LastFullHealerSwitch = now - 8000;
            roster[2].LastFullHealerSwitch = now - 10000;
            hud.ConcertoFull = true;
            Require(choose() == 2, "when both healers are locked, choose the one closer to release");
            type.GetField("executionPending", members).SetValue(planner, true);
            BattleAction action = (BattleAction)routine.Invoke(planner, new object[] { hud, now, 2 });
            Require(action.Kind == ActionKind.Switch && action.Slot == 2,
                "Phrolova skips pre-switch execution during liberation");
            Require(Rotations.AllowsSwitchExecution(CharacterRoster.Find("Phrolova"), roster[0], settings, clock, now, true),
                "the new fighter may perform the deferred execution");
            roster[0].LastLiberation = -1;
            Require(Rotations.AllowsSwitchExecution(CharacterRoster.Find("Phrolova"), roster[0], settings, clock, now, false),
                "execution is available before the first liberation");
            roster[0].LastLiberation = now - 1000;
            foreach (string healer in new[] { "Cantarella", "ShoreKeeper", "Suisui", "Verina" })
                Require(Rotations.AllowsSwitchExecution(CharacterRoster.Find(healer), roster[1], settings, clock, now, false) ==
                    (healer == "Cantarella" || healer == "ShoreKeeper"), "healer execution policy " + healer);

            roster[1].LastFullHealerSwitch = now - 15000;
            roster[2].LastFullHealerSwitch = now - 17000;
            clock.Record(now - 5000, 3100, now);
            Require(choose() == 2, "animation freeze extends the lockout for both healers");
            active.SetValue(planner, 2);
            Require(choose() == 1, "a full concerto healer may hand off to the other locked healer");
            hud.ConcertoFull = false;
            Require(choose() == -1, "the current healer stays until concerto fills while Phrolova is locked");
            active.SetValue(planner, 0);
            hud.ConcertoFull = true;
            roster[1].LastFullHealerSwitch = -1;
            Require(choose() == 1, "an eligible healer is preferred over the lockout fallback");
            fighter(1, "Suisui");
            roster[1].LastFullHealerSwitch = now - 12000;
            Require(choose() == 2, "a blocked Suisui is excluded even from fallback");
            hud.Health[2] = 0;
            Require(choose() == -1, "no fallback overrides a character's explicit prohibition");

            clock.Clear();
            MethodInfo priority = type.GetMethod("SwitchingPriority", members);
            Func<int, bool, int> rank = (slot, intro) => (int)priority.Invoke(planner, new object[] { hud, slot, now, intro });
            fighter(1, "Mornye");
            fighter(2, "Linnai");
            hud.Health[2] = 1;
            roster[1].LastFullHealerSwitch = now - 8000;
            Require(rank(1, true) == 400 && choose() == 2,
                "a mandatory healer remains locked when Phrolova can hand off to an eligible teammate");
            active.SetValue(planner, 1);
            fighter(1, "Cantarella");
            fighter(2, "Ciaccona");
            roster[0].LastLiberation = now - 5000;
            Require(rank(0, false) == 0 && rank(0, true) == 0, "Phrolova stays off field during early Hecate");
            roster[0].LastLiberation = now - 15000;
            Require(rank(0, true) == 400 && rank(0, false) == 0, "Cantarella unlocks the intro-only fourteen-second exception");
            roster[0].LastLiberation = now - 25000;
            Require(rank(0, true) == 400 && rank(0, false) == 200, "ordinary Phrolova switch returns after Hecate");
            fighter(0, "Zani");
            roster[2].InRecital = true;
            roster[2].LastLiberation = now - 5000;
            Require(rank(2, false) == 0, "Ciaccona waits out a recital on Zani's team");
            roster[2].LastLiberation = now - 20000;
            Require(rank(2, false) == 200, "a recital stops blocking the slot at twenty seconds");
        }

        private static void PhrolovaDeferredExecution()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            hud.Fighters[0] = "Phrolova";
            hud.Fighters[1] = "ShoreKeeper";
            hud.Health[2] = 0;
            hud.Skills[2] = SkillState.Ready;
            long castAt = -1, switchAt = -1, executeAt = -1;
            for (long t = 0; t < 3000 && executeAt < 0; t += 50)
            {
                bool hidden = castAt >= 0 && t >= castAt + 50 && t < castAt + 400;
                BattleAction action = planner.Observe(hidden ? new HudSnapshot { TeamHudHidden = true } : hud, t);
                if (action.Kind == ActionKind.Wait) continue;
                planner.Sent(action,t);
                if (action.Kind == ActionKind.Button && action.Button == BattleButton.Liberation)
                {
                    Require(castAt < 0, "start the opening liberation once");
                    castAt = t;
                    hud.Skills[2] = SkillState.Cooling;
                    hud.Indicators.Add("f_break_full");
                }
                if (action.Kind == ActionKind.Switch)
                {
                    Require(castAt >= 0 && t >= castAt + 400 && action.Slot == 1 && switchAt < 0, "switch after confirming R");
                    switchAt = t;
                    hud.Current = 1;
                }
                if (action.Kind == ActionKind.Button && action.Button == BattleButton.Interact && !action.DuringAnimation)
                {
                    Require(switchAt >= 0 && t > switchAt && hud.Current == 1,
                        "only the teammate who arrives can perform the pending execution");
                    executeAt = t;
                }
            }
            Require(executeAt > switchAt && switchAt > castAt && castAt >= 0, "confirmed R -> switch -> execute");
        }

        private static void ShoreKeeperCombatGrace()
        {
            foreach (bool intro in new[] { false, true })
            {
                var settings = CombatSettings.Defaults();
                settings.RotateParty = settings.SupportOpener = false;
                var planner = new BattlePlanner(settings);
                var hud = BattleHud();
                hud.Fighters[0] = "ShoreKeeper";
                hud.Skills[0] = hud.Skills[1] = hud.Skills[2] = SkillState.Cooling;
                planner.Observe(hud, 0);
                planner.Observe(hud, 100);
                var context = typeof(BattlePlanner).GetField("entryIntro", BindingFlags.Instance | BindingFlags.NonPublic);
                context.SetValue(planner, intro);
                hud.Airborne = !intro;
                hud.Locked = hud.EnemyHealth = false;
                Require(planner.Observe(hud, 200).Kind != ActionKind.Target && planner.Fighting,
                    "the intro or air state must keep the turn from retargeting");
                planner.Invalidate(250);
                var turn = (CharacterTurn)typeof(BattlePlanner).GetField("turn",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(planner);
                Require(turn == null && planner.TakeReleaseRequest(), "invalidation must clear Shorekeeper's turn and its existing input");
            }
        }

        private static void PhrolovaExplicitExecution()
        {
            var settings = CombatSettings.Defaults();
            var actor = new FighterMemory { Id = "Phrolova", LastLiberation = 9000 };
            var clock = new FreezeClock();
            Require(!Rotations.AllowsSwitchExecution(CharacterRoster.Find(actor.Id), actor, settings, clock, 10000, false),
                "Phrolova's current liberation blocks only the automatic execution before switching");
            var context = new TurnContext(actor, new[] { actor }, settings, false, null, 10000,
                clock: clock, pendingExecution: () => true);
            using (var explicitAction = new CharacterTurn(Rotations.BreakEnemy(context), context))
            {
                BattleAction command = explicitAction.Observe(BattleHud(), 10000);
                Require(command.Kind == ActionKind.Button && command.Button == BattleButton.Interact &&
                    !command.DuringAnimation, "the ordinary explicit execution routine can still issue F");
            }
        }

        private static void ConfiguredHealerSwitch()
        {
            foreach (bool enabled in new[] { false, true })
            foreach (bool hasHealer in new[] { false, true })
            {
                var settings = CombatSettings.Defaults();
                settings.SupportOpener = !enabled;
                settings.Slots[0].FieldSeconds += .1;
                var planner = new BattlePlanner(settings, supportOpener: enabled);
                var hud = BattleHud();
                hud.Fighters[1] = "Danjin";
                hud.Fighters[2] = hasHealer ? "Verina" : "Jiyan";
                planner.Observe(hud, 0);
                BattleAction start = planner.Observe(hud, 100);
                bool expectHealer = enabled && hasHealer;
                Require((start.Kind == ActionKind.Switch && start.Slot == 2) == expectHealer, "task-specific opener uses a living healer");
                hud.Locked = hud.EnemyHealth = false;
                planner.Observe(hud, 200);
                BattleAction finish = planner.Observe(hud, 3300);
                Require((finish.Kind == ActionKind.Switch && finish.Slot == 2) == expectHealer, "task-specific cleanup uses the same healer setting");
            }
        }

        private static void BackgroundCoordinates()
        {
            var client = new Rectangle(112, 138, 1600, 900);
            var borders = new Rectangle(104, 106, 1616, 940);
            Require(GraphicsCapture.CropToClient(client, borders, borders.Size) == new Rectangle(8, 32, 1600, 900),
                "decorated window capture must crop to the client area");
            Require(GraphicsCapture.CropToClient(client, borders, client.Size).Location == Point.Empty,
                "borderless window capture starts at the capture origin");
            Require(GraphicsCapture.CropToClient(client, borders, new Size(1599, 900)).IsEmpty,
                "stale captures must not be cropped to invalid bounds");
            Require((uint)WindowMessages.KeyState(18, false) == 0x00120001 &&
                unchecked((uint)WindowMessages.KeyState(18, true)) == 0xc0120001, "key up/down state bits are incorrect");
        }

        private static void FreezeAdjustedDecisions()
        {
            var clock = new FreezeClock();
            clock.Record(1000, 900, 1000, true);
            clock.Record(3300, 80, 3380);
            clock.Record(4000, 2100, 6100);
            Require(clock.Elapsed(500, 7000) == 4500 && clock.Elapsed(500, 7000, true) == 3600,
                "intro freeze and 100 ms animation margin must use separate accounting");
            Require(clock.Elapsed(4000, 7000) == 3000, "an event does not lose the freeze that starts at its timestamp");
            var phrolova = new FighterMemory { LastLiberation = 500, BuffStarted = 500, BuffDuration = 14000 };
            CharacterKit kit = CharacterRoster.Find("Phrolova");
            var settings = CombatSettings.Defaults();
            Require(phrolova.BuffRemaining(clock, 15300) == 1200, "buff duration should use elapsed combat time");
            Require(!Rotations.AllowsSwitchExecution(kit, phrolova, settings, clock, 25500, false),
                "Hecate is still in liberation after subtracting animation freeze");
            Require(Rotations.AllowsSwitchExecution(kit, phrolova, settings, clock, 25500, true) &&
                Rotations.AllowsSwitchExecution(kit, phrolova, settings, clock, 26500, false),
                "the teammate can execute immediately and Phrolova can execute at 24 combat seconds");
            settings.SuomingDps = settings.ChisaDps = true;
            Require(CharacterRoster.Find("Suoming").BuffDuration(settings) == 14000 &&
                CharacterRoster.Find("Chisa").BuffDuration(settings) == 14000, "DPS variants must use their default buff time");
        }

        private static void AnimationFreezeObservation()
        {
            var actor = new FighterMemory { Id = "Hsin" };
            var context = new TurnContext(actor, new[] { actor }, CombatSettings.Defaults(), false, null, 500);
            var steps = new[] { Move.Liberation() };
            var hud = BattleHud();
            hud.Skills[(int)Skill.Liberation] = SkillState.Ready;
            using (var turn = new CharacterTurn(steps, context))
            {
                BattleAction cast = turn.Observe(hud, 1000);
                Require(cast.Kind == ActionKind.Button, "input did not initiate the skill animation");
                turn.Sent(cast, 1000);
                turn.ObserveUnavailable(1100, true);
                turn.ObserveUnavailable(1500, true);
                hud.Skills[(int)Skill.Liberation] = SkillState.Cooling;
                turn.Observe(hud, 1800);
                Require(turn.Finished && context.Succeeded && actor.LastLiberation == 1800,
                    "liberation must be recorded at the return to team HUD");
                Require(context.Clock.Elapsed(500, 1800) == 700, "700 ms animation minus 100 ms must be subtracted once");
            }
        }

        private static void AnimationKeyCadence()
        {
            var actor = new FighterMemory { Id = "Aalto" };
            var hud = BattleHud();
            hud.Skills[(int)Skill.Liberation] = SkillState.Ready;
            foreach (AnimationInput flags in new[] { AnimationInput.InteractAndAttack, AnimationInput.Interact, AnimationInput.None })
            {
                var options = CombatSettings.Defaults();
                var context = new TurnContext(actor, new[] { actor }, options, false, null, 1000);
                var sequence = new[] { Move.Liberation(animation: flags, animationDelay: 200) };
                hud.Skills[(int)Skill.Liberation] = SkillState.Ready;
                using (var turn = new CharacterTurn(sequence, context))
                {
                    BattleAction cast = turn.Observe(hud, 1000);
                    turn.Sent(cast, 1000);
                    hud.Skills[(int)Skill.Liberation] = SkillState.Cooling;
                    turn.ObserveAvailable(hud, 1050);
                    Require(turn.ObserveUnavailable(1075, false).Kind == ActionKind.Wait, "a capture failure is not an animation");
                    BattleAction first = turn.ObserveUnavailable(1100, true);
                    if (flags != AnimationInput.InteractAndAttack)
                    {
                        Require(first.Kind == ActionKind.Wait, "the no-click variant cannot attack while waiting for animation");
                        BattleAction afterDelay = turn.ObserveUnavailable(1350, true);
                        Require(flags == AnimationInput.None ? afterDelay.Kind == ActionKind.Wait :
                            afterDelay.DuringAnimation && afterDelay.Button == BattleButton.Interact && !afterDelay.AttackBefore,
                            "suppressing clicks does not change the independent F option");
                        continue;
                    }
                    Require(first.DuringAnimation && first.Button == BattleButton.Attack && !first.AttackBefore,
                        "the attack is allowed immediately, F is delayed");
                    turn.AnimationInputSent(first, 1100);
                    Require(turn.ObserveUnavailable(1199, true).Kind == ActionKind.Wait, "attack cadence must be 100 ms");
                    BattleAction click = turn.ObserveUnavailable(1250, true);
                    Require(click.Button == BattleButton.Attack && !click.AttackBefore, "no F before the minimum");
                    turn.AnimationInputSent(click, 1250);
                    BattleAction interact = turn.ObserveUnavailable(1300, true);
                    Require(interact.DuringAnimation && interact.Button == BattleButton.Interact && !interact.AttackBefore,
                        "F is ready even while normal attack waits for its own interval");
                    turn.AnimationInputSent(interact, 1300);
                    BattleAction combined = turn.ObserveUnavailable(1400, true);
                    Require(combined.DuringAnimation && combined.Button == BattleButton.Interact && combined.AttackBefore,
                        "both inputs are due together after 100 ms");
                    turn.ObserveAvailable(hud, 1500);
                    Require(turn.ObserveUnavailable(1520, true).Kind == ActionKind.Wait, "return of team HUD stops animation input");
                }
            }
        }

        private static void PlannerAnimationInputs()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            hud.Fighters[0] = "Hsin";
            hud.Indicators.Add("hsin_lib1");
            hud.Skills[(int)Skill.Liberation] = SkillState.Ready;
            planner.Observe(hud, 0);
            BattleAction cast = planner.Observe(hud, 100);
            Require(cast.Kind == ActionKind.Button && cast.Button == BattleButton.Liberation, "expected an opening liberation");
            planner.Sent(cast, 100);
            Require(planner.Observe(new HudSnapshot(), 140).Kind == ActionKind.Wait, "a missing frame must not emit input");
            var hidden = new HudSnapshot { TeamHudHidden = true };
            BattleAction pulse = planner.Observe(hidden, 200);
            Require(pulse.DuringAnimation && pulse.Button == BattleButton.Interact, "fresh loss of team HUD should send F");
            planner.Sent(pulse, 200);
            Require(planner.Observe(hidden, 299).Kind == ActionKind.Wait && planner.Observe(hidden, 300).DuringAnimation,
                "planner must debounce F without changing the pending skill");
            planner.Invalidate(350);
            Require(planner.Observe(hidden, 375).Kind == ActionKind.Wait, "loss of window must end animation input");
        }

        private static void LiberationRetryFlow()
        {
            var actor = new FighterMemory { Id = "Hsin" };
            var context = new TurnContext(actor, new[] { actor }, CombatSettings.Defaults(), false, null, 1000);
            var hud = BattleHud();
            hud.Skills[(int)Skill.Liberation] = SkillState.Ready;
            using (var turn = new CharacterTurn(new[] { Move.Liberation(0) }, context))
            {
                BattleAction first = turn.Observe(hud, 1000);
                Require(first.Kind == ActionKind.Button && first.Button == BattleButton.Liberation, "bright R should fire");
                turn.Sent(first, 1000);
                Require(turn.Observe(hud, 1099).Kind == ActionKind.Wait, "do not tap R faster than 100 ms");
                BattleAction retry = turn.Observe(hud, 1100);
                Require(retry.Kind == ActionKind.Button && retry.Button == BattleButton.Liberation, "bright R must be retried");
                turn.Sent(retry, 1100);
                hud.Skills[(int)Skill.Liberation] = SkillState.Cooling;
                Require(turn.Observe(hud, 1200).Kind == ActionKind.Wait && !turn.Finished, "dimming starts the animation wait");
                Require(turn.Observe(hud, 1599).Kind == ActionKind.Wait && !turn.Finished, "wait 400 ms after dimming");
                turn.Observe(hud, 1600);
                Require(turn.Finished && !context.Succeeded && actor.LastLiberation < 0,
                    "a disappearing icon alone cannot mark liberation as successful");
            }
        }

        private static void DimLiberationFlow()
        {
            foreach (SkillState state in new[] { SkillState.Cooling, SkillState.Unavailable })
            {
                var actor = new FighterMemory { Id = "RoverElectro" };
                var context = new TurnContext(actor, new[] { actor }, CombatSettings.Defaults(), false, null, 1000);
                var hud = BattleHud();
                hud.Skills[(int)Skill.Liberation] = state;
                var moves = new[] { Move.Liberation(animation: AnimationInput.None), Move.Tap(BattleButton.Resonance) };
                using (var turn = new CharacterTurn(moves, context))
                {
                    BattleAction first = turn.Observe(hud, 1000);
                    if (state == SkillState.Cooling)
                    {
                        Require(first.Kind == ActionKind.Button && first.Button == BattleButton.Resonance, "cooling R should be skipped");
                        continue;
                    }
                    Require(first.Kind == ActionKind.Button && first.Button == BattleButton.Liberation, "dim R gets one retry");
                    turn.Sent(first, 1000);
                    BattleAction next = turn.Observe(hud, 1100);
                    Require(next.Kind == ActionKind.Button && next.Button == BattleButton.Resonance,
                        "no-energy R must stop blocking E after the short probe");
                }
            }
        }

        private static void LiberationAttackFlow()
        {
            foreach (bool carlotta in new[] { false, true })
            {
                var memory = new FighterMemory { Id = carlotta ? "Carlotta" : "Aalto" };
                var context = new TurnContext(memory, new[] { memory }, CombatSettings.Defaults(), false, null, 1000);
                var hud = BattleHud();
                hud.Skills[2] = SkillState.Ready;
                Move move = carlotta ? Move.Liberation(0, readyTimeout: 400, attackWhileDimmed: true, raw: true) :
                    Move.Liberation(0, AnimationInput.InteractAndAttack);
                using (var turn = new CharacterTurn(new[] { move }, context))
                {
                    BattleAction cast = turn.Observe(hud, 1000);
                    Require(cast.Button == BattleButton.Liberation && cast.AttackBefore == !carlotta,
                        "only Aalto attacks during bright retries");
                    turn.Sent(cast, 1000);
                    hud.Skills[2] = SkillState.Cooling;
                    BattleAction waiting = turn.Observe(hud, 1100);
                    Require(waiting.DuringAnimation && waiting.Button == BattleButton.Attack, "both attack while waiting for the team HUD to hide");
                    turn.AnimationInputSent(waiting, 1100);
                    BattleAction hidden = turn.ObserveUnavailable(1200, true);
                    Require(hidden.Button == BattleButton.Interact && hidden.AttackBefore == !carlotta,
                        "F starts on entering animation and only Aalto continues attacking");
                }
            }
        }

        private static void ResonanceAnimationFlow()
        {
            foreach (bool clickF in new[] { false, true })
            {
                var actor = new FighterMemory { Id = "Aemeath" };
                var context = new TurnContext(actor, new[] { actor }, CombatSettings.Defaults(), false, null, 1000);
                var hud = BattleHud();
                hud.Skills[0] = SkillState.Unavailable;
                var skill = Move.Resonance(1500, hasAnimation: true, animationDelay: 500, clickF: clickF);
                using (var turn = new CharacterTurn(new[] { skill }, context))
                {
                    BattleAction first = turn.Observe(hud, 1000);
                    Require(first.Button == BattleButton.Resonance && !first.AttackBefore, "first E is sent without a normal attack");
                    turn.Sent(first, 1000);
                    Require(turn.Observe(hud, 1199).Kind == ActionKind.Wait, "animated E waits 200 ms before another try");
                    BattleAction attack = turn.Observe(hud, 1200);
                    Require(attack.Kind == ActionKind.Button && attack.Button == BattleButton.Attack,
                        "normal attack follows a prior E attempt in a separate observation");
                    turn.Sent(attack, 1200);
                    Require(turn.Observe(hud, 1200).Kind == ActionKind.Wait, "another E requires a fresh observation");
                    BattleAction retry = turn.Observe(hud, 1201);
                    Require(retry.Button == BattleButton.Resonance && !retry.AttackBefore, "still-ready E retries only after the attack sample");
                    turn.Sent(retry, 1201);
                    Require(turn.ObserveUnavailable(1350, true).Kind == ActionKind.Wait, "no input on entry into the animation");
                    Require(turn.ObserveUnavailable(1849, true).Kind == ActionKind.Wait, "wait until the full 500 ms inside animation");
                    BattleAction pulse = turn.ObserveUnavailable(1850, true);
                    Require(clickF ? pulse.Button == BattleButton.Interact && !pulse.AttackBefore : pulse.Kind == ActionKind.Wait,
                        "animated resonance sends F only when requested, without an attack");
                    if (clickF)
                    {
                        turn.AnimationInputSent(pulse, 1850);
                        Require(turn.ObserveUnavailable(1949, true).Kind == ActionKind.Wait, "F observes its interval");
                        pulse = turn.ObserveUnavailable(1950, true);
                        Require(pulse.DuringAnimation && pulse.Button == BattleButton.Interact && !pulse.AttackBefore,
                            "repeat F no faster than every 100 ms during animated E");
                    }
                    turn.Observe(hud, 2000);
                    Require(turn.Finished && context.Succeeded && context.AnimationSeen && actor.LastSkill[0] == 1000,
                        "the first cast is recorded, and the turn finishes on the return of the HUD");
                    Require(context.Clock.Elapsed(900, 2000) == 550, "record the 650 ms animation minus the 100 ms allowance");
                }
            }
        }

        private static void SkillCooldownBoundary()
        {
            Require(SkillHud.CooldownState(true, .21, true) == SkillState.Cooling &&
                SkillHud.CooldownState(true, .2, true) == SkillState.Ready &&
                SkillHud.CooldownState(true, .1, false) == SkillState.Unavailable,
                "only decoded residuals above 0.2 count as active cooldowns");
            Require(SkillHud.CooldownState(true, double.NaN, true) == SkillState.Cooling,
                "an unreadable cooldown still suppresses speculative input");
        }

        private static void LiberationAbort()
        {
            var settings = CombatSettings.Defaults();
            settings.RotateParty = settings.SupportOpener = false;
            var hud = BattleHud();
            hud.Fighters[0] = "Hsin";
            hud.Indicators.Add("hsin_lib1");
            hud.Skills[2] = SkillState.Ready;
            foreach (bool hidden in new[] { false, true })
            {
                var planner = CombatRunner.CreateTaskPlanner(settings, cloud: false);
                planner.Observe(hud, 0);
                BattleAction cast = planner.Observe(hud, 100);
                Require(cast.Button == BattleButton.Liberation, "open the common liberation helper");
                planner.Sent(cast, 100);
                if (hidden)
                {
                    var animation = new HudSnapshot { TeamHudHidden = true };
                    planner.Observe(animation, 200);
                    planner.Observe(animation, 7200);
                }
                else planner.Observe(hud, 15100);
                Require(planner.CombatAborted && !planner.Fighting && planner.TakeReleaseRequest(),
                    "expired common cast or animation exits and requests key release");
                Require(planner.Observe(hud, 15200).Kind == ActionKind.Wait && !planner.Fighting,
                    "the same task cannot reenter an aborted fight just because the lock remains");
                planner.Invalidate(15300);
                Require(!planner.CombatAborted, "next task clears its failure state");
            }
        }

        private static void RecoverShiftedTarget()
        {
            var planner = new BattlePlanner(CombatSettings.Defaults());
            var hud = BattleHud();
            hud.BearTargetShifted = true;
            Require(planner.Observe(hud, 1000).Kind == ActionKind.Wait, "first frame must only begin confirmation");
            hud.BearTargetShifted = false;
            Require(planner.Observe(hud, 1700).Button != BattleButton.Menu, "one shifted frame cannot open the menu");
            hud.BearTargetShifted = true;
            Require(planner.Observe(hud, 1800).Kind == ActionKind.Wait, "confirmation resets on a normal frame");
            Require(planner.Observe(hud, 2799).Kind == ActionKind.Wait, "wait a full second to confirm the shifted layout");
            BattleAction first = planner.Observe(hud, 2800);
            Require(first.Button == BattleButton.Menu && first.NativeKey == 0x1b, "open the menu on the verified window");
            planner.Sent(first, 2800);
            var menu = new HudSnapshot();
            Require(planner.Observe(menu, 4799).Kind == ActionKind.Wait, "menu remains open for two seconds");
            BattleAction second = planner.Observe(menu, 4800);
            Require(second.Button == BattleButton.Menu, "close the menu even while the team HUD is hidden");
            planner.Sent(second, 4800);
            Require(planner.Observe(hud, 6299).Kind == ActionKind.Wait, "give the layout 1.5 seconds to settle");
            Require(planner.Observe(hud, 6300).Button != BattleButton.Menu && planner.Observe(hud, 7400).Button != BattleButton.Menu,
                "do not repeat layout recovery within the same fight");
        }

        private static void ChallengeTimerCombat()
        {
            var settings = CustomSlotOptions();
            settings.AutoTarget = settings.RotateParty = false;
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            hud.OpenWorld = false;
            hud.ChallengeSeconds = 173.42;
            hud.Countdown = true;
            double seconds = 173.42;
            Func<double> read = () => seconds;
            planner.Observe(hud, 0);
            planner.Observe(hud, 100);
            hud.Locked = hud.EnemyHealth = false;
            Require(planner.Observe(hud, 200, read).Kind == ActionKind.Target && !hud.InChallenge,
                "ordinary task cannot enter Abyss mode from glyph evidence alone");
            planner = new BattlePlanner(settings);
            hud.ChallengePrompt = hud.ChallengeMarker = true;
            planner.Observe(hud, 0, read);
            hud.ChallengePrompt = hud.ChallengeMarker = false;
            planner.Observe(hud, 100, read);
            planner.Observe(hud, 200, read);
            Require(planner.Fighting, "confirmed Abyss marker may open a task fight");
            hud.EnemyHealth = hud.Locked = false;
            for (int now = 300; now <= 8200; now += 100)
            {
                BattleAction action = planner.Observe(hud, now, read);
                Require(action.Kind == ActionKind.Attack && planner.Fighting, "keep acting during a challenge with time left");
            }
            hud.Countdown = false;
            hud.ChallengeSeconds = 0;
            seconds = 0;
            Require(planner.Observe(hud, 8600, read).Kind == ActionKind.Target, "zero OCR ends the timer's combat allowance");
            planner.Observe(hud, 11700, read);
            Require(!planner.Fighting, "unresolved targeting then follows normal exit behavior");
        }

        private static void ChallengeClockFormat()
        {
            Require(Math.Abs(HudGlyphs.ParseChallengeClock("04:53:49") - 293.49) < 1e-8, "colon-delimited hundredths");
            Require(Math.Abs(HudGlyphs.ParseChallengeClock("04:53.4") - 293.4) < 1e-8, "dot-delimited tenths");
            Require(HudGlyphs.ParseChallengeClock("03:12") == 192, "minutes and seconds");
            Require(HudGlyphs.ParseChallengeClock("00:00:00") == 0, "expired timer");
            foreach (string invalid in new[] { "18476/18476", "12:99", "04.53.49", "4:05:", "123:12", "4:7" })
                Require(double.IsNaN(HudGlyphs.ParseChallengeClock(invalid)), "invalid timer " + invalid);
        }

        private static void ChallengeClockRead()
        {
            using (var image = new Bitmap(1600, 900, PixelFormat.Format32bppRgb))
            using (var reader = new FrameReader())
            using (Graphics graphics = Graphics.FromImage(image))
            using (var font = new Font("Arial", 20, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                foreach (string text in new[] { "2:53.42", "0:00", "2.53.42", "2:78" })
                {
                    graphics.Clear(Color.Black);
                    graphics.DrawString(text, font, Brushes.White, 770, 113, StringFormat.GenericTypographic);
                    double actual = HudGlyphs.ReadChallengeClock(reader.Read(image));
                    if (text == "2:53.42") Require(Math.Abs(actual - 173.42) < .00001, "read minutes, seconds and fractions: " + actual);
                    else if (text == "0:00") Require(actual == 0, "zero must parse as zero");
                    else Require(double.IsNaN(actual), "invalid countdown rejected");
                }
            }
        }

        private static void EnemyColorStrip()
        {
            using (var image = new Bitmap(1600, 900, PixelFormat.Format32bppRgb))
            using (var reader = new FrameReader())
            using (Graphics graphics = Graphics.FromImage(image))
            using (var fill = new SolidBrush(Color.FromArgb(207, 75, 60)))
            {
                graphics.FillRectangle(fill, 100, 720, 50, 4);
                PixelFrame frame = reader.Read(image);
                Require(EnemyHud.HasHealth(frame, false) && EnemyHud.HasHealth(frame, false, true),
                    "the whole frame can contain a long enemy health bar");
                graphics.Clear(Color.Black);
                graphics.FillRectangle(fill, 100, 720, 8, 4);
                frame = reader.Read(image);
                Require(!EnemyHud.HasHealth(frame, false) && EnemyHud.HasHealth(frame, true) && !EnemyHud.HasHealth(frame, true, true),
                    "a short PC bar requires combat and a cloud bar requires a 3:1 aspect");
            }
        }

        private static void RocciaToolboxHandoff()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            settings.ToolKey = "Y";
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            hud.Fighters[0] = "Roccia";
            hud.Fighters[1] = "Chixia";
            hud.Health[2] = 0;
            hud.ConcertoFull = true;
            bool switched = false, toolUsed = false;
            for (long now = 0; now <= 4000; now += 50)
            {
                BattleAction action = planner.Observe(hud, now);
                if (action.Kind == ActionKind.Button && action.Button == BattleButton.Tool)
                {
                    Require(switched && !toolUsed && action.NativeKey == 'Y', "one configured tool press after Roccia's intro");
                    toolUsed = true;
                }
                if (action.Kind == ActionKind.Switch)
                {
                    Require(action.Slot == (switched ? 0 : 1), "unexpected target slot");
                    if (switched)
                    {
                        Require(toolUsed, "the recipient must use the item before switching away");
                        return;
                    }
                    hud.Current = 1;
                    switched = true;
                }
                planner.Sent(action, now);
            }
            throw new InvalidOperationException("toolbox handoff did not finish");
        }

        private static void BackgroundFallback()
        {
            using (var window = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000),
                ClientSize = new Size(320, 180), BackColor = Color.FromArgb(220, 35, 20) })
            using (var capture = new WindowCapture())
            {
                window.Show();
                window.Update();
                PixelFrame frame;
                uint owner = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                Require(capture.TryRead(window.Handle, owner, window.ClientSize, out frame), "off-screen client capture failed");
                Require(!capture.TryRead(window.Handle, owner + 1, window.ClientSize, out frame), "capture must reject a different window owner");
                Require(capture.TryRead(window.Handle, owner, window.ClientSize, out frame), "valid owner must still capture after rejection");
                int red, green, blue;
                frame.Rgb(frame.Width / 2, frame.Height / 2, out red, out green, out blue);
                Require(red == 220 && green == 35 && blue == 20,
                    string.Format("background capture returned the wrong client pixels ({0},{1},{2})", red, green, blue));
            }
        }

        private sealed class MessageSink : Form
        {
            internal readonly List<int> Received = new List<int>();

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0100 || message.Msg == 0x0101 || message.Msg == 0x0201 || message.Msg == 0x0202)
                {
                    Received.Add(message.Msg);
                    return;
                }
                base.WndProc(ref message);
            }
        }

        private static void WindowMessageDelivery()
        {
            using (var sink = new MessageSink())
            {
                IntPtr hwnd = sink.Handle;
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                var input = new WindowMessages();
                Require(!input.Send(BattleAction.Attack(), hwnd, pid + 1, CancellationToken.None), "wrong process accepted");
                Require(input.Send(BattleAction.Press(BattleButton.Forward, 'W', true), hwnd, pid, CancellationToken.None),
                    "failed to hold a key in the test window");
                Require(input.Send(BattleAction.Press(BattleButton.Attack, 0, true), hwnd, pid, CancellationToken.None),
                    "failed to hold the mouse button in the test window");
                Require(input.ReleaseAll() && !input.ReleaseAll(), "all pending input must be released once");
                Application.DoEvents();
                Require(sink.Received.Take(2).SequenceEqual(new[] { 0x0100, 0x0201 }) &&
                    sink.Received.Skip(2).OrderBy(x => x).SequenceEqual(new[] { 0x0101, 0x0202 }),
                    "expected a complete key and mouse press/release pair on the window message queue");
            }
        }

        private sealed class MenuMessageSink : NativeWindow, IDisposable
        {
            internal readonly List<Message> Received = new List<Message>();

            internal MenuMessageSink(IntPtr parent, Rectangle bounds, bool enabled = true)
            {
                bool child = parent != IntPtr.Zero;
                CreateHandle(new CreateParams
                {
                    Parent = parent,
                    X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                    Style = (child ? 0x50000000 : unchecked((int)0x90000000)) | (enabled ? 0 : 0x08000000),
                    ExStyle = child ? 0 : 0x08000080
                });
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0006 || message.Msg == 0x0100 || message.Msg == 0x0101 ||
                    message.Msg >= 0x0200 && message.Msg <= 0x0202)
                {
                    Received.Add(message);
                    return;
                }
                base.WndProc(ref message);
            }

            public void Dispose() { DestroyHandle(); }
        }

        private static void MenuClickDelivery()
        {
            using (var root = new MenuMessageSink(IntPtr.Zero, new Rectangle(-22000, -22000, 1920, 1080)))
            using (var container = new MenuMessageSink(root.Handle, new Rectangle(1400, 330, 450, 190)))
            using (var button = new MenuMessageSink(container.Handle, new Rectangle(220, 8, 200, 100)))
            using (var disabled = new MenuMessageSink(root.Handle, new Rectangle(1650, 340, 180, 60), false))
            {
                uint pid;
                Native.GetWindowThreadProcessId(root.Handle, out pid);
                Point spot = FarmCursor.ToClient(new Point(1433, 297), new Size(1600, 900), new Size(1920, 1080));
                Require(spot == new Point(1720, 356), "normalized frame should map to the 1080p client point");
                Require(!WindowMessages.ClickMenu(root.Handle, pid + 1, spot, FarmMouse.Left, 1, 0,
                    CancellationToken.None, () => true), "the wrong process cannot receive a menu click");
                Require(WindowMessages.ClickMenu(root.Handle, pid, spot, FarmMouse.Left, 1, 0,
                    CancellationToken.None, () => true), "could not deliver a menu click");
                Application.DoEvents();

                Require(!root.Received.Any(m => m.Msg >= 0x0200) && !container.Received.Any(m => m.Msg >= 0x0200) &&
                    !disabled.Received.Any(m => m.Msg >= 0x0200), "a parent or disabled overlay received the child's click");
                List<Message> clicks = button.Received.Where(m => m.Msg >= 0x0200).ToList();
                int position = 100 | 18 << 16;
                Require(clicks.Select(m => m.Msg).SequenceEqual(new[] { 0x0200, 0x0201, 0x0202 }) &&
                    clicks.All(m => m.LParam.ToInt32() == position) && clicks[1].WParam.ToInt32() == 1 &&
                    root.Received.Any(m => m.Msg == 0x0006) && button.Received.Any(m => m.Msg == 0x0006),
                    "expected activation and move/down/up addressed to the same child-client point");

                button.Received.Clear();
                Require(!WindowMessages.ClickMenu(root.Handle, pid, spot, FarmMouse.Left, 1, 0,
                    CancellationToken.None, () => false), "the click should stop after input loses its original window");
                Application.DoEvents();
                Require(button.Received.Any(m => m.Msg == 0x0200) && !button.Received.Any(m => m.Msg >= 0x0201),
                    "a mouse press escaped the window check");

                root.Received.Clear();
                Require(WindowMessages.ClickMenu(root.Handle, pid, new Point(200, 300), FarmMouse.Left, 1, 0,
                    CancellationToken.None, () => true), "root-only menu click was rejected");
                Application.DoEvents();
                Require(root.Received.Any(m => m.Msg == 0x0201 && m.LParam.ToInt32() == (200 | 300 << 16)),
                    "the root received incorrect client coordinates outside its children");
            }
        }

        private static void PickupKeyDelivery()
        {
            using (var sink = new MenuMessageSink(IntPtr.Zero, new Rectangle(-22000, -22000, 100, 100)))
            {
                uint pid;
                Native.GetWindowThreadProcessId(sink.Handle, out pid);
                Require(!WindowMessages.TapKey(sink.Handle, pid + 1, 'F', 1, CancellationToken.None),
                    "wrong process should not receive an interaction key");
                Require(WindowMessages.TapKey(sink.Handle, pid, 'F', 1, CancellationToken.None),
                    "interaction key was not delivered");
                Application.DoEvents();
                var keys = sink.Received.Where(m => m.Msg == 0x0100 || m.Msg == 0x0101).ToArray();
                Require(keys.Select(m => m.Msg).SequenceEqual(new[] { 0x0100, 0x0101 }) &&
                    keys.All(m => m.WParam.ToInt32() == 'F') && (keys[1].LParam.ToInt64() & 0xc0000000L) == 0xc0000000L,
                    "expected one F down and one F up for the same window");
            }
        }

        private static void CombatEndCleanup()
        {
            var settings = CombatSettings.Defaults();
            settings.SupportOpener = false;
            var planner = new BattlePlanner(settings);
            var hud = BattleHud();
            hud.Fighters[0] = "Cantarella";
            hud.Health[1] = 0;
            planner.Observe(hud, 0);
            planner.Observe(hud, 100);
            hud.EnemyHealth = hud.Locked = false;
            planner.Observe(hud, 1900);
            BattleAction action = planner.Observe(hud, 4910);
            Require(action.Kind == ActionKind.Switch && action.Slot == 2 && !planner.Fighting,
                "Cantarella should leave the field after combat ends");
            planner.Sent(action, 4910);
            hud.Current = 2;
            Require(planner.Observe(hud, 5500).Kind == ActionKind.Wait, "confirmed cleanup must stop retrying");
        }

        private static void VisualCharge()
        {
            using (var bitmap = new Bitmap(1600, 900, PixelFormat.Format32bppRgb))
            using (var reader = new FrameReader())
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var color = new SolidBrush(Color.FromArgb(233, 69, 140)))
            {
                graphics.Clear(Color.Black);
                Require(reader.Read(bitmap).Camellya.Charge(false) == 0, "empty meter");
                for (int x = 679; x < 793; x += 5) graphics.FillRectangle(color, x, 833, 3, 4);
                double filled = reader.Read(bitmap).Camellya.Charge(false);
                Require(filled > .46 && filled < .54, "half-filled striped meter: " + filled);
                for (int x = 840; x < 875; x += 5) graphics.FillRectangle(color, x, 833, 3, 4);
                Require(reader.Read(bitmap).Camellya.Charge(false) < 0,
                    "second patch of matching color must be treated as interference");
            }
        }

        private static void ConcertoCalibration()
        {
            using (var bitmap = new Bitmap(1920, 1080, PixelFormat.Format32bppRgb))
            using (var reader = new FrameReader())
            {
                var meter = new ConcertoMeter();
                Rectangle patch = FramePatch.ScaleCentered(new Rectangle(Point.Empty, bitmap.Size), 3840, 2160, 1431, 1942, 1557, 2068);
                ConcertoColor observed;
                PaintConcerto(bitmap, patch, Color.FromArgb(170, 115, 230), .85);
                Require(meter.Read(reader.Read(bitmap), ConcertoColor.Unknown, out observed) == 0 && observed == ConcertoColor.Electro,
                    "a partial ring identifies the element but has no calibration before a full reading");
                PaintConcerto(bitmap, patch, Color.FromArgb(170, 115, 230), 1);
                Require(meter.Read(reader.Read(bitmap), ConcertoColor.Electro, out observed) == 1, "closed ring establishes a full calibration");
                PaintConcerto(bitmap, patch, Color.FromArgb(170, 115, 230), .85);
                double value = meter.Read(reader.Read(bitmap), ConcertoColor.Electro, out observed);
                Require(value > .82 && value < .88, "connected arc percentage=" + value);
                PaintConcerto(bitmap, patch, Color.FromArgb(208, 90, 162), .85);
                Require(meter.Read(reader.Read(bitmap), ConcertoColor.Unknown, out observed) == 0 && observed == ConcertoColor.Havoc,
                    "calibration is independent for Havoc and Electro");
            }
        }

        private static void PaintConcerto(Bitmap bitmap, Rectangle patch, Color color, double portion)
        {
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.FillRectangle(Brushes.Black, patch);
            for (int y = 0; y < patch.Height; y++)
            for (int x = 0; x < patch.Width; x++)
            {
                int dx = x - patch.Width / 2, dy = y - patch.Height / 2;
                int square = dx * dx + dy * dy;
                if (square < 24 * 24 || square > 26 * 26) continue;
                double angle = Math.Atan2(dy, dx);
                if (angle < 0) angle += 2 * Math.PI;
                if (angle < 2 * Math.PI * portion) bitmap.SetPixel(patch.Left + x, patch.Top + y, color);
            }
        }

        private static void ReplayReference(string root)
        {
            string images = Path.Combine(root, "tests", "images");
            if (File.Exists(Path.Combine(root, "ok_templates", "43.png")))
                Check("recovery map locates both waypoint sizes in distance order", () => DomainChecks.RecoveryMap(root));
                Check("domain reward amount and reserve popup match the reference captures", () => DomainChecks.ReplayRewardControls(root));
            if (File.Exists(Path.Combine(root, "ok_templates", "monthly_card.png")))
            {
                Check("monthly card recognition locates the prompt and its bottom-center control", () => GameSourceChecks.MonthlyCard(root));
                Check("monthly card recognition honors both search margins on wide screens", () => GameSourceChecks.MonthlyCardGeometry(root));
            }
            if (File.Exists(Path.Combine(images, "cloud_disconnect_4012.png")))
                Check("cloud assist reconnects after both original disconnect dialogs", () => WorldAssistChecks.CloudReconnect(root));
                Check("login detects the original notice and switch-account icons", () => WorldAssistChecks.LoginIcons(root));
            if (File.Exists(Path.Combine(images, "xinyao_interaction_different_prompt.png")))
                Check("world assist blocks both original Astrite prompts", () => WorldAssistChecks.ExcludesAstrite(root));
                Check("automatic pickup preserves the selected prompt across resolutions and key presses", () => WorldAssistChecks.PickupReplay(root));
            if (File.Exists(Path.Combine(images, "absorb.png")) && File.Exists(Path.Combine(root, "ok_templates", "62.png")))
                Check("automatic pickup follows the reference lower-row wheel and three-key sequence", () => WorldAssistChecks.PickupBelowReplay(root));
            if (File.Exists(Path.Combine(images, "21-9_quest_skip.png")))
                Check("delayed story summaries and optional checkboxes follow the actual click sequence",
                    () => WorldAssistChecks.DialogActions(root));
                Check("travel from the map confirms both controls in the compact entry dialog",
                    () => WorldAssistChecks.TravelConfirmation(root));
                Check("challenge markers track displaced HUD icons and ignore unrelated quest icons",
                    () => WorldAssistChecks.ChallengeMarkers(root));
                Check("party number templates recover the active slot without accepting co-op or map badges",
                    () => WorldAssistChecks.PartyTemplateFallback(root));
                Check("levitator recognition on entry works with short enemy health bar scanning",
                    () => WorldAssistChecks.LevitatorEntryWithShortBars(root));
                Check("world assist recognizes the original story skip and widescreen confirmations", () => WorldAssistChecks.SkipDialog(root));
                Check("tinted skip clicks follow the left anchor and ignore flat backgrounds", () => WorldAssistChecks.WideSkip(root));
            if (File.Exists(Path.Combine(images, "rahai_score.jpg")))
            {
                Check("Rahai board and placement matches the reference captures", () => RahaiChecks.Replay(images));
                Check("Rahai title, score and rewards are recognized in native OCR regions", () => RahaiChecks.OcrReplay(images));
            }
            Check("reference hotkey reading updates echo and liberation while retaining configured resonance and tool", delegate
            {
                var options = CombatSettings.Defaults();
                options.ResonanceKey = options.EchoKey = options.LiberationKey = options.ToolKey = "A";
                HudSnapshot result = ReadReplay(Path.Combine(images, "in_combat.png"), 1600,
                    frame => new HudVision(options).Read(frame));
                Require(result.Valid && result.Keys.SequenceEqual(new[] { 'A', 'R', 'Q' }) && options.ToolKey == "A",
                    "displayed echo/liberation must be identified independently of configured defaults");
            });
            Check("reference Levitator fixtures separate equipped, grounded, and airborne states", delegate
            {
                foreach (string filename in new[] { "in_combat.png", "in_combat3.png" })
                using (var source = new Bitmap(Path.Combine(images, filename)))
                using (var bitmap = new Bitmap(1600, 900, PixelFormat.Format32bppRgb))
                using (var reader = new FrameReader())
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.DrawImage(source, new Rectangle(0, 0, 1600, 900));
                    var vision = new HudVision(CombatSettings.Defaults());
                    HudSnapshot initial = vision.Read(reader.Read(bitmap));
                    Require(initial.Valid && initial.LevitatorKnown && initial.Airborne == (filename == "in_combat.png"),
                        filename + " initial tool brightness");
                    graphics.FillRectangle(Brushes.Black, new Rectangle(1256, 786, 34, 34));
                    HudSnapshot airborne = vision.Read(reader.Read(bitmap), true);
                    Require(airborne.Valid && airborne.LevitatorKnown && airborne.Airborne,
                        filename + " a dimmed icon should retain the entry recognition and mark the character airborne");
                }
            });
            Check("cooldown numerals match the reference combat screenshots", delegate
            {
                var expected = new Dictionary<string, double[]>
                {
                    { "in_combat.png", new[] { 0.0, 18.1, 0.0 } },
                    { "in_combat3.png", new[] { 1.3, 14.1, 18.0 } },
                    { "all_cd_1080p.png", new[] { 13.6, 18.4, 22.6 } },
                    { "con_full2.png", new[] { 1.9, 21.3, 0.0 } }
                };
                var different = new List<string>();
                foreach (var reference in expected)
                {
                    HudSnapshot result = Replay(Path.Combine(images, reference.Key), 1600);
                    double[] cooldowns = result.Cooldowns.Select((value, skill) => result.Skills[skill] == SkillState.Cooling ? value : 0).ToArray();
                    if (!cooldowns.SequenceEqual(reference.Value)) different.Add(reference.Key + " " + string.Join("/", cooldowns));
                }
                Require(different.Count == 0, string.Join(", ", different));
            });
            Check("reference short target box is reserved for Lucilla's combat layout", delegate
            {
                using (var source = new Bitmap(Path.Combine(root, "ok_templates", "25.png")))
                using (var reader = new FrameReader())
                {
                    PixelFrame frame = reader.Read(source);
                    var vision = new CombatVision(CombatSettings.Defaults());
                    Require(EnemyHud.LockOffset(frame, 847) == 0 && vision.HasShortTarget(frame),
                        "the short-action fixture must not activate ordinary lock detection");
                    var options = CombatSettings.Defaults();
                    options.EchoKey = options.LiberationKey = "A";
                    for (int slot = 0; slot < 3; slot++) options.PartyCharacters[slot] = "Chixia";
                    HudSnapshot ordinary = new HudVision(options).Read(frame, allowShortEnemyBar: true);
                    Require(ordinary.Valid && !ordinary.Locked && ordinary.Keys[1] == 'A' && ordinary.Keys[2] == 'A',
                        "ordinary character keeps its configured hotkeys and ignores a short target");
                    options.PartyCharacters[ordinary.Current] = "Lucilla";
                    HudSnapshot lucilla = new HudVision(options).Read(frame, allowShortEnemyBar: true);
                    Require(lucilla.Locked && lucilla.LockOffset == 329 && lucilla.Fighters[lucilla.Current] == "Lucilla" &&
                        lucilla.Keys[1] == 'A' && lucilla.Keys[2] == 'A',
                        "only the resolved Lucilla role may enter combat from this short target");
                }
            });
            Check("Linnai recognizes acceleration from the archived long target layout", delegate
            {
                var options = CombatSettings.Defaults();
                for (int slot = 0; slot < 3; slot++) options.PartyCharacters[slot] = "Linnai";
                HudSnapshot longTarget = ReadReplay(Path.Combine(images, "cloud_combat_long_action.png"), 1600,
                    frame => new HudVision(options).Read(frame, true, true), true);
                Require(longTarget.Valid && longTarget.EnemyHealth && longTarget.LongActionLayout != 0 && longTarget.LinnaiAccelerating,
                    "a confirmed long target layout must still signal Linnai's acceleration: HUD=" + longTarget.Valid +
                    " health=" + longTarget.EnemyHealth + " layout=" + longTarget.LongActionLayout + " accelerating=" + longTarget.LinnaiAccelerating);
                var alternative = new HudSnapshot { Current = 0, LongActionLayout = 1 };
                alternative.Fighters[0] = "Linnai";
                new CombatVision(options).Read(new PixelFrame(new byte[1600 * 900 * 4], 1600, 900), alternative, true);
                Require(alternative.LinnaiAccelerating, "the optional second long-target layout also indicates acceleration");
            });
            Check("attack entry uses the current desktop lock across the three reference captures", delegate
            {
                var paths = new[] { "tests/images/in_combat.png", "tests/images/in_combat3.png",
                    "ok_templates/57d8d801-BitBlt_True_3840x2160_1759986393607.1733_original.png" };
                for (int index = 0; index < paths.Length; index++)
                {
                    var options = CombatSettings.Defaults();
                    options.AutoTarget = false;
                    var planner = new BattlePlanner(options);
                    using (var image = new Bitmap(Path.Combine(root, paths[index])))
                    using (var reader = new FrameReader())
                    {
                        HudSnapshot hud = new HudVision(options).Read(reader.Read(image));
                        planner.Observe(hud, 0);
                        planner.Observe(hud, 200);
                        Require(hud.Valid && hud.Locked == (index != 1) && planner.Fighting == (index != 1), paths[index]);
                    }
                }
            });
            Check("the source captures for ordinary and full execution prompts are distinguished", delegate
            {
                var fixtures = new[] { "ok_templates/30811235-Windows_Graphics_Capture_3840x2160_1766756085539.751_original.png",
                    "ok_templates/f_break_full.png", "tests/images/in_combat3.png" };
                for (int index = 0; index < fixtures.Length; index++)
                {
                    int expected = index;
                    ReadReplay(Path.Combine(root, fixtures[index]), 3840, frame =>
                    {
                        var hud = new HudSnapshot { Current = 0 };
                        var vision = new CombatVision(CombatSettings.Defaults());
                        vision.Read(frame, hud, true);
                        Require(hud.BreakPrompt == (expected < 2) && hud.Shows("f_break_full") == (expected < 2) &&
                            vision.HasOrdinaryExecutionPrompt(frame) == (expected == 0), "wrong execution prompt in " + fixtures[index]);
                        return true;
                    });
                }
            });
            Check("ordinary execution can move with the enemy inside the source search region", delegate
            {
                string path = Path.Combine(root, "ok_templates", "30811235-Windows_Graphics_Capture_3840x2160_1766756085539.751_original.png");
                using (var source = new Bitmap(path))
                using (var scene = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppRgb))
                using (var reader = new FrameReader())
                using (Graphics graphics = Graphics.FromImage(scene))
                {
                    var vision = new CombatVision(CombatSettings.Defaults());
                    var glyph = new Rectangle(1690, 1510, 48, 52);
                    graphics.DrawImage(source, new Rectangle(2190, 1050, glyph.Width, glyph.Height), glyph, GraphicsUnit.Pixel);
                    Require(vision.HasOrdinaryExecutionPrompt(reader.Read(scene)), "moving the same prompt must not hide it");
                    graphics.Clear(Color.Black);
                    graphics.DrawImage(source, new Rectangle(3290, 1050, glyph.Width, glyph.Height), glyph, GraphicsUnit.Pixel);
                    Require(!vision.HasOrdinaryExecutionPrompt(reader.Read(scene)), "outside the battle's prompt region must be ignored");
                }
            });
            Check("desktop target shapes agree with normal, long and unlocked source layouts", delegate
            {
                var filenames = new[] { "57d8d801-BitBlt_True_3840x2160_1759986393607.1733_original.png",
                    "5770cfa5-BitBlt_True_3840x2160_1759988352721.53_original.png",
                    "e4b3051a-Windows_Graphics_Capture_1600x900_1766755217313.3096_original.png",
                    "b9576d56-Windows_Graphics_Capture_1600x900_1766755208823.355_original.png",
                    "c4bcbbcf-BitBlt_True_3840x2160_1759986985843.6372_original.png", "char_iuno.png" };
                var vision = new CombatVision(CombatSettings.Defaults());
                for (int index = 0; index < filenames.Length; index++)
                using (var source = new Bitmap(Path.Combine(root, "ok_templates", filenames[index])))
                using (var reader = new FrameReader())
                {
                    bool recognized, shifted;
                    int layout;
                    int offset = vision.DesktopLockOffset(reader.Read(source), out recognized, out shifted, out layout);
                    Require(recognized && !shifted && (offset > 0) == (index != 1 && index != 3), filenames[index] + " lock=" + offset +
                        " recognized=" + recognized + " shifted=" + shifted);
                    if (index >= 4) Require(offset > 454, "long-layout target position was not retained");
                    Require(layout == (index >= 4 ? index - 3 : 0), "the ordinary long action is distinct from the longer layout");
                }
            });
            foreach (int width in new[] { 1280, 1600, 1920, 2560, 3840 })
            {
                int replayWidth = width;
                Check("HUD replay at " + width + " pixels", delegate
                {
                    HudSnapshot locked = Replay(Path.Combine(images, "in_combat.png"), replayWidth);
                    Require(locked.Valid && locked.Locked && locked.Current == 0, "PC lock/party not recognized");
                    Require(locked.Keys[1] == 'R' && locked.Keys[2] == 'Q', "swap in displayed skill keys not detected");
                    Require(locked.Skills[1] == SkillState.Cooling, "cooling echo appeared usable");
                    HudSnapshot allCooling = Replay(Path.Combine(images, "all_cd_1080p.png"), replayWidth);
                    Require(allCooling.Valid && allCooling.Skills.All(skill => skill == SkillState.Cooling),
                        "expected three cooling skills: " + string.Join(", ", allCooling.Skills));
                    HudSnapshot idle = Replay(Path.Combine(images, "in_combat3.png"), replayWidth);
                    Require(idle.Valid && !idle.Locked && !idle.EnemyHealth, "idle PC frame must not start combat");
                    HudSnapshot world = Replay(Path.Combine(images, "cloud_world_unlocked.png"), replayWidth);
                    Require(world.Valid && !world.Locked && !world.EnemyHealth && world.Keys[1] == 'Q',
                        "unlocked HUD or alternate bindings misclassified");
                    foreach (string name in new[] { "cloud_launcher_home.png", "cloud_disconnect_4012.png", "big_map.png" })
                        Require(!Replay(Path.Combine(images, name), replayWidth).Valid, "UI overlay misclassified: " + name);
                });
            }
            Check("boss HUD and full concerto", delegate
            {
                HudSnapshot boss = Replay(Path.Combine(images, "con_full2.png"), 2560);
                Require(boss.Valid && boss.Locked && boss.ConcertoFull, "boss lock or full concerto not recognized");
            });
            Check("both source boss markers count as target evidence", delegate
            {
                var path = Path.Combine(root, "ok_templates");
                var positive = new[] { "30811235-Windows_Graphics_Capture_3840x2160_1766756085539.751_original.png",
                    "e8c83e8b-Windows_Graphics_Capture_3840x2160_1766756041738.255_original.png" };
                var vision = new CombatVision(CombatSettings.Defaults());
                using (var reader = new FrameReader())
                foreach (string name in positive)
                using (var source = new Bitmap(Path.Combine(path, name)))
                    Require(vision.HasBossMarker(reader.Read(source)), "boss marker was missed in " + name);
            });
            Check("boss map icon, minimap heading, highlighted confirmation, and world HUD match their source captures", () => ArchivedTaskControls(root));
            Check("visual matching rejects search areas that cannot contain the template", SmallTemplateSearch);
            Check("concerto fullness agrees with all six source captures", delegate
            {
                var files = new[] { "con_full.png", "in_combat.png", "all_cd_1080p.png", "con_full2.png", "absorb.png", "angle_130.png" };
                var meter = new ConcertoMeter();
                for (int index = 0; index < files.Length; index++)
                using (var source = new Bitmap(Path.Combine(images, files[index])))
                using (var reader = new FrameReader())
                {
                    ConcertoColor observed;
                    double value = meter.Read(reader.Read(source), ConcertoColor.Unknown, out observed);
                    Require((value == 1) == (index < 4), "concerto disagrees with source in " + files[index] + " value=" + value + " type=" + observed);
                }
            });
            Check("heavy-attack forte markers agree with the source captures", delegate
            {
                var files = new[] { "tests/images/treasure2.png", "tests/images/33forte.png", "tests/images/33_forte_2.png",
                    "tests/images/33_forte_3.png", "ok_templates/3.png" };
                for (int index = 0; index < files.Length; index++)
                using (var source = new Bitmap(Path.Combine(root, files[index])))
                using (var reader = new FrameReader())
                {
                    var hud = new HudSnapshot { Current = 0 };
                    new CombatVision(CombatSettings.Defaults()).Read(reader.Read(source), hud, true);
                    Require(hud.Shows("mouse_forte") == (index != 0), "heavy-attack cue disagrees in " + files[index]);
                }
            });
            foreach (int width in new[] { 1600, 1920, 2560 })
            {
                int replayWidth = width;
                Check("cloud lock, compressed health bars, and overlays at " + width + " pixels", delegate
                {
                    Func<string, HudSnapshot> cloud = name => Replay(Path.Combine(images, name + ".png"), replayWidth, true);
                    Require(cloud("cloud_target_locked").Locked, "locked cloud target not recognized");
                    Require(!cloud("cloud_target_unlocked").Locked, "unlocked cloud target misclassified");
                    Require(cloud("cloud_combat_long_action").EnemyHealth, "long-action enemy health not recognized");
                    Require(cloud("cloud_combat_low_health").EnemyHealth, "low enemy health not recognized");
                    Require(!cloud("cloud_world_unlocked").EnemyHealth, "cloud exploration misclassified");
                    Require(!cloud("cloud_disconnect_4012").Valid && !cloud("cloud_disconnect_1020").Valid,
                        "disconnection overlays must suppress combat actions");
                });
                Check("cloud target icon colors match all five reference captures at " + width + " pixels", delegate
                {
                    var vision = new CombatVision(CombatSettings.Defaults());
                    var fixtures = new[] { "tests/images/cloud_target_locked.png", "tests/images/cloud_target_unlocked.png",
                        "tests/images/cloud_world_unlocked.png", "ok_templates/63.png", "ok_templates/64.png" };
                    for (int index = 0; index < fixtures.Length; index++)
                    {
                        string fixture = fixtures[index];
                        bool expected = index == 0 || index == 4;
                        ReadReplay(Path.Combine(root, fixture), replayWidth, frame =>
                        {
                            Require(!vision.FindCloudTargetIcon(frame).IsEmpty, "cloud lock icon missing in " + fixture);
                            int layout;
                            Require((vision.CloudLockOffset(frame, out layout) > 0) == expected, "cloud lock color disagrees in " + fixture);
                            return true;
                        }, true);
                    }
                });
            }
            Check("portrait retained between slots", delegate
            {
                using (var first = new Bitmap(Path.Combine(images, "in_combat.png")))
                using (var next = new Bitmap(Path.Combine(images, "all_cd_1080p.png")))
                using (var reader = new FrameReader())
                {
                    var settings = CombatSettings.Defaults();
                    settings.Characters.Add(new CharacterProfile { Name = "录入角色", Rotation = Rotation.ForRole(BattleRole.Support),
                        Portrait = PortraitReader.Sample(reader.Read(first), 1) });
                    HudSnapshot after = new HudVision(settings).Read(reader.Read(next));
                    Require(after.Valid && after.Profiles[0] == settings.Characters[0], "portrait did not follow to the new slot");
                }
            });
        }

        private static void ArchivedTaskControls(string root)
        {
            string images = Path.Combine(root, "tests", "images");
            var vision = new FarmVision();
            using (var reader = new FrameReader())
            using (var boss = new Bitmap(Path.Combine(images, "teleport_boss.png")))
            using (var heading = new Bitmap(Path.Combine(images, "angle_130.png")))
            using (var confirm = new Bitmap(Path.Combine(images, "confirm_highlight.png")))
            using (var world = new Bitmap(Path.Combine(images, "all_cd_1080p.png")))
            {
                PixelFrame map = reader.Read(boss);
                var markerFinder = new BossMarkerFinder(BossMarkerFinder.DataPath);
                Rectangle marker;
                Require(vision.Read(map, true).HasMapMarker || markerFinder.TryFind(map, out marker),
                    "recognize the checked or rotated boss marker on the map");
                var navigator = new MinimapNavigator(MinimapNavigator.ArrowPath);
                double angle;
                Require(navigator.TryHeading(reader.Read(heading), out angle) && angle >= 100 && angle <= 200,
                    "recognize the player's minimap heading, got " + angle);
                Require(!navigator.TryHeading(new PixelFrame(new byte[1600 * 900 * 4], 1600, 900), out angle),
                    "a blank minimap cannot yield a heading");
                Rectangle button;
                Require(vision.FindOptionalButton(reader.Read(confirm), .70, out button), "recognize an enabled confirmation button");
                HudSnapshot hud = new HudVision(CombatSettings.Defaults()).Read(reader.Read(world));
                Require(hud.Valid && !hud.InRealm, "recognize the party HUD without classifying its scene as a realm");
            }
        }

        private static void SmallTemplateSearch()
        {
            var vision = new FarmVision();
            var blank = new PixelFrame(new byte[1600 * 900 * 4], 1600, 900);
            foreach (VisualPattern pattern in PatternBank.For("boss_check_mark"))
            {
                var shortArea = new Rectangle(200, 200, pattern.Area.Width, pattern.Area.Height - 1);
                Require(vision.FindAll(blank, pattern.Label, shortArea, 1, .8, .7).Count == 0,
                    "the shorter search area must produce zero matches");
            }
        }

        private static HudSnapshot Replay(string path, int width, bool cloud = false)
        {
            return ReadReplay(path, width, frame => new HudVision(CombatSettings.Defaults()).Read(frame, cloud, cloud), cloud);
        }

        private static T ReadReplay<T>(string path, int width, Func<PixelFrame, T> read, bool retainCloudColors = false)
        {
            using (var source = new Bitmap(path))
            using (var resized = new Bitmap(width, width * 9 / 16, PixelFormat.Format32bppRgb))
            using (var reader = new FrameReader(retainCloudColors))
            {
                if (retainCloudColors && source.Width > width)
                    ResizeByArea(source, resized);
                else using (Graphics graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    graphics.DrawImage(source, new Rectangle(0, 0, resized.Width, resized.Height));
                }
                return read(reader.Read(resized));
            }
        }

        private static void ResizeByArea(Bitmap source, Bitmap resized)
        {
            double dx = (double)source.Width / resized.Width, dy = (double)source.Height / resized.Height;
            Rectangle extent = new Rectangle(Point.Empty, source.Size);
            Rectangle target = new Rectangle(Point.Empty, resized.Size);
            BitmapData from = source.LockBits(extent, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                BitmapData to = resized.LockBits(target, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    var input = new byte[from.Stride * source.Height];
                    var output = new byte[to.Stride * resized.Height];
                    Marshal.Copy(from.Scan0, input, 0, input.Length);
                    for (int y = 0; y < resized.Height; y++)
                    {
                        double top = y * dy, bottom = top + dy;
                        for (int x = 0; x < resized.Width; x++)
                        {
                            double left = x * dx, right = left + dx;
                            double r = 0, g = 0, b = 0;
                            for (int yy = (int)top; yy < Math.Min(source.Height, Math.Ceiling(bottom)); yy++)
                            {
                                double height = Math.Min(yy + 1, bottom) - Math.Max(yy, top);
                                for (int xx = (int)left; xx < Math.Min(source.Width, Math.Ceiling(right)); xx++)
                                {
                                    double weight = height * (Math.Min(xx + 1, right) - Math.Max(xx, left));
                                    int position = yy * from.Stride + 4 * xx;
                                    b += input[position] * weight;
                                    g += input[position + 1] * weight;
                                    r += input[position + 2] * weight;
                                }
                            }
                            int dest = y * to.Stride + 4 * x;
                            output[dest] = (byte)Math.Round(b / (dx * dy));
                            output[dest + 1] = (byte)Math.Round(g / (dx * dy));
                            output[dest + 2] = (byte)Math.Round(r / (dx * dy));
                        }
                    }
                    Marshal.Copy(output, 0, to.Scan0, output.Length);
                }
                finally { resized.UnlockBits(to); }
            }
            finally { source.UnlockBits(from); }
        }

        private static void RenderWindows(string directory)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(directory);
            using (var main = new MainForm(Path.Combine(directory, "NoSettings.json"), enableGlobalHotkey: false))
                Draw(main, Path.Combine(directory, "main.png"));
            using (var profiles = new ProfileForm(new List<CharacterProfile>()))
                Draw(profiles, Path.Combine(directory, "profiles.png"));
            using (var daily = new DailyForm(DailyOptions.Defaults()))
                Draw(daily, Path.Combine(directory, "daily.png"));
        }

        private static void Draw(Form form, string destination)
        {
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-10000, -10000);
            form.Show();
            form.PerformLayout();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(destination, ImageFormat.Png);
            }
        }
    }
}
