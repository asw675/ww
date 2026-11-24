using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace TideCombat
{
    internal sealed class LoginScreen
    {
        private static readonly Size Design = new Size(1600, 900);
        private static readonly Size TemplateDesign = new Size(1920, 1080);
        private readonly ScreenTemplate notice = new ScreenTemplate("login_close",
            size => ScreenTemplate.Place(size, TemplateDesign, new Rectangle(1553, 159, 51, 55), true, false), .15, .10, .80);
        private readonly ScreenTemplate account = new ScreenTemplate("switch_account",
            size => ScreenTemplate.Place(size, TemplateDesign, new Rectangle(1796, 533, 43, 42), false, false), .002, .10, .70);
        private readonly Func<Func<bool>, bool> confirmUpdate;
        private long confirmAt = -1;

        internal LoginScreen(Func<Func<bool>, bool> confirmUpdate = null) { this.confirmUpdate = confirmUpdate; }

        internal long ConfirmationTime { get { return confirmAt; } }
        internal int AfterClickDelay { get; private set; }
        internal void Reset() { confirmAt = -1; AfterClickDelay = 0; }

        internal static Rectangle Bounds(Size size, Rectangle box, bool centered)
        {
            if (Math.Abs(size.Width / (double)size.Height - 16.0 / 9) <= .01)
                return new Rectangle((int)Math.Round(size.Width * (box.X / 1600.0)), (int)Math.Round(size.Height * (box.Y / 900.0)),
                    (int)Math.Round(size.Width * (box.Width / 1600.0)), (int)Math.Round(size.Height * (box.Height / 900.0)));
            return ScreenTemplate.Place(size, Design, box, centered, centered);
        }

        internal static bool LoginLabel(string text)
        {
            return text == "登录" || text == "登入" || text.Contains("点击连接") ||
                text.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Click to Connect", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static OcrLine First(IEnumerable<OcrLine> lines, Rectangle area, Func<string, bool> matches)
        {
            return lines.FirstOrDefault(line => !line.Box.IsEmpty && area.Contains(line.Box) &&
                line.Text != null && matches(line.Text));
        }

        private static Point Center(Rectangle box) { return new Point(box.X + box.Width / 2, box.Y + box.Height / 2); }

        internal static Point CenteredPoint(Size size, double x, double y)
        {
            if (Math.Abs(size.Width / (double)size.Height - 16.0 / 9) <= .01)
                return new Point((int)(size.Width * x), (int)(size.Height * y));
            double scale = Math.Min(size.Width / 1600.0, size.Height / 900.0);
            return new Point((int)Math.Round(size.Width * .5 + (x - .5) * 1600 * scale),
                (int)Math.Round(size.Height * .5 + (y - .5) * 900 * scale));
        }

        internal static Point AccountLoginPoint(Size size, IEnumerable<OcrLine> lines)
        {
            Rectangle dialog = Bounds(size, new Rectangle(480, 270, 640, 450), false);
            OcrLine button = First(lines, dialog, LoginLabel);
            return button == null ? CenteredPoint(size, .5, .568) : Center(button.Box);
        }

        internal bool Observe(PixelFrame frame, long now, Func<PixelFrame, List<OcrLine>> read, Func<Point, bool> click)
        {
            if (confirmAt >= 0 && now < confirmAt) return false;
            var elapsed = Stopwatch.StartNew();
            Rectangle close;
            if (confirmAt < 0 && notice.Find(frame, out close))
            {
                AfterClickDelay = 1000;
                return click(Center(close));
            }
            PixelFrame source = frame.FullResolution;
            var lines = read(source);
            return ObserveText(new Size(source.Width, source.Height), now + elapsed.ElapsedMilliseconds, lines,
                () => { Rectangle icon; return account.Find(frame, out icon); },
                point => click(new Point((int)Math.Round(point.X * (double)frame.Width / source.Width),
                    (int)Math.Round(point.Y * (double)frame.Height / source.Height))));
        }

        internal bool ObserveText(Size size, long now, List<OcrLine> lines, Func<bool> switchAccount, Func<Point, bool> click)
        {
            if (confirmAt >= 0 && now < confirmAt) return false;
            AfterClickDelay = 0;
            Rectangle center = Bounds(size, new Rectangle(480, 270, 640, 360), true);
            OcrLine login = First(lines, center, LoginLabel);
            bool phone = First(lines, center, text => text.Contains("+86")) != null;
            if (confirmAt >= 0)
            {
                Reset();
                AfterClickDelay = 1000;
                return login != null && !phone && click(Center(login.Box));
            }
            if (login != null)
            {
                if (!phone) confirmAt = now + 4000;
                return false;
            }
            OcrLine agree = First(lines, center, text => text == "同意");
            if (agree != null)
            {
                AfterClickDelay = 1000;
                return First(lines, center, text => text.Contains("隐私")) != null && click(Center(agree.Box));
            }
            if (lines.Any(line => line.Text != null && (line.Text.Contains("游戏即将重启") || line.Text.Contains("遊戲即將重啟"))))
            {
                OcrLine confirm = lines.FirstOrDefault(line => !line.Box.IsEmpty && (line.Text == "确认" || line.Text == "確認"));
                return confirm != null && confirmUpdate != null && confirmUpdate(() => click(Center(confirm.Box)));
            }

            Rectangle bottomRight = Bounds(size, new Rectangle(800, 450, 800, 450), false);
            OcrLine start = First(lines, bottomRight, text => text == "开始游戏" || text.Contains("进入游戏"));
            if (start != null && First(lines, bottomRight, LoginLabel) == null) return click(Center(start.Box));
            Rectangle bottomCenter = Bounds(size, new Rectangle(592, 567, 416, 324), true);
            if (First(lines, bottomCenter, text => true) == null || !switchAccount()) return false;
            AfterClickDelay = 3000;
            return click(CenteredPoint(size, .503, .926));
        }
    }
}
