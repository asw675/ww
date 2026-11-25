using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    public sealed class WorldAssistOptions
    {
        public WorldAssistOptions()
        {
            PickUp = Login = SkipDialog = RestoreCursor = true;
        }

        internal static readonly string[] DefaultAllow = { "吸收", "Absorb" };
        internal static readonly string[] DefaultBlock = { "开始合成", "领取奖励", "Claim", "合成台", "开始烹饪" };

        public bool PickUp { get; set; }
        public bool SkipDialog { get; set; }
        public bool Login { get; set; }
        public bool Travel { get; set; }
        public bool? CheckMonthlyCard { get; set; }
        public int? MonthlyCardHour { get; set; }
        public bool RestoreCursor { get; set; }
        public bool? StartWithUtility { get; set; }
        public bool? StartManagedCloudWithUtility { get; set; }
        public bool? StartBattleWithUtility { get; set; }
        public string[] PickUpAllow { get; set; }
        public string[] PickUpBlock { get; set; }

        internal bool NeedsVision { get { return PickUp || SkipDialog || Login || Travel; } }
        internal bool StartsAutomatically { get { return StartWithUtility != false; } }
        internal bool StartsBattleAutomatically { get { return StartsAutomatically && StartBattleWithUtility == true; } }

        internal bool AllowsStartup(GameSource source)
        {
            if (!StartsAutomatically || source == GameSource.Cloud) return false;
            return source == GameSource.ManagedCloud ? StartManagedCloudWithUtility == true :
                source == GameSource.Foreground || source == GameSource.Background || source == GameSource.AutoLocal;
        }

        internal WorldAssistOptions ForStartup()
        {
            // Ordinary reference triggers can be resident at app start. Keep
            // session/login/recovery actions restricted to the existing manual
            // start path; startup must not expand account-access authorization.
            return new WorldAssistOptions
            {
                StartWithUtility = StartWithUtility, StartManagedCloudWithUtility = StartManagedCloudWithUtility,
                StartBattleWithUtility = StartBattleWithUtility, PickUp = PickUp, SkipDialog = SkipDialog,
                Login = false, Travel = Travel, RestoreCursor = RestoreCursor,
                CheckMonthlyCard = CheckMonthlyCard, MonthlyCardHour = MonthlyCardHour,
                PickUpAllow = PickUpAllow == null ? null : (string[])PickUpAllow.Clone(),
                PickUpBlock = PickUpBlock == null ? null : (string[])PickUpBlock.Clone()
            };
        }
    }

    internal sealed class MonthlyCardSchedule
    {
        private readonly bool enabled;
        private readonly int hour;
        private DateTime start;

        internal MonthlyCardSchedule(WorldAssistOptions options)
        {
            enabled = options == null || options.CheckMonthlyCard != false;
            hour = NormalizeHour(options == null ? 4 : options.MonthlyCardHour ?? 4);
        }

        // The reference exposes hours 1–24. Treat 24 as midnight instead of
        // allowing its invalid datetime.replace(hour=24) path to throw.
        internal static int NormalizeHour(int hour) { return hour == 24 ? 0 : Math.Max(0, Math.Min(23, hour)); }

        internal bool Due(DateTime now)
        {
            return enabled && start != default(DateTime) && now > start && now < start.AddSeconds(120);
        }

        internal void Handled(DateTime now) { start = now.Date.AddDays(1).AddHours(hour).AddSeconds(-30); }
    }

    internal enum MonthlyCardResult { Waiting, ReturnedToTeam, TimedOut, InputLost }

    internal sealed class MonthlyCardDismissal
    {
        private readonly Func<long> clock;
        private int clicks;
        private long next, deadline;

        internal MonthlyCardDismissal(Func<long> clock) { this.clock = clock; }
        internal int Delay { get { return (int)Math.Max(0, next - clock()); } }

        internal MonthlyCardResult Observe(bool team, Func<bool> click)
        {
            long now = clock();
            if (now < next) return MonthlyCardResult.Waiting;
            if (clicks >= 2)
            {
                if (team) return MonthlyCardResult.ReturnedToTeam;
                if (now >= deadline) return MonthlyCardResult.TimedOut;
            }
            if (!click()) return MonthlyCardResult.InputLost;
            clicks++;
            next = clock() + (clicks <= 2 ? 2000 : 1000);
            if (clicks == 2) deadline = next + 10000;
            return MonthlyCardResult.Waiting;
        }

        internal static Point Button(PixelFrame frame) { return new Point(frame.Width / 2, (int)(frame.Height * .89)); }
    }

    internal sealed class WorldAssist : IDisposable
    {
        private static readonly string[] TravelButtons = { "Travel", "Fast Travel", "快速旅行", "前往", "Proceed" };
        private static readonly string[] CloudDisconnectText = { "云服务错误", "服务器断开", "断开连接" };
        private static readonly string[] SkipIcons = { "skip_dialog", "skip_dialog_new" };
        private static readonly string[] SkipConfirmations = { "skip_quest_confirm", "skip_quest_confirm_new" };
        private static readonly double[] WideUiScales = { 1, .75 };
        private readonly FarmVision vision;
        private readonly WorldAssistOptions options;
        private readonly OcrReader ocr;
        private readonly bool ownsOcr;
        private readonly bool recoveringLogin;
        private readonly Func<long> clock;
        private long nextAction, nextLogin;
        private long reconnectUntil;
        private readonly LoginScreen login;
        private bool entered;
        private enum DialogAction { None, Check, Confirm, Summary }
        private DialogAction pendingDialog;
        private Rectangle pendingButton;
        private readonly CursorResetTracker cursorTracker = new CursorResetTracker();
        private CursorRestoreSampler cursorSampler;
        private int pendingPickups;
        private long skipConfirmUntil;
        private static readonly string[] TravelIcons = { "fast_travel_custom", "gray_teleport", "remove_custom" };
        private string travelIcon;
        private int travelStep;
        private int travelCandidate = -1;
        private long travelUntil;

        internal WorldAssist(WorldAssistOptions options, FarmVision vision, OcrReader sharedOcr = null, GameUpdateRecovery updates = null,
            bool recoveringLogin = false, Func<long> clock = null)
        {
            this.options = options;
            this.vision = vision;
            login = new LoginScreen(updates == null ? (Func<Func<bool>,bool>)null : updates.Confirm);
            this.recoveringLogin = recoveringLogin;
            this.clock = clock;
            ocr = sharedOcr;
            ownsOcr = sharedOcr == null;
            if (options.NeedsVision && ownsOcr)
            {
                try
                {
                    string directory = OcrReader.Directory;
                    if (File.Exists(Path.Combine(directory, "det.onnx")))
                        ocr = new OcrReader(directory);
                }
                catch (Exception error) { RunLog.Error("战斗辅助", "OCR 未能初始化", error); }
            }
        }

        internal static WorldAssist CreateCursorOnly(WorldAssistOptions options, FarmVision vision)
        {
            return options == null || !options.RestoreCursor ? null :
                new WorldAssist(new WorldAssistOptions { PickUp = false, Login = false,
                    SkipDialog = false, Travel = false, RestoreCursor = true }, vision);
        }

        internal void ResetWindow()
        {
            entered = false;
            cursorTracker.Reset();
            ResetCursorRoute();
            nextLogin = nextAction = 0;
            reconnectUntil = 0;
            pendingDialog = DialogAction.None;
            skipConfirmUntil = 0;
            login.Reset();
            ResetPickups();
            travelStep = 0;
            travelIcon = null;
            travelCandidate = -1;
        }

        internal void ResetPickups()
        {
            pendingPickups = 0;
        }

        internal bool NeedsNativeFrame(long captureTime) { return options.NeedsVision && captureTime >= nextAction; }
        private long ActionTime(long observedTime) { return clock == null ? observedTime : Math.Max(observedTime, clock()); }

        internal string Observe(PixelFrame frame, HudSnapshot hud, bool fighting, long now,
            Func<Point, bool> click, Func<bool> pressF, bool cloud = false, long? capturedAt = null, Func<bool> scrollPickup = null)
        {
            if (!options.NeedsVision)
                return null;
            bool inTeam = !hud.Dead && (hud.Valid || vision.HasTeamNumbers(frame));
            if (options.Login && !entered && !hud.Dead && vision.HasTeamNumbers(frame))
            {
                entered = true;
                reconnectUntil = 0;
                login.Reset();
            }
            if (inTeam)
            {
                skipConfirmUntil = 0;
                // A map can briefly retain the previous team HUD. Do not drop
                // a validated travel sequence just because that HUD is visible.
                if (!options.Travel || travelStep == 0)
                {
                    pendingDialog = DialogAction.None;
                    travelStep = 0;
                }
            }
            if (hud.Dead || fighting)
            {
                pendingDialog = DialogAction.None;
                skipConfirmUntil = 0;
                travelStep = 0;
                ResetPickups();
                return null;
            }
            if (!NeedsNativeFrame(capturedAt ?? now))
                return null;
            if (inTeam)
            {
                if (options.Travel && !hud.EnemyHealth && !hud.Locked)
                {
                    if (TryTravel(frame, now, click)) { ResetPickups(); return "自动快速旅行"; }
                    if (travelStep != 0) { ResetPickups(); return null; }
                }
                if (pendingPickups > 0)
                {
                    if (!pressF()) { pendingPickups = 0; return null; }
                    pendingPickups--;
                    nextAction = ActionTime(now) + 200;
                    return "自动拾取";
                }
                if (options.PickUp && ocr != null)
                {
                    string pickup = TryPick(frame, now, pressF, scrollPickup);
                    if (pickup != null) return pickup;
                }
                nextAction = now + 350;
                return null;
            }

            ResetPickups();
            if (hud.EnemyHealth || hud.Locked) return null;
            nextAction = now + 400;
            bool disconnected = false;
            bool relogging = recoveringLogin || now < reconnectUntil;
            if (options.Login && !relogging && TryReconnect(frame, now, cloud, click, out disconnected))
                return "处理云游戏断线";
            if (options.Login && disconnected)
                return null;
            if (options.Travel && travelStep != 0)
                return TryTravel(frame, now, click) ? "自动快速旅行" : null;
            if (options.SkipDialog)
            {
                if (TrySkip(frame, now, click)) return "自动跳过剧情";
                if (skipConfirmUntil > now) return null;
            }
            if (options.Travel)
            {
                if (TryTravel(frame, now, click)) return "自动快速旅行";
                if (travelStep != 0) return null;
            }
            bool confirmDue = login.ConfirmationTime >= 0;
            if (options.Login && !entered && now >= (confirmDue ? login.ConfirmationTime : nextLogin))
            {
                if (!confirmDue) nextLogin = now + (relogging ? 0 : 5000);
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                if (login.Observe(frame, now, source => Read(source, new Rectangle(0, 0, source.Width, source.Height)), click))
                {
                    nextLogin = Math.Max(nextLogin, now + elapsed.ElapsedMilliseconds + login.AfterClickDelay);
                    return "自动进入游戏";
                }
            }
            return null;
        }

        private bool TryTravel(PixelFrame frame, long now, Func<Point, bool> click)
        {
            Rectangle button;
            // The reference checks each candidate in order, waits 500ms and
            // continues to the next candidate if the first one disappeared.
            // Do not restart the initial gray-map/OCR gate midway through it.
            if (travelStep == 1)
            {
                travelStep = 0;
                if (!vision.FindLabel(frame, travelIcon, .70, out button))
                {
                    QueueTravelCandidate(frame, travelCandidate + 1, now);
                    return false;
                }
                if (!click(Center(button))) return false;
                travelStep = travelIcon == "fast_travel_custom" ? 2 : travelIcon == "gray_teleport" ? 5 : 0;
                nextAction = ActionTime(now) + 1000;
                travelUntil = nextAction + (travelStep == 5 ? 3000 : 2000);
                return true;
            }
            if (travelStep != 0 && now > travelUntil && pendingDialog == DialogAction.None)
            {
                if (travelStep == 2 || travelStep == 4)
                {
                    travelStep = 5;
                    travelUntil = now + 3000;
                }
                else travelStep = 0;
            }
            if (travelStep == 5)
            {
                bool handled = ObserveDialog(frame, now, click, false);
                if (handled && pendingDialog == DialogAction.None) travelStep = 0;
                return handled;
            }
            if (travelStep >= 2)
            {
                if (!vision.FindOptionalButton(frame, .60, out button)) return false;
                if (travelStep == 2)
                {
                    if (!click(new Point((int)(frame.Width * .49), (int)(frame.Height * .55)))) return false;
                    travelStep = 3;
                }
                else
                {
                    if (!click(Center(button))) return false;
                    travelStep++;
                }
                nextAction = ActionTime(now) + 500;
                travelUntil = nextAction + (travelStep == 4 ? 1000 : travelStep == 5 ? 3000 : 2000);
                return true;
            }
            if (!vision.FindLabel(frame, "gray_teleport", .76, out button)) return false;
            var labels = Read(frame, Region(frame, .70, .89, 1, 1));
            if (!labels.Any(line => ContainsAny(new[] { line.Text ?? "" }, TravelButtons)))
                return false;
            QueueTravelCandidate(frame, 0, now);
            return false;
        }

        internal static int NextTravelCandidate(int start, Func<string, bool> visible)
        {
            for (int i = Math.Max(0, start); i < TravelIcons.Length; i++)
                if (visible(TravelIcons[i])) return i;
            return -1;
        }

        private void QueueTravelCandidate(PixelFrame frame, int start, long now)
        {
            Rectangle ignored;
            travelCandidate = NextTravelCandidate(start, icon => vision.FindLabel(frame, icon, .70, out ignored));
            if (travelCandidate < 0)
            {
                travelStep = 0;
                travelIcon = null;
                return;
            }
            travelIcon = TravelIcons[travelCandidate];
            travelStep = 1;
            nextAction = ActionTime(now) + 500;
            travelUntil = nextAction + 2000;
        }

        private string TryPick(PixelFrame frame, long now, Func<bool> pressF, Func<bool> scrollPickup)
        {
            Rectangle key, ignored;
            if (!vision.FindPickupKey(frame, out key)) return null;
            if (!WhiteEnough(frame, key) || HasAstriteIcon(frame, vision, key)) return null;
            Rectangle dot = new Rectangle(key.X + key.Width * 3, key.Y - key.Height, key.Width * 3, key.Height * 3);
            bool hasDialog = vision.FindFirst(frame, "dialog_3_dots", dot, 1, .60, .64, out ignored);
            var textArea = PickupTextArea(key);
            OcrLine matched = FindPickupMatch(hasDialog, Read(frame, textArea), options);
            // Both lists use the reference's multi-row region. A lower match
            // scrolls the options before the whitelist's F sequence (or blocks it
            // when the lower line matched the blacklist). The route callback must
            // still acknowledge delivery before any subsequent action.
            if (matched != null && matched.Box.Top > textArea.Top + key.Height)
            {
                if (scrollPickup == null || !scrollPickup()) return null;
                if (!hasDialog)
                {
                    nextAction = ActionTime(now) + 200;
                    return "切换拾取选项";
                }
            }
            if (hasDialog != (matched != null) || !pressF()) return null;
            pendingPickups = 2;
            nextAction = ActionTime(now) + 200;
            return "自动拾取";
        }

        internal static Rectangle PickupTextArea(Rectangle key)
        {
            return new Rectangle(key.Left + (int)(key.Width * 5.2), key.Top - (int)(key.Height * .8),
                key.Width * 7, (int)(key.Height * 5.5));
        }

        internal static bool HasAstriteIcon(PixelFrame frame, FarmVision vision, Rectangle key)
        {
            Rectangle ignored;
            var icon = new Rectangle(key.X + key.Width * 2, key.Y - key.Height, key.Width * 5, key.Height * 3);
            return vision.FindFirst(frame, "pick_up_xinyao_icon", icon, 1, .80, .66, out ignored);
        }

        internal static bool CanPick(bool hasDialog, IEnumerable<OcrLine> lines, WorldAssistOptions options)
        {
            return hasDialog == (FindPickupMatch(hasDialog, lines, options) != null);
        }

        private static OcrLine FindPickupMatch(bool hasDialog, IEnumerable<OcrLine> lines, WorldAssistOptions options)
        {
            string[] terms = hasDialog ? options.PickUpAllow ?? WorldAssistOptions.DefaultAllow :
                options.PickUpBlock ?? WorldAssistOptions.DefaultBlock;
            // OcrReader already filters rejected recognitions. Do not add a
            // separate confidence gate to this task's text matching.
            return lines.FirstOrDefault(line => line != null && ContainsAny(new[] { OcrText.Compact(line.Text) }, terms));
        }

        private static bool ContainsAny(IEnumerable<string> texts, string[] terms)
        {
            return terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                texts.Any(text => text.IndexOf(OcrText.Compact(term), StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool WhiteEnough(PixelFrame frame, Rectangle key)
        {
            Rectangle area = Rectangle.Intersect(key, new Rectangle(0, 0, frame.Width, frame.Height));
            int white = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int red, green, blue;
                    frame.Rgb(x, y, out red, out green, out blue);
                    if (red >= 235 && green >= 235 && blue >= 235) white++;
                }
            return !area.IsEmpty && white * 2 >= area.Width * area.Height;
        }

        private bool TrySkip(PixelFrame frame, long now, Func<Point, bool> click)
        {
            Rectangle box;
            if (pendingDialog == DialogAction.None && FindSkipIcon(frame, vision, out box))
            {
                if (!click(Center(box))) return false;
                nextAction = ActionTime(now) + 200;
                // Keep clicking a visible skip icon before starting the
                // three-second confirmation window, as in the reference.
                skipConfirmUntil = -1;
                return true;
            }
            if (skipConfirmUntil == -1) skipConfirmUntil = now + 3000;
            if (ObserveDialog(frame, now, click, true))
            {
                if (pendingDialog == DialogAction.None) skipConfirmUntil = 0;
                return true;
            }
            if (skipConfirmUntil > now)
            {
                nextAction = now + 200;
                return false;
            }
            skipConfirmUntil = 0;
            if (vision.FindLabel(frame, "message", .85, out box) &&
                vision.FindLabel(frame, "message_dialog", .84, out box))
            {
                var close = new Point(box.Left + (int)(frame.Width * .315), box.Top + box.Height * 3);
                return click(close);
            }
            return false;
        }

        private bool ObserveDialog(PixelFrame frame, long now, Func<Point, bool> click, bool allowSummary)
        {
            Rectangle button, check;
            DialogAction action = pendingDialog;
            pendingDialog = DialogAction.None;
            if (action == DialogAction.Summary)
            {
                if (FindSkipConfirmation(frame, vision, out button, skipConfirmUntil > now) && pendingButton.IntersectsWith(button) && click(Center(button)))
                {
                    nextAction = ActionTime(now) + 200;
                    return true;
                }
                return false;
            }
            if (vision.FindOptionalButton(frame, .78, out button))
            {
                if (action == DialogAction.Confirm && pendingButton.IntersectsWith(button))
                {
                    if (!click(Center(button))) { pendingDialog = action; return false; }
                    nextAction = ActionTime(now) + 200;
                    return true;
                }
                if (vision.FindOptionalCheckbox(frame, out check))
                {
                    pendingDialog = DialogAction.Check;
                    if (action == DialogAction.Check && pendingButton.IntersectsWith(button))
                    {
                        if (!click(Center(check))) return false;
                        pendingDialog = DialogAction.Confirm;
                    }
                    pendingButton = button;
                    nextAction = ActionTime(now) + 500;
                    return true;
                }
            }
            if (allowSummary && FindSkipConfirmation(frame, vision, out pendingButton, skipConfirmUntil > now))
            {
                pendingDialog = DialogAction.Summary;
                nextAction = now + 200;
                return true;
            }
            return false;
        }

        internal static bool FindSkipIcon(PixelFrame frame, FarmVision vision, out Rectangle box)
        {
            // Both skip icons are anchored to the left edge, including ultrawide viewports.
            return vision.BestMatchInRegion(frame, SkipIcons, new Rectangle(22, 25, 110, 145), 1,
                .74, .83, out box, WideUiScales) != null;
        }

        internal static bool FindSkipConfirmation(PixelFrame frame, FarmVision vision, out Rectangle box, bool knownStory = false)
        {
            int middle = frame.Width / 2;
            var search = new Rectangle(middle + 90, 548, 160, 124);
            foreach (string label in SkipConfirmations)
            {
                if (vision.FindLabel(frame, label, .80, out box) && (knownStory || HasStoryButtons(frame, box))) return true;
                if (vision.BestMatchInRegion(frame, new[] { label }, search, 1, .78, .86, out box,
                    WideUiScales, (name, candidate) => knownStory || HasStoryButtons(frame, candidate)) != null) return true;
            }
            box = Rectangle.Empty;
            return false;
        }

        private static bool HasStoryButtons(PixelFrame frame, Rectangle icon)
        {
            // The template covers the left edge of the Skip Story button. The matching
            // Continue Watching button lies across the middle of the same dialog.
            int start = icon.Left + icon.Width / 2, end = icon.Left + 5 * icon.Width;
            if (end >= frame.Width || start < frame.Width / 2) return false;
            int count = 0, rightFill = 0, leftFill = 0, rightText = 0, leftText = 0;
            for (int y = icon.Top + icon.Height / 3; y < icon.Top + icon.Height * 2 / 3; y += 2)
                for (int x = start; x <= end; x += 3)
                {
                    int opposite = frame.Width - 1 - x;
                    if (frame.Pale(x, y, 196, 38)) rightFill++;
                    if (frame.Pale(opposite, y, 196, 38)) leftFill++;
                    if (frame.Light(x, y) < 105) rightText++;
                    if (frame.Light(opposite, y) < 105) leftText++;
                    count++;
                }
            return count > 0 && Math.Min(rightFill, leftFill) * 2 >= count &&
                Math.Min(rightText, leftText) * 80 >= count;
        }

        internal static Rectangle CloudDisconnectButton(List<OcrLine> dialog, out bool disconnected)
        {
            disconnected = dialog.Any(line => line.Text != null && CloudDisconnectText.Any(line.Text.Contains));
            if (!disconnected) return Rectangle.Empty;
            OcrLine button = dialog.FirstOrDefault(line => line.Text == "退出游戏" || line.Text == "确定");
            return button == null ? Rectangle.Empty : button.Box;
        }

        private bool TryReconnect(PixelFrame frame, long now, bool cloud, Func<Point, bool> click, out bool disconnected)
        {
            disconnected = false;
            if (!cloud || ocr == null) return false;
            Rectangle button;
            try { button = CloudDisconnectButton(ocr.ReadRelative(frame, .20, .25, .80, .75, centered: true), out disconnected); }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException error)
            {
                RunLog.Error("战斗辅助", "云游戏断线弹窗识别失败", error);
                return false;
            }
            if (button.IsEmpty || !click(Center(button)))
                return false;

            entered = false;
            login.Reset();
            nextLogin = nextAction = now + 3000;
            reconnectUntil = nextLogin + 600000;
            return true;
        }

        internal void UpdateCursor(GameWindow window, Rectangle gameBounds, long now)
        {
            // The independent sampler is only armed by a successfully verified
            // local background route. It cannot discover or redirect to any
            // other window. Suspend it when that route is not available.
            if (!options.RestoreCursor || Environment.OSVersion.Platform != PlatformID.Win32NT ||
                window.Mode != GameSource.Background || window.InputBlockedByIntegrity || gameBounds.IsEmpty || !window.Available())
            {
                ResetCursorRoute();
                return;
            }
            if (cursorSampler == null) cursorSampler = new CursorRestoreSampler();
            cursorSampler.SetRoute(window.Handle, window.ProcessId, gameBounds);
        }

        internal void ResetCursorRoute() { if (cursorSampler != null) cursorSampler.ClearRoute(); }

        internal void UpdateCursor(GameSource mode, Rectangle gameBounds, long now, Func<bool> canRestore,
            Func<Point> readCursor, Func<Point, bool> setCursor)
        {
            if (options.RestoreCursor) cursorTracker.Observe(mode, gameBounds, now, canRestore, readCursor, setCursor);
        }

        internal static bool ShouldRestoreCursor(Point previous, Point current, Point center)
        {
            return DistanceSquared(previous, current) > 200L * 200 && DistanceSquared(center, current) < 50L * 50;
        }

        private static long DistanceSquared(Point a, Point b)
        {
            long dx = (long)a.X - b.X, dy = (long)a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        internal double ReadAbyssTimer(PixelFrame frame)
        {
            var viewport = new Rectangle(0, 0, frame.Width, frame.Height);
            Rectangle area = FramePatch.ScaleCentered(viewport, 3840, 2160, 1820, 266, 2100, 340);
            foreach (OcrLine line in Read(frame, area))
            {
                string label = (line.Text ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKC).Trim();
                double seconds = HudGlyphs.ParseChallengeClock(label);
                if (!double.IsNaN(seconds)) return seconds;
            }
            return double.NaN;
        }

        private List<OcrLine> Read(PixelFrame frame, Rectangle area)
        {
            if (ocr != null)
            {
                try { return ocr.Read(frame, area); }
                catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException error)
                {
                    RunLog.Error("战斗辅助", "界面文字识别失败", error);
                }
            }
            return new List<OcrLine>();
        }

        private static Rectangle Region(PixelFrame frame, double left, double top, double right, double bottom)
        {
            return Rectangle.FromLTRB((int)(frame.Width * left), (int)(frame.Height * top),
                (int)(frame.Width * right), (int)(frame.Height * bottom));
        }

        private static Point Center(Rectangle box) { return new Point(box.X + box.Width / 2, box.Y + box.Height / 2); }

        public void Dispose()
        {
            if (cursorSampler != null) cursorSampler.Dispose();
            if (ownsOcr && ocr != null) ocr.Dispose();
        }
    }

    // Shared pure cursor policy; the independent timer and deterministic
    // regression harness use exactly the same state and cooldown transitions.
    internal sealed class CursorResetTracker
    {
        private long next;
        private Point? previous;
        private bool failed;

        internal void Reset() { next = 0; previous = null; failed = false; }

        internal int Delay(long now)
        {
            return failed ? Timeout.Infinite : (int)Math.Max(1, next - now);
        }

        internal void Observe(GameSource mode, Rectangle bounds, long now, Func<bool> canRestore,
            Func<Point> readCursor, Func<Point, bool> setCursor)
        {
            if (failed || mode == GameSource.Cloud || mode == GameSource.ManagedCloud || now < next) return;
            try
            {
                Point current = readCursor();
                next = now + 2;
                Point center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                if (mode == GameSource.Background && previous.HasValue &&
                    WorldAssist.ShouldRestoreCursor(previous.Value, current, center) && canRestore())
                {
                    if (setCursor(previous.Value)) next = now + 1000;
                    return;
                }
                previous = current;
            }
            catch (ExternalException error)
            {
                failed = true;
                previous = null;
                RunLog.Error("战斗辅助", "鼠标位置恢复不可用", error);
            }
        }
    }

    internal sealed class CursorRestoreSampler : IDisposable
    {
        private static readonly object leaseGate = new object();
        private static CursorRestoreSampler active;
        private readonly object gate = new object();
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private readonly AutoResetEvent change = new AutoResetEvent(false);
        private readonly CursorResetTracker tracker = new CursorResetTracker();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly Thread worker;
        private IntPtr window;
        private uint process;
        private Rectangle bounds;

        internal CursorRestoreSampler()
        {
            worker = new Thread(Run) { IsBackground = true, Name = "TideCombat cursor recovery" };
            worker.Start();
        }

        internal void SetRoute(IntPtr target, uint owner, Rectangle area)
        {
            // Nested one-time tasks can share the same window. Allow only the
            // most recently armed route's sampler to act on the global cursor.
            lock (leaseGate)
            {
                if (active != null && active != this) active.SetLocalRoute(IntPtr.Zero, 0, Rectangle.Empty);
                active = this;
                SetLocalRoute(target, owner, area);
            }
        }

        private void SetLocalRoute(IntPtr target, uint owner, Rectangle area)
        {
            lock (gate)
            {
                if (window == target && process == owner && bounds == area) return;
                window = target;
                process = owner;
                bounds = area;
                tracker.Reset();
            }
            change.Set();
        }

        internal void ClearRoute()
        {
            lock (leaseGate)
            {
                SetLocalRoute(IntPtr.Zero, 0, Rectangle.Empty);
                if (active == this) active = null;
            }
        }

        private bool CanRestore()
        {
            if (!GameWindow.Owns(window, process) || Native.GetForegroundWindow() == window ||
                !Native.IsWindowVisible(window) || Native.IsIconic(window)) return false;
            Native.Rect client;
            var origin = new Native.Point();
            return Native.GetClientRect(window, out client) && Native.ClientToScreen(window, ref origin) &&
                bounds == new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        }

        private void Run()
        {
            var events = new WaitHandle[] { stop, change };
            while (!stop.WaitOne(0))
            {
                int delay;
                lock (gate)
                {
                    if (window == IntPtr.Zero) delay = Timeout.Infinite;
                    else
                    {
                        long now = clock.ElapsedMilliseconds;
                        tracker.Observe(GameSource.Background, bounds, now, CanRestore, () => Cursor.Position,
                            point => {
                                if (!CanRestore()) return false;
                                if (!Native.SetCursorPos(point.X, point.Y)) throw new Win32Exception();
                                return true;
                            });
                        delay = tracker.Delay(clock.ElapsedMilliseconds);
                    }
                }
                if (WaitHandle.WaitAny(events, delay) == 0) break;
            }
        }

        public void Dispose()
        {
            ClearRoute();
            stop.Set();
            worker.Join();
            change.Dispose();
            stop.Dispose();
        }
    }
}
