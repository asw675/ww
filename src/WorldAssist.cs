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
        public string[] PickUpAllow { get; set; }
        public string[] PickUpBlock { get; set; }

        internal bool NeedsVision { get { return PickUp || SkipDialog || Login || Travel; } }
    }

    internal sealed class MonthlyCardSchedule
    {
        private readonly bool enabled;
        private readonly int hour;
        private DateTime start;

        internal MonthlyCardSchedule(WorldAssistOptions options)
        {
            enabled = options == null || options.CheckMonthlyCard != false;
            hour = Math.Max(0, Math.Min(23, options == null ? 4 : options.MonthlyCardHour ?? 4));
        }

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
        private long nextAction, nextLogin, nextCursor;
        private long reconnectUntil;
        private readonly LoginScreen login;
        private bool entered;
        private enum DialogAction { None, Check, Confirm, Summary }
        private DialogAction pendingDialog;
        private Rectangle pendingButton;
        private Point? previousCursor;
        private bool cursorFailed;
        private int pendingPickups;
        private static readonly string[] TravelIcons = { "fast_travel_custom", "gray_teleport", "remove_custom" };
        private string travelIcon;
        private int travelStep;
        private long travelUntil;

        internal WorldAssist(WorldAssistOptions options, FarmVision vision, OcrReader sharedOcr = null, GameUpdateRecovery updates = null,
            bool recoveringLogin = false)
        {
            this.options = options;
            this.vision = vision;
            login = new LoginScreen(updates == null ? (Func<Func<bool>,bool>)null : updates.Confirm);
            this.recoveringLogin = recoveringLogin;
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

        internal void ResetWindow()
        {
            entered = false;
            previousCursor = null;
            cursorFailed = false;
            nextLogin = nextAction = nextCursor = 0;
            reconnectUntil = 0;
            pendingDialog = DialogAction.None;
            login.Reset();
            pendingPickups = travelStep = 0;
            travelIcon = null;
        }

        internal bool NeedsNativeFrame(long captureTime) { return options.NeedsVision && captureTime >= nextAction; }

        internal string Observe(PixelFrame frame, HudSnapshot hud, bool fighting, long now,
            Func<Point, bool> click, Func<bool> pressF, bool cloud = false, long? capturedAt = null)
        {
            if (!options.NeedsVision)
                return null;
            bool inTeam = hud.Valid && !hud.Dead;
            if (options.Login && !entered && !hud.Dead && vision.HasTeamNumbers(frame))
            {
                entered = true;
                reconnectUntil = 0;
                login.Reset();
            }
            if (inTeam)
            {
                pendingDialog = DialogAction.None;
                travelStep = 0;
            }
            if (fighting || hud.EnemyHealth || hud.Locked || hud.Dead)
            {
                pendingDialog = DialogAction.None;
                travelStep = 0;
                pendingPickups = 0;
                return null;
            }
            if (!NeedsNativeFrame(capturedAt ?? now))
                return null;
            if (inTeam)
            {
                if (pendingPickups > 0)
                {
                    Rectangle prompt;
                    if (vision.FindPickupKey(frame, out prompt) && HasAstriteIcon(frame, vision, prompt))
                    {
                        pendingPickups = 0;
                        nextAction = now + 350;
                        return null;
                    }
                    if (!pressF()) { pendingPickups = 0; return null; }
                    pendingPickups--;
                    nextAction = now + 200;
                    return "自动拾取";
                }
                if (options.PickUp && ocr != null && TryPick(frame, pressF))
                {
                    nextAction = now + (pendingPickups > 0 ? 200 : 800);
                    return "自动拾取";
                }
                pendingPickups = 0;
                nextAction = now + 350;
                return null;
            }

            pendingPickups = 0;
            nextAction = now + 400;
            bool disconnected = false;
            bool relogging = recoveringLogin || now < reconnectUntil;
            if (options.Login && !relogging && TryReconnect(frame, now, cloud, click, out disconnected))
                return "处理云游戏断线";
            if (options.Login && disconnected)
                return null;
            if (options.Travel && travelStep != 0)
                return TryTravel(frame, now, click) ? "自动快速旅行" : null;
            if (options.SkipDialog && TrySkip(frame, now, click))
                return "自动跳过剧情";
            if (options.Travel && TryTravel(frame, now, click))
                return "自动快速旅行";
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
            if (travelStep != 0 && now > travelUntil && pendingDialog == DialogAction.None)
            {
                if (travelStep == 2 || travelStep == 4)
                {
                    travelStep = 5;
                    travelUntil = now + 3000;
                }
                else travelStep = 0;
            }
            if (travelStep == 1)
            {
                travelStep = 0;
                if (!vision.FindLabel(frame, travelIcon, .70, out button) || !click(Center(button))) return false;
                travelStep = travelIcon == "fast_travel_custom" ? 2 : travelIcon == "gray_teleport" ? 5 : 0;
                nextAction = now + 1000;
                travelUntil = nextAction + (travelStep == 5 ? 3000 : 2000);
                return true;
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
                nextAction = now + 500;
                travelUntil = nextAction + (travelStep == 4 ? 1000 : travelStep == 5 ? 3000 : 2000);
                return true;
            }
            if (!vision.FindLabel(frame, "gray_teleport", .76, out button)) return false;
            var labels = Read(frame, Region(frame, .70, .89, 1, 1));
            if (!labels.Any(line => line.Score >= .65f && ContainsAny(new[] { line.Text ?? "" }, TravelButtons)))
                return false;
            foreach (string icon in TravelIcons)
                if (vision.FindLabel(frame, icon, .70, out button))
                {
                    travelIcon = icon;
                    travelStep = 1;
                    nextAction = now + 500;
                    travelUntil = nextAction + 2000;
                    break;
                }
            return false;
        }

        private bool TryPick(PixelFrame frame, Func<bool> pressF)
        {
            Rectangle key, ignored;
            if (!vision.FindPickupKey(frame, out key) || !WhiteEnough(frame, key))
                return false;
            if (HasAstriteIcon(frame, vision, key))
                return false;
            Rectangle dot = new Rectangle(key.X + key.Width * 3, key.Y - key.Height, key.Width * 3, key.Height * 3);
            bool hasDialog = vision.FindFirst(frame, "dialog_3_dots", dot, 1, .60, .64, out ignored);
            int textLeft = dot.Right;
            List<OcrLine> text = Read(frame, new Rectangle(textLeft, dot.Top, key.Width * 10, key.Height * 3));
            if (!CanPick(hasDialog, text, options, key) || !pressF())
                return false;
            pendingPickups = 2;
            return true;
        }

        internal static bool HasAstriteIcon(PixelFrame frame, FarmVision vision, Rectangle key)
        {
            Rectangle ignored;
            var icon = new Rectangle(key.X + key.Width * 2, key.Y - key.Height, key.Width * 5, key.Height * 3);
            return vision.FindFirst(frame, "pick_up_xinyao_icon", icon, 1, .80, .66, out ignored);
        }

        internal static bool CanPick(bool hasDialog, IEnumerable<OcrLine> lines, WorldAssistOptions options,
            Rectangle selectedKey = default(Rectangle))
        {
            int center = Center(selectedKey).Y;
            string[] texts = lines.Where(line => line.Score >= .62f && (selectedKey.IsEmpty ||
                !line.Box.IsEmpty && Math.Abs(Center(line.Box).Y - center) <= selectedKey.Height * .75))
                .Select(line => OcrText.Compact(line.Text)).Where(value => value.Length > 0).ToArray();
            if (hasDialog)
                return ContainsAny(texts, options.PickUpAllow ?? WorldAssistOptions.DefaultAllow);
            return !ContainsAny(texts, options.PickUpBlock ?? WorldAssistOptions.DefaultBlock);
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
                nextAction = now + 200;
                return true;
            }
            if (ObserveDialog(frame, now, click, true)) return true;
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
                if (FindSkipConfirmation(frame, vision, out button) && pendingButton.IntersectsWith(button) && click(Center(button)))
                {
                    nextAction = now + 200;
                    return true;
                }
                return false;
            }
            if (vision.FindOptionalButton(frame, .78, out button))
            {
                if (action == DialogAction.Confirm && pendingButton.IntersectsWith(button))
                {
                    if (!click(Center(button))) { pendingDialog = action; return false; }
                    nextAction = now + 200;
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
                    nextAction = now + 500;
                    return true;
                }
            }
            if (allowSummary && FindSkipConfirmation(frame, vision, out pendingButton))
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

        internal static bool FindSkipConfirmation(PixelFrame frame, FarmVision vision, out Rectangle box)
        {
            int middle = frame.Width / 2;
            var search = new Rectangle(middle + 90, 548, 160, 124);
            foreach (string label in SkipConfirmations)
            {
                if (vision.FindLabel(frame, label, .80, out box) && HasStoryButtons(frame, box)) return true;
                if (vision.BestMatchInRegion(frame, new[] { label }, search, 1, .78, .86, out box,
                    WideUiScales, (name, candidate) => HasStoryButtons(frame, candidate)) != null) return true;
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
            UpdateCursor(window.Mode, gameBounds, now, () => !window.StillForeground() && window.Available(),
                () => Cursor.Position, position =>
                {
                    if (!Native.SetCursorPos(position.X, position.Y)) throw new Win32Exception();
                    return true;
                });
        }

        internal void UpdateCursor(GameSource mode, Rectangle gameBounds, long now, Func<bool> canRestore,
            Func<Point> readCursor, Func<Point, bool> setCursor)
        {
            if (!options.RestoreCursor || cursorFailed || mode == GameSource.Cloud || mode == GameSource.ManagedCloud || now < nextCursor)
                return;
            try
            {
                Point current = readCursor();
                nextCursor = now + 2;
                if (mode == GameSource.Background && previousCursor.HasValue &&
                    ShouldRestoreCursor(previousCursor.Value, current, Center(gameBounds)) && canRestore())
                {
                    if (setCursor(previousCursor.Value))
                        nextCursor = now + 1000;
                    return;
                }
                previousCursor = current;
            }
            catch (ExternalException error)
            {
                cursorFailed = true;
                previousCursor = null;
                RunLog.Error("战斗辅助", "鼠标位置恢复不可用", error);
            }
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
            if (ownsOcr && ocr != null) ocr.Dispose();
        }
    }
}
