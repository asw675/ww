using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class SelfTest
    {
        private static int failed;

        [STAThread]
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
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
            if (args.Length == 3 && args[0] == "--browser-protocol")
            {
                using (var browser = new CloudBrowser(args[1], address => address == args[2]))
                    Check("loopback browser captures an offline frame and routes input", () => VerifyBrowserEvents(browser, () => true));
                return failed == 0 ? 0 : 1;
            }
            Check("two-frame lock confirmation and stop on HUD loss", LockConfirmation);
            Check("auto-target is gated by repeated enemy evidence", AutoTarget);
            Check("active fights retry targets at the reference cadence and stop when the health bar disappears", ActiveRetarget);
            Check("cooldowns, disabled liberation, and detected skill key", SkillSelection);
            Check("an unrecognized character uses the source default echo-to-liberation sequence", SourceFallback);
            Check("world liberation preference follows the confirmed battle through marker flicker", WorldLiberationPreference);
            Check("rotation waits for concerto and verifies switches", RotationAndSwitch);
            Check("failed switch backs off and resumes safely", FailedSwitch);
            Check("low-health support follows the learned character", LowHealthSupport);
            Check("fight cleanup runs the character's own handoff before its healer switch", FightEndHandoff);
            Check("configuration round-trip and validation", SettingsRoundTrip);
            Check("4C farm signatures load from the embedded table", FarmChecks.Signatures);
            Check("4C farm walks approach, challenge, combat and pickup", FarmChecks.StateMachine);
            Check("4C farm teleport opens the guidebook and picks a boss", FarmChecks.TeleportFlow);
            Check("4C farm revive clicks the death prompt", FarmChecks.ReviveFlow);
            Check("4C farm closes claim popups and restarts the challenge", FarmChecks.ClaimRestart);
            Check("4C farm teleport clicks the configured boss row", FarmChecks.TeleportBossRow);
            Check("4C farm ends combat as soon as an echo is visible", FarmChecks.EchoEndsCombat);
            Check("4C farm clicks the OCR-matched boss level", FarmChecks.LevelOcr);
            Check("input cannot run without a verified foreground window", MissingWindow);
            Check("local game window is recognized without a readable title or installation path", LocalWindowIdentity);
            Check("input privilege preflight detects elevated clients and reads native process tokens", InputPrivilegePreflight);
            Check("cloud combat uses health evidence and a bounded grace period", CloudGrace);
            Check("visible cloud health starts combat without sending a needless lock", CloudEntry);
            Check("cloud control accepts only the official endpoint", CloudPageScope);
            Check("challenge start without a visible timer and instance liberation", ChallengeOpening);
            Check("known character switch tolerates hidden HUD and retries without losing intro", SwitchTransition);
            Check("full concerto refreshes the next character before a switch is confirmed", RecheckConcerto);
            Check("execution is ordered around the confirmed change of slot", SwitchExecutionOrder);
            Check("freeze-adjusted buffs and Hecate execution windows mirror the reference", FreezeAdjustedDecisions);
            Check("confirmed hidden-HUD animation records its duration in the shared clock", AnimationFreezeObservation);
            Check("animation clicks and F have independent reference cadence and honor the delay and opt-out", AnimationKeyCadence);
            Check("only a fresh hidden-team HUD frame can emit input from the animation path", PlannerAnimationInputs);
            Check("liberation helper retries bright R and requires animation after it dims", LiberationRetryFlow);
            Check("dim liberation gets the short retry window and skips true cooldown", DimLiberationFlow);
            Check("liberation varies attack inputs before and during the animation", LiberationAttackFlow);
            Check("resonance retries with normal attack and delays F only inside its animation", ResonanceAnimationFlow);
            Check("a persistently shifted PC target layout gets one timed pair of menu presses", RecoverShiftedTarget);
            Check("a valid Abyss countdown keeps combat active beyond five seconds without a lock", ChallengeTimerCombat);
            Check("the countdown parser distinguishes time, zero and invalid punctuation", ChallengeClockRead);
            Check("off-center enemy bars use the source color, width and height rules", EnemyColorStrip);
            Check("Roccia's confirmed intro gives the recipient one toolbox use before handing off", RocciaToolboxHandoff);
            Check("background client crop and key flags", BackgroundCoordinates);
            Check("background capture fallback reads an offline test window", BackgroundFallback);
            Check("background keyboard and mouse releases reach only the verified test window", WindowMessageDelivery);
            Check("a direct combat-end handoff chooses a living teammate", CombatEndCleanup);
            Check("Camellya visual charge separates striped fill from interference", VisualCharge);
            Check("concerto measures the connected arc against the last full ring of its element", ConcertoCalibration);
            RotationChecks.Run(Check);
            if (args.Length == 2 && args[0] == "--reference")
            {
                ReplayReference(args[1]);
                RotationChecks.Replay(args[1], Check);
            }
            Console.WriteLine(failed == 0 ? "All checks passed." : failed + " check(s) failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string name, Action test)
        {
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

        private static void SettingsRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "TideCombat-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var settings = CombatSettings.Defaults();
                settings.Characters.Add(new CharacterProfile { Name = "治疗角色", Portrait = new byte[PortraitReader.SampleLength],
                    Rotation = Rotation.ForRole(BattleRole.Support) });
                SettingsFile.Save(path, settings);
                settings.AutoTarget = false;
                SettingsFile.Save(path, settings);
                CombatSettings saved = SettingsFile.Load(path);
                Require(!saved.AutoTarget && saved.Characters[0].Name == "治疗角色", "independent settings were not persisted");
                saved.EchoKey = saved.LiberationKey;
                bool denied = false;
                try { SettingsFile.Save(path, saved); }
                catch (InvalidDataException) { denied = true; }
                Require(denied && SettingsFile.Load(path).EchoKey != saved.EchoKey, "invalid config overwrote the saved config");
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
                    VerifyBrowserEvents(session, () => !process.HasExited);
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
            }
        }

        private static bool CenterMatches(PixelFrame frame, int red, int green, int blue)
        {
            int r, g, b;
            frame.Rgb(frame.Width / 2, frame.Height / 2, out r, out g, out b);
            return Math.Abs(r - red) <= 8 && Math.Abs(g - green) <= 8 && Math.Abs(b - blue) <= 8;
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
                }
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
            foreach (AnimationInput flags in new[] { AnimationInput.InteractAndAttack, AnimationInput.None })
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
                    if (flags == AnimationInput.None)
                    {
                        Require(first.Kind == ActionKind.Wait && turn.ObserveUnavailable(1350, true).Kind == ActionKind.Wait,
                            "suppressed character must not click or send F");
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
                    BattleAction retry = turn.Observe(hud, 1200);
                    Require(retry.Button == BattleButton.Resonance && retry.AttackBefore, "normal attack follows a prior E attempt");
                    turn.Sent(retry, 1200);
                    Require(turn.ObserveUnavailable(1350, true).Kind == ActionKind.Wait, "no input on entry into the animation");
                    Require(turn.ObserveUnavailable(1849, true).Kind == ActionKind.Wait, "wait until the full 500 ms inside animation");
                    BattleAction pulse = turn.ObserveUnavailable(1850, true);
                    Require(clickF ? pulse.Button == BattleButton.Interact && !pulse.AttackBefore : pulse.Kind == ActionKind.Wait,
                        "animated resonance sends F only when requested, without an attack");
                    turn.Observe(hud, 1900);
                    Require(turn.Finished && context.Succeeded && context.AnimationSeen && actor.LastSkill[0] == 1000,
                        "the first cast is recorded, and the turn finishes on the return of the HUD");
                    Require(context.Clock.Elapsed(900, 1900) == 550, "record the 550 ms animation minus the 100 ms allowance");
                }
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
            planner.Observe(hud, 0);
            planner.Observe(hud, 100);
            hud.EnemyHealth = hud.Locked = false;
            for (int now = 200; now <= 8200; now += 100)
            {
                BattleAction action = planner.Observe(hud, now);
                Require(action.Kind == ActionKind.Attack && planner.Fighting, "keep acting during a challenge with time left");
            }
            hud.Countdown = false;
            hud.ChallengeSeconds = 0;
            Require(planner.Observe(hud, 8300).Kind == ActionKind.Target, "zero ends the timer's combat allowance");
            planner.Observe(hud, 11400);
            Require(!planner.Fighting, "unresolved targeting then follows normal exit behavior");
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
                Require(capture.TryRead(window.Handle, window.ClientSize, out frame), "off-screen client capture failed");
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
            using (var main = new MainForm(Path.Combine(directory, "NoSettings.json")))
                Draw(main, Path.Combine(directory, "main.png"));
            using (var profiles = new ProfileForm(new List<CharacterProfile>()))
                Draw(profiles, Path.Combine(directory, "profiles.png"));
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
