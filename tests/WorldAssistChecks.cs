using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace TideCombat
{
    internal static class WorldAssistChecks
    {
        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
        }

        private static OcrLine LoginText(string text, int x = 550, int y = 375)
        {
            return new OcrLine { Text = text, Box = new Rectangle(x, y, 90, 30), Score = .58f };
        }

        internal static void LoginSettle()
        {
            var size = new Size(1600, 900);
            var screen = new LoginScreen();
            var clicks = new List<Point>();
            Func<long, OcrLine[], bool> observe = (now, lines) => screen.ObserveText(size, now,
                new List<OcrLine>(lines), () => false, p => { clicks.Add(p); return true; });
            var first = new[] { LoginText("Log In") };
            Require(!observe(0, first) && !observe(3999, first), "wait four seconds before sending any login input");
            Require(!observe(4000, new OcrLine[0]) && clicks.Count == 0, "an auto-login transition cancels the pending click");
            Require(!observe(5000, first), "arm an independent second check");
            var moved = new[] { LoginText("LOGIN TO CONTINUE", 820), LoginText("Log In", 980) };
            Require(observe(9000, moved) && clicks.Count == 1 && clicks[0] == new Point(865, 390),
                "use the first matching button from the fresh frame even after it moves");
            var phone = new[] { LoginText("登录"), LoginText("+86 手机号", 820) };
            Require(!observe(14000, phone) && !observe(18000, first) && clicks.Count == 1, "phone form does not arm a delayed click");
            Require(!observe(22000, phone) && clicks.Count == 1, "phone form appearing on confirmation cancels the delayed click");
            Require(!observe(28000, first), "new frame");
            screen.Reset();
            Require(!observe(33000, first) && clicks.Count == 1, "route switch discards the first login sample");
        }

        internal static void LoginControls()
        {
            var size = new Size(1600, 900);
            var screen = new LoginScreen();
            var clicks = new List<Point>();
            Func<bool, OcrLine[], bool> observe = (icon, lines) => screen.ObserveText(size, 0,
                new List<OcrLine>(lines), () => icon, p => { clicks.Add(p); return true; });
            var start = LoginText("立即进入游戏", 1120, 550);
            OcrLine agree = LoginText("同意"), policy = LoginText("隐私协议", 800);
            Require(!observe(false, new[] { agree, start }), "an unrelated agree dialog owns the current frame");
            Require(observe(false, new[] { agree, policy, start }) && clicks[0] == new Point(595, 390),
                "accept only the privacy dialog with its agree button in the center");
            Require(!observe(false, new[] { agree, LoginText("隐私", 1100), start }), "exclude a privacy label crossing the right edge");
            Require(observe(false, new[] { start }) && clicks[1] == new Point(1165, 565), "allow start near the top of the bottom-right quadrant");
            Require(!observe(false, new[] { start, LoginText("Click to Connect", 1180, 780) }), "bottom-right login text blocks start");
            Require(!observe(true, new[] { LoginText("点击屏幕", 1100, 750) }), "switch icon alone does not enter the game");
            Require(!observe(false, new[] { LoginText("点击屏幕", 630, 750) }), "screen prompt alone does not enter the game");
            Require(observe(true, new[] { LoginText("点击屏幕", 630, 750) }) && clicks[2] == new Point(804, 833),
                "switch icon and bottom-center text click the game entry position");
            Require(LoginScreen.Bounds(new Size(2100,900), new Rectangle(480,270,640,360), true) == new Rectangle(730,270,640,360),
                "ultrawide login dialog stays centered on the game UI");
            bool disconnected;
            var cloud = new List<OcrLine> { LoginText("云服务错误：1020"), LoginText("确定"), LoginText("退出游戏", 830) };
            Require(WorldAssist.CloudDisconnectButton(cloud, out disconnected) == cloud[1].Box && disconnected,
                "reconnect uses first matching visible button on a recognized error");
            cloud[0].Text = "云服务 1020";
            Require(WorldAssist.CloudDisconnectButton(cloud, out disconnected).IsEmpty && !disconnected,
                "an error number without a disconnect message cannot exit the game");
        }

        internal static void LoginIcons(string root)
        {
            string images = Path.Combine(root, "ok_templates");
            var screen = new LoginScreen();
            using (var reader = new FrameReader())
            using (var notice = new Bitmap(Path.Combine(images, "7.png")))
            using (var account = new Bitmap(Path.Combine(images, "18.png")))
            using (var ocr = new OcrReader(OcrReader.Directory))
            {
                var clicks = new List<Point>();
                Func<Point,bool> click = p => { clicks.Add(p); return true; };
                PixelFrame frame = reader.Read(notice,true);
                Require(screen.Observe(frame, 0, f => { throw new InvalidOperationException("notice must precede OCR"); }, click)
                    && Math.Abs(clicks[0].X - 1315) <= 2 && Math.Abs(clicks[0].Y - 155) <= 2, "click archived notice close");
                frame = reader.Read(account,true);
                Require(screen.Observe(frame, 5000, f => new List<OcrLine> { LoginText("点击屏幕", 820, 850) }, click)
                    && clicks.Count == 2 && Math.Abs(clicks[1].X - 805) <= 1 && Math.Abs(clicks[1].Y - 833) <= 1,
                    "archived switch-account icon plus bottom-center text");
                Require(screen.Observe(frame, 10000, f => ocr.Read(f, new Rectangle(0,0,f.Width,f.Height)), click)
                    && clicks.Count == 3 && clicks[1] == clicks[2], "archived account-switch and game entry text from the full screenshot");
            }
        }

        internal static void UpdateRestart()
        {
            var events = new List<string>();
            Func<Func<bool>,bool> update = click => GameUpdateRecovery.Recover(click,
                ms => { events.Add(ms.ToString()); return true; }, () => events.Add("restart"));
            var screen = new LoginScreen(update);
            var lines = new List<OcrLine> { LoginText("遊戲即將重啟"), LoginText("開始", 1190, 710),
                LoginText("確認", 770, 530), LoginText("进入游戏", 1300, 760) };
            Require(screen.ObserveText(new Size(1600,900), 0, lines, () => false,
                p => { events.Add("click " + p.X + " " + p.Y); return true; }), "recognize the client update");
            Require(string.Join(",",events) == "200,click 815 545,60000,restart,30000",
                "wait for update confirmation, relaunch after one minute and allow startup");
            events.Clear();
            lines.RemoveAt(2);
            Require(!screen.ObserveText(new Size(1600,900), 0, lines, () => true, p => true) && events.Count == 0,
                "do not enter the game through an unfinished update dialog");
            bool restarted = false;
            Require(GameUpdateRecovery.Recover(() => true, ms => ms != 60000, () => restarted = true) && !restarted,
                "cancellation after confirmation prevents a delayed relaunch");
            Require(!GameUpdateRecovery.Recover(() => false, ms => true, () => restarted = true) && !restarted,
                "a lost click must not schedule a relaunch");
        }

        internal static void UpdatePackage()
        {
            string test = Path.Combine(Path.GetTempPath(), "ww-login-" + Guid.NewGuid().ToString("N"));
            string game = Path.Combine(test,"Wuthering Waves Game");
            string cache = Path.Combine(test,"kr_game_cache");
            string launcher = Path.Combine(game,"Wuthering Waves.exe");
            string running = Path.Combine(game,"Client","Binaries","Win64","Client-Win64-Shipping.exe");
            string manifest = Path.Combine(game,"launcherDownloadConfig.json"), state = Path.Combine(cache,"platform_ui_state.bin");
            Directory.CreateDirectory(cache);
            Directory.CreateDirectory(game);
            Action<string> write = json => {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0x63;
                File.WriteAllText(state,Convert.ToBase64String(bytes));
            };
            try
            {
                Require(GameUpdateRecovery.LauncherPath(running) == launcher, "find launcher belonging to attached client");
                Require(GameUpdateRecovery.DetectPackage(launcher) == null, "missing game package");
                bool fallback;
                var start = GameUpdateRecovery.StartInfo(launcher, out fallback);
                Require(fallback && start.Arguments == "-krqlv=hd" && start.WorkingDirectory == game &&
                    start.FileName == launcher && !start.UseShellExecute, "direct launch from the attached install defaults to HD");
                var notices = new List<string>();
                foreach (GameLanguage language in new[] { GameLanguage.SimplifiedChinese, GameLanguage.TraditionalChinese, GameLanguage.English })
                    Require(GameUpdateRecovery.PrepareLaunch(launcher, language, notices.Add,
                        System.Globalization.CultureInfo.InvariantCulture).Arguments == "-krqlv=hd",
                        "the actual launch path uses HD for an unavailable bundle regardless of language");
                Require(notices.Count == 3 && notices[0].Contains("高清") && notices[1].Contains("高畫質") && notices[2].Contains("HD assets"),
                    "the default warning follows the three supported client languages");
                foreach (string locale in new[] { "es-ES","ja-JP","ko-KR" })
                    GameUpdateRecovery.PrepareLaunch(launcher, GameLanguage.Unknown, notices.Add,
                        System.Globalization.CultureInfo.GetCultureInfo(locale));
                Require(notices.Count == 6 && notices[3].Contains("recursos HD") && notices[4].Contains("起動") &&
                    notices[5].Contains("시작"), "resource fallback warnings cover all supported locales");
                string legacy = Path.Combine(test, "legacy-settings.json");
                SettingsFile.Save(legacy, CombatSettings.Defaults());
                File.WriteAllText(legacy, File.ReadAllText(legacy).Insert(1, "\"Game Package\":\"sd\",\"GamePackage\":\"sd\","));
                CombatSettings settings = SettingsFile.Load(legacy);
                SettingsFile.Save(legacy, settings);
                Require(File.ReadAllText(legacy).IndexOf("GamePackage", StringComparison.OrdinalIgnoreCase) < 0,
                    "the obsolete manual package setting must not become an override");
                File.Delete(legacy);
                File.WriteAllText(manifest,"{\"bundles\":{\"HD\":{},\"UHD\":{}}}");
                File.WriteAllText(state,"broken file");
                Require(GameUpdateRecovery.DetectPackage(launcher) == null, "ambiguous downloaded bundles");
                File.WriteAllText(manifest,"{\"bundles\":{\"SD\":{},\"unknown\":{}}}");
                Require(GameUpdateRecovery.DetectPackage(launcher) == "sd", "single recognized download survives corrupt UI state");
                const string recent = ",\"recentGames\":{\"Aki\":{\"bundleName\":\"UHD\"}}}";
                write("{\"activeGameScope\":{\"gameIdentity\":\"Other\",\"bundleName\":\"HD\"}" + recent);
                Require(GameUpdateRecovery.DetectPackage(launcher) == "uhd", "ignore current bundle of a different game");
                foreach (string name in new[] {"SD","HD","UHD"})
                {
                    write("{\"activeGameScope\":{\"gameIdentity\":\"Aki\",\"bundleName\":\"" + name + "\"}" + recent);
                    start = GameUpdateRecovery.StartInfo(launcher, out fallback);
                    Require(!fallback && start.Arguments == "-krqlv=" + name.ToLowerInvariant(),
                        "reread selected bundle on each launch");
                    start = GameUpdateRecovery.PrepareLaunch(launcher, GameLanguage.English,
                        value => { throw new InvalidOperationException(value); });
                    Require(start.Arguments == "-krqlv=" + name.ToLowerInvariant(),
                        "the default launch path must reread the selected bundle and avoid unnecessary warnings");
                }
            }
            finally
            {
                if (File.Exists(state)) File.Delete(state);
                if (File.Exists(manifest)) File.Delete(manifest);
                string legacy = Path.Combine(test, "legacy-settings.json");
                if (File.Exists(legacy)) File.Delete(legacy);
                Directory.Delete(cache);
                Directory.Delete(game);
                Directory.Delete(test);
            }
        }

        internal static void LauncherFallback()
        {
            string root = Path.Combine(Path.GetTempPath(), "ww-launcher-" + Guid.NewGuid().ToString("N"));
            string recentFolder = Path.Combine(root, "recent");
            string installedFolder = Path.Combine(root, "Wuthering Waves Game");
            string recent = Path.Combine(recentFolder, "Wuthering Waves.exe");
            string installed = Path.Combine(installedFolder, "Wuthering Waves.exe");
            Directory.CreateDirectory(recentFolder);
            Directory.CreateDirectory(installedFolder);
            try
            {
                File.WriteAllBytes(recent, new byte[0]);
                File.WriteAllBytes(installed, new byte[0]);
                string registered = "\"" + Path.Combine(root, "Wuthering Waves bilibili", "launcher.exe") + "\" /uninstall";
                Require(GameUpdateRecovery.FindLauncherNear(registered) == installed,
                    "registered launcher can be beside the game install");
                Require(GameUpdateRecovery.FindLauncherNear(installedFolder) == installed &&
                    GameUpdateRecovery.FindLauncherNear(Path.Combine(root, "other", "launcher.exe")) == installed,
                    "check the listed, nested, and sibling install locations");
                Require(GameUpdateRecovery.FindLauncherNear("not installed") == null, "reject missing executable");

                Func<string, ulong, KeyValuePair<string, byte[]>> entry = (path, time) =>
                {
                    string encoded = new string(path.Select(value => value >= 'A' && value <= 'Z' ?
                        (char)('A' + (value - 'A' + 13) % 26) : value >= 'a' && value <= 'z' ?
                        (char)('a' + (value - 'a' + 13) % 26) : value).ToArray());
                    var data = new byte[68];
                    Buffer.BlockCopy(BitConverter.GetBytes(time), 0, data, 60, 8);
                    return new KeyValuePair<string, byte[]>(encoded, data);
                };
                string latest = GameUpdateRecovery.MostRecentLauncher(new[]
                {
                    entry(recent, 200),
                    entry(installed, 100),
                    entry(Path.Combine(root, "Wuthering Waves.exe"), 300),
                    entry(Path.Combine(root, "other.exe"), 400)
                });
                Require(latest == recent, "use the newest existing game executable from the launch history");
                int fallbacks = 0;
                Require(GameUpdateRecovery.ResolveLauncher(null, () => latest, () => { fallbacks++; return installed; }) == recent &&
                    fallbacks == 0, "recent launch outranks the registry");
                Require(GameUpdateRecovery.ResolveLauncher(null, () => null, () => { fallbacks++; return installed; }) == installed &&
                    fallbacks == 1, "look up the registered installation only if there is no recent launch");
                Require(GameUpdateRecovery.ResolveLauncher(null, () => null, () => null) == null,
                    "no installation leaves the restart idle");
                string running = Path.Combine(installedFolder, "Client", "Binaries", "Win64", "Client-Win64-Shipping.exe");
                Require(GameUpdateRecovery.ResolveLauncher(running, () => recent, () => null) == installed,
                    "an attached client keeps its own installation");
            }
            finally
            {
                File.Delete(recent);
                File.Delete(installed);
                Directory.Delete(recentFolder);
                Directory.Delete(installedFolder);
                Directory.Delete(root);
            }
        }

        internal static void TrustedRelativeLauncher()
        {
            // Use a real directory rather than a macOS /tmp or /var symlink;
            // the production relative path deliberately rejects reparse escape.
            string temp = Directory.Exists("/private/tmp") ? "/private/tmp" : Path.GetTempPath();
            string root = Path.Combine(temp, "ww-trusted-launcher-" + Guid.NewGuid().ToString("N"));
            string install = Path.Combine(root, "install"), deep = Path.Combine(install, "deep");
            string launcherFolder = Path.Combine(deep, "launcher"), game = Path.Combine(deep, "Wuthering Waves Game");
            string executable = Path.Combine(game, "Wuthering Waves.exe"), ambiguous = Path.Combine(launcherFolder, "Wuthering Waves.exe");
            Directory.CreateDirectory(launcherFolder);
            Directory.CreateDirectory(game);
            try
            {
                File.WriteAllBytes(executable, new byte[0]);
                string relative = Path.Combine("deep", "launcher", "launcher.exe");
                var values = new Dictionary<string, string> { { "InstallLocation", install }, { "LauncherPath", relative } };
                Require(GameUpdateRecovery.FindLauncherNear(relative) == null &&
                    GameUpdateRecovery.FindLauncherNear(relative, install) == executable &&
                    GameUpdateRecovery.ResolveRegisteredRelativeLauncher(values) == executable,
                    "registered relative launcher must use a single verified install root, never current directory");
                Require(GameUpdateRecovery.FindLauncherNear(Path.Combine("..", "escape", "launcher.exe"), install) == null,
                    "relative traversal must remain rejected");
                string unexpected = Path.Combine(game, "Client", "Binaries", "Win64", "other.exe");
                Require(GameUpdateRecovery.LauncherPath(unexpected) == null, "unrecognized process basename must remain rejected");
                values["InstallPath"] = deep;
                Require(GameUpdateRecovery.ResolveRegisteredRelativeLauncher(values) == null, "conflicting installation roots are ambiguous");
                values.Remove("InstallPath");
                File.WriteAllBytes(ambiguous, new byte[0]);
                Require(GameUpdateRecovery.FindLauncherNear(relative, install) == null &&
                    GameUpdateRecovery.ResolveRegisteredRelativeLauncher(values) == null,
                    "multiple relative launcher candidates must not be chosen automatically");
            }
            finally
            {
                File.Delete(ambiguous);
                File.Delete(executable);
                Directory.Delete(game);
                Directory.Delete(launcherFolder);
                Directory.Delete(deep);
                Directory.Delete(install);
                Directory.Delete(root);
            }
        }

        internal static void Rules()
        {
            var options = new WorldAssistOptions();
            Require(WorldAssist.CanPick(false, new[] { new OcrLine { Text = "野花", Score = .9f } }, options), "allow material prompts");
            Require(!WorldAssist.CanPick(false, new[] { new OcrLine { Text = "领取奖励", Score = .9f } }, options), "block reward menus");
            Require(OcrText.Compact("Ａ Ｂ Ｃ１２") == "ABC12", "compact full-width Latin OCR text");
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Require(OcrText.Compact("領 取 獎勵 ＡＢＣ１２") == "领取奖励ABC12", "normalize OCR to simplified Chinese");
                Require(!WorldAssist.CanPick(false, new[] { new OcrLine { Text = "領取獎勵", Score = .9f } }, options), "block traditional Chinese reward menus");
            }
            Require(WorldSession.MenuClickHoldMilliseconds == 200, "coordinate-targeted world menu clicks use reference press duration");
            string browserCode, browserKey;
            CloudBrowser.BrowserKey(0x09, out browserCode, out browserKey);
            Require(browserCode == "Tab" && browserKey == "Tab", "a named jump binding translates consistently to the managed browser");
            CloudBrowser.BrowserKey(0x20, out browserCode, out browserKey);
            Require(browserCode == "Space" && browserKey == " ", "keep the default browser jump binding");
            Require(!WorldAssist.CanPick(true, new[] { new OcrLine { Text = "对话", Score = .9f } }, options), "block unapproved dialogue");
            Require(WorldAssist.CanPick(true, new[] { new OcrLine { Text = "吸收", Score = .9f } }, options), "allow configured echo absorption");
            DateTime hour = new DateTime(2026, 11, 22, 4, 0, 0);
            var schedule = new MonthlyCardSchedule(options);
            Require(!schedule.Due(hour), "a new task schedules its next check only after observing a card");
            schedule.Handled(hour.AddDays(-1));
            Require(!schedule.Due(hour.AddSeconds(-30)) && schedule.Due(hour.AddSeconds(-29)) &&
                schedule.Due(hour.AddSeconds(89)) && !schedule.Due(hour.AddSeconds(90)), "monthly card window boundaries");
            schedule.Handled(hour);
            Require(!schedule.Due(hour.AddSeconds(5)) && schedule.Due(hour.AddDays(1)), "next-day monthly card");
            Require(!schedule.Due(hour.AddDays(2)), "an expired unclaimed window is not silently rescheduled");
            var disabled = new MonthlyCardSchedule(new WorldAssistOptions { CheckMonthlyCard = false });
            disabled.Handled(hour);
            Require(!disabled.Due(hour.AddDays(1)), "disabled schedule stays dormant after a visible card");
            var midnight = new MonthlyCardSchedule(new WorldAssistOptions { MonthlyCardHour = 24 });
            midnight.Handled(hour);
            Require(midnight.Due(hour.Date.AddDays(1)) && !midnight.Due(hour.Date.AddDays(1).AddHours(23)),
                "24 in the reference-style hour field means the next midnight");
            Require(MonthlyCardSchedule.NormalizeHour(0) == 0 && MonthlyCardSchedule.NormalizeHour(24) == 0,
                "legacy 0 and reference 24 denote the same midnight");
            Require(WorldAssist.CanPick(true, new[] { new OcrLine { Text = "吸收领取奖励", Score = .9f } }, options),
                "dialogue checks only the allow list");
            var key = new Rectangle(120, 100, 20, 22);
            Require(WorldAssist.CanPick(true, new[]
            {
                new OcrLine { Text = "对话", Score = .9f, Box = new Rectangle(240, 102, 40, 16) },
                new OcrLine { Text = "吸收", Score = .58f, Box = new Rectangle(240, 132, 40, 16) }
            }, options), "reference checks a recognized second-row whitelist match without another score threshold");
            Require(WorldAssist.PickupTextArea(key) == new Rectangle(224, 83, 140, 121), "the pickup text search is relative to F and contains several rows");
            Require(WorldAssist.CanPick(false, new OcrLine[0], options), "ordinary prompts pass when no blocked text is found");
            Require(!WorldAssist.CanPick(false, new[]
            {
                new OcrLine { Text = "材料", Score = .9f, Box = new Rectangle(240, 102, 40, 16) },
                new OcrLine { Text = "领取奖励", Score = .58f, Box = new Rectangle(240, 132, 40, 16) }
            }, options), "ordinary interactions block a blacklist match anywhere in the search area");
            Require(options.Login && options.SkipDialog && options.PickUp && options.RestoreCursor && !options.Travel, "reference trigger defaults");
            Require(options.StartsAutomatically, "ordinary reference triggers start with the utility by default");
            Require(options.AllowsStartup(GameSource.AutoLocal) && options.AllowsStartup(GameSource.Foreground) &&
                options.AllowsStartup(GameSource.Background) && !options.AllowsStartup(GameSource.Cloud) &&
                !options.AllowsStartup(GameSource.ManagedCloud) && !options.StartsBattleAutomatically,
                "startup defaults do not grant automatic battle or cloud-session input authorization");
            WorldAssistOptions startup = options.ForStartup();
            Require(startup.PickUp && startup.SkipDialog && startup.RestoreCursor && !startup.Travel && !startup.Login && options.Login,
                "the startup copy includes ordinary triggers but never expands configured manual login authorization");
            options.StartManagedCloudWithUtility = true;
            options.StartBattleWithUtility = true;
            startup = options.ForStartup();
            Require(startup.AllowsStartup(GameSource.ManagedCloud) && !startup.AllowsStartup(GameSource.Cloud) &&
                startup.StartsBattleAutomatically && !startup.Login,
                "explicit saved authorization can opt into the dedicated cloud and resident battle, without automatic login");
            options.StartWithUtility = false;
            Require(!options.StartsAutomatically && !options.AllowsStartup(GameSource.ManagedCloud) && !options.StartsBattleAutomatically,
                "the user can disable all resident triggers");
            var observedTravelCandidates = new List<string>();
            int firstTravel = WorldAssist.NextTravelCandidate(0, name => { observedTravelCandidates.Add(name); return name == "fast_travel_custom"; });
            int nextTravel = WorldAssist.NextTravelCandidate(firstTravel + 1, name => { observedTravelCandidates.Add(name); return name == "remove_custom"; });
            Require(firstTravel == 0 && nextTravel == 2 && observedTravelCandidates.SequenceEqual(new[] { "fast_travel_custom", "gray_teleport", "remove_custom" }) &&
                WorldAssist.NextTravelCandidate(nextTravel + 1, name => true) == -1,
                "a candidate disappearing after the settle delay advances in original priority without restarting the gray-map gate");
            Require(WorldAssist.ShouldRestoreCursor(new Point(10, 100), new Point(805, 450), new Point(800, 450)) &&
                !WorldAssist.ShouldRestoreCursor(new Point(710, 450), new Point(803, 450), new Point(800, 450)) &&
                !WorldAssist.ShouldRestoreCursor(new Point(10, 100), new Point(690, 450), new Point(800, 450)),
                "restore only a large jump towards the center");
        }

        internal static void MonthlyCardTiming()
        {
            long time = 0;
            var dismiss = new MonthlyCardDismissal(() => time);
            var taps = new List<long>();
            Func<bool> click = () => { taps.Add(time); time += 20; return true; };
            Require(dismiss.Observe(false, click) == MonthlyCardResult.Waiting && dismiss.Delay == 2000,
                "first click must be followed by two seconds after key release");
            time = 2019;
            Require(dismiss.Observe(true, click) == MonthlyCardResult.Waiting && taps.Count == 1,
                "an early HUD does not remove the second click");
            time = 2020;
            Require(dismiss.Observe(true, click) == MonthlyCardResult.Waiting && taps.Count == 2,
                "second click follows the first delay");
            time = 4039;
            Require(dismiss.Observe(true, click) == MonthlyCardResult.Waiting && taps.Count == 2,
                "the team check waits until the second delay is complete");
            time = 4040;
            Require(dismiss.Observe(true, click) == MonthlyCardResult.ReturnedToTeam && taps.Count == 2,
                "a confirmed team completes without an extra click");
            var rejected = new MonthlyCardDismissal(() => time);
            Require(rejected.Observe(false, () => false) == MonthlyCardResult.InputLost, "a rejected click interrupts dismissal");
        }

        internal static void CursorRestore()
        {
            // The worker's cadence is calculated from this shared pure tracker;
            // neither the unit test nor the idle-worker lifetime touches the OS cursor.
            using (new CursorRestoreSampler()) { }
            var tracker = new CursorResetTracker();
            var bounds = new Rectangle(200, 100, 1600, 900);
            tracker.Observe(GameSource.Background, bounds, 0, () => true, () => new Point(400, 300), point => true);
            Require(tracker.Delay(0) == 2, "the independent idle sample schedules its next observation after 2ms");
            tracker.Observe(GameSource.Background, bounds, 2, () => true, () => new Point(1000, 550), point => true);
            Require(tracker.Delay(2) == 1000, "the independent restore sample keeps the reference one-second cooldown");
            var options = new WorldAssistOptions { PickUp = false, Login = false, SkipDialog = false, Travel = false };
            using (var assist = new WorldAssist(options, new FarmVision()))
            {
                var game = new Rectangle(200, 100, 1600, 900);
                var outside = new Point(400, 300);
                Point current = outside;
                int reads = 0;
                var moves = new List<Point>();
                Func<Point> read = () => { reads++; return current; };
                Func<Point, bool> set = point => { moves.Add(point); return true; };
                Func<bool> activeBackground = () => true;
                assist.UpdateCursor(GameSource.Cloud, game, 0, activeBackground, read, set);
                assist.UpdateCursor(GameSource.ManagedCloud, game, 1, activeBackground, read, set);
                options.RestoreCursor = false;
                assist.UpdateCursor(GameSource.Background, game, 2, activeBackground, read, set);
                Require(reads == 0 && moves.Count == 0, "disabled and cloud modes do not read or move the cursor");

                options.RestoreCursor = true;
                assist.UpdateCursor(GameSource.Background, game, 3, activeBackground, read, set);
                current = new Point(1000, 550);
                assist.UpdateCursor(GameSource.Background, game, 4, activeBackground, read, set);
                Require(reads == 1 && moves.Count == 0, "an idle read keeps the two-millisecond polling interval");
                assist.UpdateCursor(GameSource.Background, game, 5, activeBackground, read, set);
                assist.UpdateCursor(GameSource.Background, game, 1004, activeBackground, read, set);
                Require(reads == 2 && moves.Count == 1 && moves[0] == outside, "restore previous point once for the next second");
                assist.UpdateCursor(GameSource.Background, game, 1005, () => false, read, set);
                Require(reads == 3 && moves.Count == 1, "unavailable or foreground game cannot restore the cursor");
                assist.UpdateCursor(GameSource.Background, game, 1007, activeBackground, read, set);
                Require(reads == 4 && moves.Count == 1, "the same cursor position cannot trigger a second restoration");
                options.RestoreCursor = false;
                assist.UpdateCursor(GameSource.Background, game, 1010, activeBackground, read, set);
                options.RestoreCursor = true;
                assist.UpdateCursor(GameSource.Background, game, 1012, activeBackground, read, set);
                Require(reads == 5, "re-enabling uses the existing polling path");
                assist.UpdateCursor(GameSource.Background, game, 1014, activeBackground,
                    () => { throw new System.ComponentModel.Win32Exception(5); }, set);
                assist.UpdateCursor(GameSource.Background, game, 1016, activeBackground, read, set);
                Require(reads == 5 && moves.Count == 1, "a native failure pauses restoration without stopping battle");
                assist.ResetWindow();
                assist.UpdateCursor(GameSource.Background, game, 1018, activeBackground, read, set);
                Require(reads == 6, "the next window can start cursor polling again");
            }
        }

        internal static void ExcludesAstrite(string root)
        {
            using (var reader = new FrameReader())
            foreach (string path in new[] { Path.Combine(root, "ok_templates", "62.png"),
                Path.Combine(root, "tests", "images", "xinyao_interaction_different_prompt.png") })
            using (var image = new Bitmap(path))
            {
                PixelFrame frame = reader.Read(image);
                var vision = new FarmVision();
                Rectangle key;
                Require(vision.FindPickupKey(frame, out key) && WorldAssist.HasAstriteIcon(frame, vision, key),
                    "guard via the selected prompt's icon in " + Path.GetFileName(path));
            }
        }

        internal static void PickupReplay(string root)
        {
            var options = new WorldAssistOptions { SkipDialog = false, Travel = false, Login = false };
            var hud = new HudSnapshot { Valid = true };
            var vision = new FarmVision();
            Rectangle icon = new Rectangle(1076, 449, 30, 31);
            using (var source = new Bitmap(Path.Combine(root, "ok_templates", "62.png")))
            using (var second = new Bitmap(Path.Combine(root, "tests", "images", "xinyao_interaction_different_prompt.png")))
            using (var reader = new FrameReader())
            using (var assist = new WorldAssist(options, vision))
            using (var ordinary = new Bitmap(source))
            using (var noText = new Bitmap(source))
            using (var blank = new Bitmap(1600, 900))
            {
                using (Graphics draw = Graphics.FromImage(ordinary)) draw.FillRectangle(Brushes.Black, icon);
                using (Graphics draw = Graphics.FromImage(noText)) draw.FillRectangle(Brushes.Black, 1106, 449, 176, 31);
                int count = 0;
                Func<bool> key = () => { count++; return true; };
                Func<Point, bool> click = p => { throw new InvalidOperationException("pickup should send only F"); };
                Action<Bitmap, int, bool> verify = (image, expected, hasAstrite) =>
                {
                    assist.ResetWindow();
                    count = 0;
                    PixelFrame frame = reader.Read(image, true);
                    Rectangle prompt;
                    bool found = vision.FindPickupKey(frame, out prompt);
                    Require(found == !ReferenceEquals(image, blank), "fixture must show the selected F prompt " + image.Size);
                    Require(!found || WorldAssist.HasAstriteIcon(frame, vision, prompt) == hasAstrite,
                        "fixture must preserve selected-row icon state " + image.Size);
                    for (int i = 0; i < 3; i++)
                        assist.Observe(frame, hud, false, 1000 + i * (expected == 0 ? 350 : 200), click, key);
                    Require(count == expected, "unexpected number of pickup events " + count + " at " + image.Size);
                };
                options.PickUpAllow = new[] { "吸收", "Absorb" };
                verify(source, 0, true);
                verify(second, 0, true);
                verify(noText, 0, true);
                verify(blank, 0, false);
                verify(ordinary, 3, false);
                using (var crop = source.Clone(icon, source.PixelFormat))
                foreach (int dy in new[] { -60, 60 })
                using (var moved = new Bitmap(ordinary))
                {
                    using (Graphics draw = Graphics.FromImage(moved)) draw.DrawImageUnscaled(crop, icon.X, icon.Y + dy);
                    verify(moved, 3, false);
                }
                foreach (var size in new[] { new Size(1280,720), new Size(1920,1080), new Size(2560,1440),
                    new Size(3840,2160), new Size(2560,1080) })
                using (var scaled = new Bitmap(size.Width, size.Height))
                {
                    int width = (int)Math.Round(1600 * size.Height / 900.0);
                    using (Graphics draw = Graphics.FromImage(scaled))
                        draw.DrawImage(source, (size.Width - width) / 2, 0, width, size.Height);
                    verify(scaled, 0, true);
                }
                assist.ResetWindow();
                count = 0;
                assist.Observe(reader.Read(ordinary, true), hud, false, 1000, click, key);
                assist.Observe(reader.Read(source, true), hud, false, 1200, click, key);
                assist.Observe(reader.Read(source, true), hud, false, 1400, click, key);
                Require(count == 3, "the reference sends the three-key sequence after the first prompt was approved");
                assist.Observe(reader.Read(source, true), hud, false, 1600, click, key);
                Require(count == 3, "a new initially selected Astrite prompt still does not start a pickup sequence");
                assist.ResetWindow();
                count = 0;
                var staleCombat = new HudSnapshot { Valid = true, EnemyHealth = true, Locked = true };
                assist.Observe(reader.Read(ordinary, true), staleCombat, false, 1800, click, key);
                Require(count == 1, "a verified F prompt with team HUD can run while a stale lock/health bar remains");
                assist.ResetWindow();
                count = 0;
                assist.Observe(reader.Read(ordinary, true), staleCombat, true, 2000, click, key);
                Require(count == 0, "do not send competing input while the battle planner owns the route");
            }
        }

        internal static void PickupBelowReplay(string root)
        {
            var options = new WorldAssistOptions { SkipDialog = false, Travel = false, Login = false };
            var hud = new HudSnapshot { Valid = true };
            using (var source = new Bitmap(Path.Combine(root, "tests", "images", "absorb.png")))
            using (var below = new Bitmap(source))
            using (var astrite = new Bitmap(Path.Combine(root, "ok_templates", "62.png")))
            using (var unrelated = new Bitmap(astrite))
            using (var reader = new FrameReader())
            using (var assist = new WorldAssist(options, new FarmVision()))
            {
                var text = new Rectangle(1342, 540, 57, 29);
                using (var patch = source.Clone(text, source.PixelFormat))
                using (var draw = Graphics.FromImage(below))
                {
                    draw.FillRectangle(Brushes.Black, text);
                    draw.DrawImageUnscaled(patch, text.X, text.Y + 44);
                }
                using (var draw = Graphics.FromImage(unrelated))
                    draw.FillRectangle(Brushes.Black, 1076, 449, 30, 31);
                int keys = 0, scrolls = 0;
                Func<bool> press = () => { keys++; return true; };
                Func<bool> scroll = () => { scrolls++; return true; };
                Func<Point, bool> click = point => { throw new InvalidOperationException("pickup must not click"); };
                Require(assist.Observe(reader.Read(below, true), hud, false, 1000, click, press) == null && keys == 0,
                    "an unsupported scroll route sends no input for a lower row");
                Require(assist.Observe(reader.Read(below, true), hud, false, 2000, click, press,
                    scrollPickup: scroll) == "自动拾取" && keys == 1 && scrolls == 1,
                    "a visible allowed lower row sends one wheel step followed by the first F");
                Require(assist.Observe(reader.Read(source, true), hud, false, 5000, click, press,
                    capturedAt: 2000, scrollPickup: scroll) == null && keys == 1,
                    "an older route capture cannot trigger a follow-up");
                Require(assist.Observe(reader.Read(below, true), hud, false, 6000, click, press,
                    scrollPickup: scroll) == "自动拾取" && keys == 2 && scrolls == 1,
                    "a follow-up F does not require selection confirmation or repeat the wheel");
                Require(assist.Observe(reader.Read(unrelated, true), hud, false, 6200, click, press,
                    scrollPickup: scroll) == "自动拾取" && keys == 3 && scrolls == 1,
                    "the approved sequence completes after three presses");
                Require(assist.Observe(reader.Read(astrite, true), hud, false, 6400, click, press,
                    scrollPickup: scroll) == null && keys == 3 && scrolls == 1,
                    "a new Astrite prompt does not begin another sequence");
            }
        }

        internal static void SkipDialog(string root)
        {
            using (var reader = new FrameReader())
            {
                var vision = new FarmVision();
                foreach (string path in new[] { "ok_templates/47.png", "ok_templates/53.png", "tests/images/21-9_quest_skip.png" })
                using (var image = new Bitmap(Path.Combine(root, path)))
                {
                    Rectangle icon;
                    Require(WorldAssist.FindSkipIcon(reader.Read(image), vision, out icon), "find the story skip icon in " + path);
                }
                foreach (string name in new[] { "33.png", "roccia_air_forte_second.png",
                    "30811235-Windows_Graphics_Capture_3840x2160_1766756085539.751_original.png",
                    "76cbf6ba-BitBlt_True_3840x2160_1751535646038.8118_original.png" })
                using (var image = new Bitmap(Path.Combine(root, "ok_templates", name)))
                {
                    Rectangle button;
                    Require(!WorldAssist.FindSkipConfirmation(reader.Read(image), vision, out button),
                        "reject combat and challenge settlement as story summaries " + name);
                }
                foreach (string path in new[] { "ok_templates/10.png", "ok_templates/9.png", "tests/images/skip_quest_confirm_21_9.jpg" })
                using (var image = new Bitmap(Path.Combine(root, path)))
                {
                    PixelFrame frame = reader.Read(image);
                    Rectangle button;
                    Require(WorldAssist.FindSkipConfirmation(frame, vision, out button) && button.X > frame.Width / 2,
                        "find the right-hand skip confirmation in " + path);
                }
                using (var image = new Bitmap(Path.Combine(root, "ok_templates/10.png")))
                {
                    using (Graphics draw = Graphics.FromImage(image)) draw.FillRectangle(Brushes.Black, 0, 0, image.Width / 2, image.Height);
                    PixelFrame frame = reader.Read(image);
                    Rectangle button;
                    Require(WorldAssist.FindSkipConfirmation(frame, vision, out button, knownStory: true) && button.X > frame.Width / 2,
                        "a visible summary confirmation is enough after a known story skip even if the Continue Watching button is obscured");
                }
            }
        }

        internal static void WideSkip(string root)
        {
            var options = new WorldAssistOptions { SkipDialog = true, Travel = false, Login = false, PickUp = false };
            var vision = new FarmVision();
            var hud = new HudSnapshot();
            using (var reader = new FrameReader())
            using (var original = new Bitmap(Path.Combine(root, "tests", "images", "21-9_quest_skip.png")))
            using (var assist = new WorldAssist(options, vision))
            {
                foreach (Size size in new[] { new Size(2560, 1080), new Size(3440, 1440), new Size(5120, 2160) })
                using (var image = new Bitmap(original, size))
                using (var blank = new Bitmap(size.Width, size.Height))
                {
                    assist.ResetWindow();
                    var clicks = new List<Point>();
                    Func<Point, bool> click = point => { clicks.Add(point); return true; };
                    PixelFrame frame = reader.Read(image);
                    Rectangle icon;
                    Require(WorldAssist.FindSkipIcon(frame, vision, out icon) &&
                        Math.Abs(icon.X - 179 * 900.0 / 2160) <= 5 && Math.Abs(icon.Y - 104 * 900.0 / 2160) <= 5,
                        "locate the tinted skip glyph at the same left anchor " + size);
                    Require(assist.Observe(frame, hud, false, 0, click, () => false) == "自动跳过剧情" &&
                        clicks.Count == 1 && icon.Contains(clicks[0]), "send one click to the recognized glyph");
                    foreach (Color color in new[] { Color.Black, Color.FromArgb(100, 100, 100), Color.White, Color.FromArgb(100, 140, 160) })
                    {
                        using (Graphics graphics = Graphics.FromImage(blank)) graphics.Clear(color);
                        frame = reader.Read(blank);
                        Require(!WorldAssist.FindSkipIcon(frame, vision, out icon) &&
                            assist.Observe(frame, hud, false, 300, click, () => false) == null && clicks.Count == 1,
                            "ignore flat backgrounds before issuing a skip");
                    }
                }
            }
        }

        internal static void DialogActions(string root)
        {
            var options = new WorldAssistOptions { PickUp = false, Travel = false, Login = false };
            string templates = Path.Combine(root, "ok_templates");
            using (var reader = new FrameReader())
            using (var assist = new WorldAssist(options, new FarmVision()))
            using (var generic = new Bitmap(Path.Combine(templates, "12.png")))
            using (var empty = new Bitmap(1600, 900))
            {
                var hud = new HudSnapshot();
                var clicks = new List<Point>();
                Func<Point, bool> click = p => { clicks.Add(p); return true; };
                Func<bool> key = () => { throw new InvalidOperationException("dialog requested a keyboard event"); };
                using (var skip = new Bitmap(Path.Combine(templates, "47.png")))
                using (var summary = new Bitmap(Path.Combine(templates, "10.png")))
                {
                    PixelFrame visible = reader.Read(skip);
                    assist.Observe(visible, hud, false, 0, click, key);
                    assist.Observe(visible, hud, false, 199, click, key);
                    assist.Observe(visible, hud, false, 200, click, key);
                    Require(clicks.Count == 2, "repeat a visible skip icon at 200ms intervals");
                    assist.Observe(reader.Read(empty), hud, false, 400, click, key);
                    Require(clicks.Count == 2, "wait on fresh frames while the summary appears");
                    PixelFrame confirmed = reader.Read(summary);
                    assist.Observe(confirmed, hud, false, 600, click, key);
                    assist.Observe(confirmed, hud, false, 800, click, key);
                    Require(clicks.Count == 3 && clicks[2].X > confirmed.Width / 2,
                        "the confirmation window starts when the skip icon disappears");
                }
                foreach (string name in new[] { "10.png", "9.png", "../tests/images/skip_quest_confirm_21_9.jpg" })
                using (var image = new Bitmap(Path.Combine(templates, name)))
                {
                    assist.ResetWindow();
                    clicks.Clear();
                    PixelFrame frame = reader.Read(image);
                    assist.Observe(frame, hud, false, 10000, click, key);
                    assist.Observe(frame, hud, false, 10199, click, key);
                    Require(clicks.Count == 0, "wait for story confirmation to settle " + name);
                    assist.Observe(frame, hud, false, 10200, click, key);
                    Require(clicks.Count == 1 && clicks[0].X > frame.Width / 2, "finish a leftover summary " + name);
                }
                foreach (string name in new[] { "11.png", "19.png" })
                using (var image = new Bitmap(Path.Combine(templates, name)))
                {
                    assist.ResetWindow();
                    clicks.Clear();
                    PixelFrame frame = reader.Read(image);
                    assist.Observe(frame, hud, false, 10000, click, key);
                    assist.Observe(frame, hud, false, 10499, click, key);
                    Require(clicks.Count == 0, "checkbox stability " + name);
                    assist.Observe(frame, hud, false, 10500, click, key);
                    Require(clicks.Count == 1 && clicks[0].X < frame.Width / 2, "first click is the checkbox " + name);
                    assist.Observe(frame, hud, false, 10999, click, key);
                    Require(clicks.Count == 1, "confirm stability " + name);
                    // Ticking the box can replace its template. Keep the rest of the dialog unchanged.
                    using (var changed = new Bitmap(image))
                    {
                        using (Graphics g = Graphics.FromImage(changed))
                        {
                            float scale = changed.Height / (float)frame.Height;
                            Point check = clicks[0];
                            g.FillRectangle(Brushes.Gray, (check.X - 32) * scale, (check.Y - 28) * scale, 64 * scale, 56 * scale);
                        }
                        assist.Observe(reader.Read(changed), hud, false, 11000, click, key);
                    }
                    Require(clicks.Count == 2 && clicks[1].X > frame.Width / 2, "confirm after the checkbox changes " + name);
                    assist.ResetWindow();
                    clicks.Clear();
                    assist.Observe(reader.Read(image), hud, false, 0, click, key);
                    assist.Observe(reader.Read(empty), hud, false, 600, click, key);
                    Require(clicks.Count == 0, "a vanished dialog cancels the pending click " + name);
                }
                assist.ResetWindow();
                clicks.Clear();
                PixelFrame other = reader.Read(generic);
                for (int at = 0; at <= 2000; at += 500) assist.Observe(other, hud, false, at, click, key);
                Require(clicks.Count == 0, "ordinary confirmation with no story checkbox");
            }
        }

        internal static void TravelConfirmation(string root)
        {
            var options = new WorldAssistOptions { Travel = true, SkipDialog = false, Login = false, PickUp = false };
            string templates = Path.Combine(root, "ok_templates");
            using (var map = new Bitmap(Path.Combine(templates, "travel.png")))
            using (var dialog = new Bitmap(Path.Combine(templates, "19.png")))
            using (var reader = new FrameReader())
            using (var assist = new WorldAssist(options, new FarmVision()))
            {
                var clicks = new List<Point>();
                Func<Point, bool> click = p => { clicks.Add(p); return true; };
                var hud = new HudSnapshot();
                PixelFrame frame = reader.Read(map);
                assist.Observe(frame, hud, false, 0, click, () => false);
                assist.Observe(frame, hud, false, 500, click, () => false);
                Require(clicks.Count == 1 && clicks[0].Y > frame.Height * .85, "click the map Travel button");
                frame = reader.Read(dialog);
                assist.Observe(frame, hud, false, 1500, click, () => false);
                assist.Observe(frame, hud, false, 2000, click, () => false);
                assist.Observe(frame, hud, false, 2500, click, () => false);
                Require(clicks.Count == 3 && clicks[1].X < frame.Width / 2 && clicks[2].X > frame.Width / 2,
                    "check the compact travel dialog before confirming");
                var ui = new FarmVision().Read(frame, true);
                Require(ui.SkipDialog && ui.SkipCheckBox.Contains(clicks[1]) && ui.ConfirmBox.Contains(clicks[2]),
                    "domain and farm navigation detect both compact controls on the same frame");
                // Do not require the team HUD to have vanished if the map
                // supplies independent evidence of its travel button.
                assist.ResetWindow();
                clicks.Clear();
                var staleTeam = new HudSnapshot { Valid = true };
                frame = reader.Read(map);
                assist.Observe(frame, staleTeam, false, 0, click, () => false);
                assist.Observe(frame, staleTeam, false, 500, click, () => false);
                Require(clicks.Count == 1 && clicks[0].Y > frame.Height * .85, "map evidence still works when the HUD was retained");
                // The reference after_sleep is measured after the emitted
                // input completed, not from the frame's pre-click timestamp.
                long time = 0;
                using (var timed = new WorldAssist(options, new FarmVision(), clock: () => time))
                {
                    var timedClicks = new List<Point>();
                    Func<Point, bool> slowClick = p => { timedClicks.Add(p); time += 200; return true; };
                    timed.Observe(reader.Read(map), hud, false, time, slowClick, () => false);
                    time = 500;
                    timed.Observe(reader.Read(map), hud, false, time, slowClick, () => false);
                    Require(timedClicks.Count == 1, "the candidate click follows its 500ms settle check");
                    time = 1500;
                    Require(timed.Observe(reader.Read(dialog), hud, false, time, slowClick, () => false) == null && timedClicks.Count == 1,
                        "the next stage is blocked until one second after click release");
                    time = 1700;
                    timed.Observe(reader.Read(dialog), hud, false, time, slowClick, () => false);
                    time = 2200;
                    timed.Observe(reader.Read(dialog), hud, false, time, slowClick, () => false);
                    Require(timedClicks.Count == 2 && timedClicks[1].X < frame.Width / 2, "the optional checkbox follows its post-release settle delay");
                    time = 2700;
                    timed.Observe(reader.Read(dialog), hud, false, time, slowClick, () => false);
                    Require(timedClicks.Count == 2, "the checkbox's hold duration must not shorten the 500ms delay");
                    time = 2900;
                    timed.Observe(reader.Read(dialog), hud, false, time, slowClick, () => false);
                    Require(timedClicks.Count == 3, "the confirmed travel prompt completes on its reference delay");
                }
            }
        }

        internal static void ChallengeMarkers(string root)
        {
            foreach (string name in new[] { "abyss_start", "abyss_tower_start" })
            using (var original = new Bitmap(Path.Combine(root, "ok_templates", name + ".png")))
            using (var normalized = new Bitmap(original, 1600, 900))
            using (var inputKey = new Bitmap(Path.Combine(root, "assets", "templates", "pick_up_f_hcenter_vcenter_3840x2160_2441_1090.png")))
            using (var reader = new FrameReader())
            using (var image = new Bitmap(1600, 900))
            {
                VisualPattern marker = System.Linq.Enumerable.First(PatternBank.For(name == "abyss_start" ? "abyss_start_marker" : "abyss_tower_start_marker"));
                Rectangle src = marker.Map(1600, 0);
                Rectangle key = System.Linq.Enumerable.First(PatternBank.For("pick_up_f_hcenter_vcenter")).Map(1600, 0);
                var vision = new CombatVision(CombatSettings.Defaults());
                var dest = new Rectangle(1010, 360, src.Width, src.Height);
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);
                    g.DrawImage(inputKey, key);
                    g.DrawImage(normalized, dest, src, GraphicsUnit.Pixel);
                    var hud = new HudSnapshot();
                    vision.ReadScene(reader.Read(image), hud);
                    Require(hud.ChallengeInteractVisible && hud.ChallengePrompt, "off-center joint sighting " + name + ", F=" + hud.ChallengeInteractVisible + ", marker=" + hud.ChallengeMarker);
                    g.FillRectangle(Brushes.Black, key);
                    hud = new HudSnapshot();
                    vision.ReadScene(reader.Read(image), hud);
                    Require(hud.ChallengeMarker && !hud.ChallengeInteractVisible, "marker lingers after F vanishes " + name);
                    g.FillRectangle(Brushes.Black, dest);
                    g.DrawImage(normalized, new Rectangle(500, 300, src.Width, src.Height), src, GraphicsUnit.Pixel);
                    hud = new HudSnapshot();
                    vision.ReadScene(reader.Read(image), hud);
                    Require(!hud.ChallengeMarker, "disappearance only checks near the last marker " + name);
                    g.Clear(Color.Black);
                    g.DrawImage(inputKey, key);
                    g.DrawImage(normalized, new Rectangle(55, 360, src.Width, src.Height), src, GraphicsUnit.Pixel);
                    hud = new HudSnapshot();
                    vision.ReadScene(reader.Read(image), hud);
                    Require(hud.ChallengeInteractVisible && !hud.ChallengeMarker, "exclude the left quest list " + name);
                }
            }
        }

        internal static void PartyTemplateFallback(string root)
        {
            string templates = Path.Combine(root, "ok_templates");
            string images = Path.Combine(root, "tests", "images");
            var settings = CombatSettings.Defaults();
            using (var reader = new FrameReader())
            using (var source = new Bitmap(Path.Combine(templates, "55.png")))
            {
                PixelFrame frame = reader.Read(source);
                Require(!PartyHud.Read(frame, new HudSnapshot()), "test requires an ambiguous generic party badge");
                HudSnapshot hud = new HudVision(settings).Read(frame);
                Require(hud.Valid && hud.PartySize == 3 && hud.Current == 1,
                    "two bright number templates confirm the active middle slot");
                Require(new HudVision(settings).Read(frame, cloud: true).Valid,
                    "the reference uses the same team numeral rule on PC and cloud");

                using (var withoutSkills = new Bitmap(source, 1600, 900))
                using (Graphics paint = Graphics.FromImage(withoutSkills))
                {
                    paint.FillRectangle(Brushes.Black, new Rectangle(1280, 825, 320, 75));
                    HudSnapshot unavailable = new HudVision(settings).Read(reader.Read(withoutSkills));
                    Require(!unavailable.Valid, "a recognizable party cannot substitute for a visible skill bar");
                }
            }

            foreach (string path in new[]
            {
                Path.Combine(templates, "664081ea-WindowsGraphicsCaptureMethod_3840x2160_title_None_Client-Win64-Shippin_HFdZtL0.png"),
                Path.Combine(images, "big_map.png"),
                Path.Combine(templates, "hiyuki3.png")
            })
            using (var reader = new FrameReader())
            using (var source = new Bitmap(path))
                Require(!new HudVision(settings).Read(reader.Read(source)).Valid,
                    "unverified teammate, map or health evidence must not promote " + Path.GetFileName(path));
        }

        internal static void LevitatorEntryWithShortBars(string root)
        {
            foreach (string name in new[] { "in_combat.png", "in_combat3.png" })
            using (var source = new Bitmap(Path.Combine(root, "tests", "images", name)))
            using (var image = new Bitmap(1600, 900, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            using (var reader = new FrameReader())
            using (Graphics paint = Graphics.FromImage(image))
            {
                paint.DrawImage(source, new Rectangle(0, 0, 1600, 900));
                var vision = new HudVision(CombatSettings.Defaults());
                HudSnapshot entry = vision.Read(reader.Read(image), allowShortEnemyBar: true, activeFight: false);
                Require(entry.Valid && entry.LevitatorKnown && entry.Airborne == (name == "in_combat.png"),
                    "allowing short enemy bars must not suppress entry-time levitator recognition");
                paint.FillRectangle(Brushes.Black, new Rectangle(1256, 786, 34, 34));
                HudSnapshot airborne = vision.Read(reader.Read(image), allowShortEnemyBar: true, activeFight: true);
                Require(airborne.Valid && airborne.LevitatorKnown && airborne.Airborne,
                    "an active fight must retain a recognized levitator even after its icon dims");
            }
        }

        internal static void CloudReconnect(string root)
        {
            string images = Path.Combine(root, "tests", "images");
            using (var reader = new FrameReader(true))
            using (var world = new Bitmap(Path.Combine(images, "cloud_world_unlocked.png")))
            using (var launcher = new Bitmap(Path.Combine(images, "cloud_launcher_home.png")))
            foreach (string name in new[] { "cloud_disconnect_1020.png", "cloud_disconnect_4012.png" })
            using (var disconnected = new Bitmap(Path.Combine(images, name)))
            using (var assist = new WorldAssist(new WorldAssistOptions { Login = true }, new FarmVision()))
            {
                var clicks = new List<Point>();
                Func<Point, bool> click = point => { clicks.Add(point); return true; };
                Func<bool> pressF = () => { throw new InvalidOperationException("recovery must use visible buttons"); };
                var hud = new HudVision(CombatSettings.Defaults());
                PixelFrame ready = reader.Read(world);
                HudSnapshot readyHud = hud.Read(ready, cloud: true);
                Require(readyHud.Valid && assist.Observe(ready, readyHud, false, 0, click, pressF, cloud: true) == null,
                    "entering the world records a completed login");
                PixelFrame frame = reader.Read(disconnected);
                HudSnapshot loading = hud.Read(frame, cloud: true);
                Require(assist.Observe(frame, loading, false, 500, click, pressF) == null && clicks.Count == 0,
                    "cloud recovery is restricted to cloud modes");
                Require(assist.Observe(frame, loading, false, 1000, point => false, pressF, cloud: true) == null,
                    "a failed button click does not mark the disconnect as handled");
                frame = reader.Read(launcher);
                Require(assist.Observe(frame, hud.Read(frame, cloud: true), false, 1500, click, pressF, cloud: true) == null &&
                    clicks.Count == 0, "a failed error-dismiss click preserves the completed login state");
                frame = reader.Read(disconnected);
                loading = hud.Read(frame, cloud: true);
                Require(assist.Observe(frame, loading, false, 2000, click, pressF, cloud: true) == "处理云游戏断线" && clicks.Count == 1,
                    "recognize and dismiss the original disconnection modal " + name);
                frame = reader.Read(launcher);
                HudSnapshot home = hud.Read(frame, cloud: true);
                Require(assist.Observe(frame, home, false, 3000, click, pressF, cloud: true) == null && clicks.Count == 1,
                    "wait for the launcher to settle after dismissing an error");
                Require(assist.Observe(frame, home, false, 5500, click, pressF, cloud: true) == "自动进入游戏" && clicks.Count == 2,
                    "re-enter the game from the original cloud launcher after a previously completed login");
            }
        }
    }
}
