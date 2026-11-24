using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal enum FarmMouse { Left, Right, Middle }

    // Mouse and keyboard primitives for the 4C farm task. The battle path keeps its
    // own senders; this class adds absolute clicks, holds and scrolling that the
    // campaign layer needs to drive menus.
    internal sealed class FarmInput
    {
        private const uint Wheel = 0x0800;
        private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;
        private const uint MiddleDown = 0x0020, MiddleUp = 0x0040;
        private const uint KeyUpFlag = 0x0002;
        private const uint UnicodeFlag = 0x0004, ScanFlag = 0x0008;
        private static readonly int InputSize = Marshal.SizeOf(typeof(Native.Input));

        private readonly GameWindow window;
        private readonly Dictionary<char, Func<bool>> held = new Dictionary<char, Func<bool>>();
        private long nextSkippedInput;
        internal bool HoverLoginControls { get; set; }

        internal FarmInput(GameWindow window)
        {
            this.window = window;
        }

        internal bool Click(Point frame, Size frameSize, Rectangle bounds, FarmMouse button, int holdMs,
            CancellationToken token, int settleMs = 0, bool useWindowMessage = false)
        {
            if (token.IsCancellationRequested)
                return false;
            if (!Available())
                return SkipInput("鼠标点击", "游戏窗口不可用");
            Point client = FarmCursor.ToClient(frame, frameSize, bounds.Size);
            bool sent;
            string transport;
            if (window.Mode == GameSource.Background)
            {
                transport = "后台窗口消息";
                IntPtr handle = window.Handle;
                if (HoverLoginControls)
                {
                    var screen = new Native.Point { X = client.X, Y = client.Y };
                    if (!StillBackgroundAt(handle, bounds.Size) || !Native.ClientToScreen(handle, ref screen) ||
                        !MoveCursor(new Point(screen.X, screen.Y)) || token.WaitHandle.WaitOne(200))
                        return false;
                }
                sent = StillBackgroundAt(handle, bounds.Size) &&
                    WindowMessages.ClickBackground(handle, window.ProcessId, client, button, holdMs, settleMs,
                        token, () => StillBackgroundAt(handle, bounds.Size), MouseModifiers());
            }
            else if (useWindowMessage && window.Mode == GameSource.Foreground)
            {
                transport = "前台菜单消息";
                IntPtr handle = window.Handle;
                sent = WindowMessages.ClickMenu(handle, window.ProcessId, client, button, holdMs, settleMs,
                    token, () => StillAt(handle, bounds));
            }
            else
            {
                transport = "系统键鼠";
                sent = ClickDesktop(client, bounds, button, holdMs, token, settleMs);
            }
            string action = string.Format("鼠标点击 {0}｜客户端坐标=({1},{2}) 按下={3}ms", button, client.X, client.Y, holdMs);
            return LogAction(sent, action, transport, token);
        }

        private bool ClickDesktop(Point client, Rectangle bounds, FarmMouse button, int holdMs, CancellationToken token,
            int settleMs)
        {
            Point screen = new Point(bounds.X + client.X, bounds.Y + client.Y);
            IntPtr handle = window.Handle;
            Point original = Cursor.Position;
            if (!StillAt(handle, bounds) || !MoveCursor(screen))
                return false;
            try
            {
                if (settleMs > 0 && token.WaitHandle.WaitOne(settleMs))
                    return false;
                if (token.IsCancellationRequested || Cursor.Position != screen ||
                    !StillAt(handle, bounds) || !PointsAt(handle, screen))
                    return false;
                if (!SendMouse(button, true))
                    return false;
                try { token.WaitHandle.WaitOne(Math.Max(1, holdMs)); }
                finally { SendMouse(button, false); }
                if (settleMs > 0)
                    token.WaitHandle.WaitOne(settleMs);
                return true;
            }
            finally
            {
                if (Cursor.Position == screen && window.StillForeground())
                    Cursor.Position = original;
            }
        }

        internal bool DragForeground(Point start, Size frameSize, Rectangle bounds, CancellationToken token,
            Func<Func<Point, bool>, bool> finish)
        {
            if (window.Mode != GameSource.Foreground)
                return false;
            IntPtr handle = window.Handle;
            Point last = Cursor.Position;
            Point original = last;
            Func<Point, bool> move = point =>
            {
                if (token.IsCancellationRequested || !StillAt(handle, bounds) || Cursor.Position != last)
                    return false;
                Point client = FarmCursor.ToClient(point, frameSize, bounds.Size);
                var screen = new Point(bounds.X + client.X, bounds.Y + client.Y);
                if (!PointsAt(handle, screen) || !MoveCursor(screen))
                    return false;
                last = screen;
                return true;
            };
            if (!move(start) || token.WaitHandle.WaitOne(120) || Cursor.Position != last ||
                !StillAt(handle, bounds) || !SendMouse(FarmMouse.Left, true))
                return false;
            bool placed = false;
            try
            {
                placed = finish(move) && !token.IsCancellationRequested && StillAt(handle, bounds) && Cursor.Position == last;
                return placed;
            }
            finally
            {
                if (!placed && StillAt(handle, bounds) && Cursor.Position == last)
                {
                    Point client = FarmCursor.ToClient(start, frameSize, bounds.Size);
                    var pickup = new Point(bounds.X + client.X, bounds.Y + client.Y);
                    if (PointsAt(handle, pickup) && MoveCursor(pickup))
                        last = pickup;
                }
                try { SendMouse(FarmMouse.Left, false); }
                finally
                {
                    if (StillAt(handle, bounds) && Cursor.Position == last)
                        MoveCursor(original);
                }
            }
        }

        private bool StillAt(IntPtr handle, Rectangle bounds)
        {
            Rectangle current;
            return window.Handle == handle && window.TryGetForegroundBounds(out current) && current == bounds;
        }

        private bool StillBackgroundAt(IntPtr handle, Size clientSize)
        {
            Native.Rect current;
            return window.Mode == GameSource.Background && window.Handle == handle && Available() &&
                Native.GetClientRect(handle, out current) && current.Right - current.Left == clientSize.Width &&
                current.Bottom - current.Top == clientSize.Height;
        }

        private static bool PointsAt(IntPtr root, Point screen)
        {
            IntPtr hit = Native.WindowFromPoint(new Native.Point { X = screen.X, Y = screen.Y });
            return hit == root || hit != IntPtr.Zero && Native.GetAncestor(hit, 2) == root;
        }

        internal bool ClickRelative(double x, double y, Size frameSize, Rectangle bounds, FarmMouse button, int holdMs,
            CancellationToken token)
        {
            return Click(new Point((int)Math.Round(x * frameSize.Width), (int)Math.Round(y * frameSize.Height)),
                frameSize, bounds, button, holdMs, token);
        }

        internal bool Key(char key, int holdMs, CancellationToken token)
        {
            ushort vk = VirtualKey(key);
            return vk != 0 && KeyCode(vk, holdMs, token);
        }

        internal bool KeyCode(ushort vk, int holdMs, CancellationToken token, bool useWindowMessage = false)
        {
            if (token.IsCancellationRequested || vk == 0)
                return false;
            if (!Available())
                return SkipInput("按键", "游戏窗口不可用");
            bool viaMessage = window.Mode == GameSource.Background || useWindowMessage && window.Mode == GameSource.Foreground;
            bool sent = viaMessage ? WindowMessages.TapKey(window.Handle, window.ProcessId, vk, holdMs, token,
                Available) : SendKey(vk, holdMs, token);
            return LogAction(sent, string.Format("按键 VK=0x{0:X2}｜按下={1}ms", vk, holdMs),
                viaMessage ? "窗口消息" : "系统键鼠", token);
        }

        internal bool KeyDown(char key)
        {
            if (!Available() || held.ContainsKey(key))
                return false;
            ushort vk = VirtualKey(key);
            if (vk == 0)
                return false;
            if (window.Mode == GameSource.Background)
            {
                Func<bool> release;
                if (!WindowMessages.TryHoldKey(window.Handle, window.ProcessId, vk, out release, Available))
                    return false;
                held.Add(key, release);
            }
            else
            {
                ushort scan = (ushort)Native.MapVirtualKey(vk, 0);
                if (scan == 0 || !Available() || !Send(Keyboard(scan, ScanFlag)))
                    return false;
                held.Add(key, () => Send(Keyboard(scan, ScanFlag | KeyUpFlag)));
            }
            RunLog.Info("Input", string.Format("开始长按 VK=0x{0:X2}｜模式={1} PID={2}", vk, window.Mode, window.ProcessId));
            return true;
        }

        internal bool KeyUp(char key)
        {
            Func<bool> release;
            if (!held.TryGetValue(key, out release))
                return false;
            bool sent = release();
            held.Remove(key);
            if (sent)
                RunLog.Info("Input", string.Format("结束长按 VK=0x{0:X2}", VirtualKey(key)));
            else
                RunLog.Warn("Input", string.Format("未投递长按松开 VK=0x{0:X2}｜原目标窗口已失效", VirtualKey(key)));
            return sent;
        }

        internal bool TypeText(string text, CancellationToken token)
        {
            if (string.IsNullOrEmpty(text) || token.IsCancellationRequested || !Available())
                return false;
            bool viaMessage = window.Mode == GameSource.Background;
            bool sent = viaMessage ? WindowMessages.TypeText(window.Handle, window.ProcessId, text, token) :
                SendText(text, token);
            return LogAction(sent, "文本输入", viaMessage ? "窗口消息" : "系统键鼠", token);
        }

        private bool SendText(string text, CancellationToken token)
        {
            var packets = new Native.Input[checked(text.Length * 2)];
            for (int index = 0; index < text.Length; index++)
            {
                packets[index * 2] = Keyboard(text[index], UnicodeFlag);
                packets[index * 2 + 1] = Keyboard(text[index], UnicodeFlag | KeyUpFlag);
            }
            if (token.IsCancellationRequested || !Available())
                return false;
            uint count = (uint)packets.Length;
            if (Native.SendInput(count, packets, InputSize) != count)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未接受完整的文本输入。");
            return true;
        }

        internal bool Scroll(int clicks, Point frame, Size frameSize, Rectangle bounds, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return false;
            if (!Available())
                return SkipInput("滚轮", "游戏窗口不可用");
            Point client = FarmCursor.ToClient(frame, frameSize, bounds.Size);
            bool viaMessage = window.Mode == GameSource.Background;
            bool sent;
            if (viaMessage)
                sent = StillBackgroundAt(window.Handle, bounds.Size) &&
                    WindowMessages.ScrollBackground(window.Handle, window.ProcessId, client, clicks, MouseModifiers());
            else
            {
                Point screen = new Point(bounds.X + client.X, bounds.Y + client.Y);
                IntPtr handle = window.Handle;
                sent = StillAt(handle, bounds) && MoveCursor(screen) && StillAt(handle, bounds) &&
                    PointsAt(handle, screen) && Send(WheelInput(clicks * 120));
            }
            string action = string.Format("滚轮 {0} 格｜客户端坐标=({1},{2})", clicks, client.X, client.Y);
            return LogAction(sent, action, viaMessage ? "窗口消息" : "系统键鼠", token);
        }

        internal bool ReleaseAll()
        {
            var keys = new List<char>(held.Keys);
            foreach (char key in keys)
            {
                try { KeyUp(key); }
                catch (Win32Exception error) { RunLog.Error("Input", "清理长按时未能松开按键", error); }
            }
            return keys.Count > 0;
        }

        private bool LogAction(bool sent, string action, string transport, CancellationToken token)
        {
            if (sent)
                RunLog.Info("Input", string.Format("已发送 {0}｜方式={1} PID={2}", action, transport, window.ProcessId));
            else if (!token.IsCancellationRequested)
                SkipInput(action, transport + "未完成投递");
            return sent;
        }

        private bool SkipInput(string action, string reason)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= nextSkippedInput)
            {
                nextSkippedInput = now + 3 * Stopwatch.Frequency;
                RunLog.Warn("Input", "未发送 " + action + "｜原因=" + reason);
            }
            return false;
        }

        private bool Available()
        {
            if (window.InputBlockedByIntegrity)
                throw new InputElevationRequiredException();
            return window.Available();
        }

        private int MouseModifiers()
        {
            return (held.ContainsKey('\u0010') ? 0x0004 : 0) | (held.ContainsKey('\u0011') ? 0x0008 : 0);
        }

        private bool SendKey(ushort vk, int holdMs, CancellationToken token)
        {
            ushort scan = (ushort)Native.MapVirtualKey(vk, 0);
            if (scan == 0 || token.IsCancellationRequested || !Available())
                return false;
            if (!Send(Keyboard(scan, ScanFlag)))
                return false;
            try
            {
                token.WaitHandle.WaitOne(Math.Max(1, holdMs));
            }
            finally
            {
                Send(Keyboard(scan, ScanFlag | KeyUpFlag));
            }
            return true;
        }

        private static bool Send(Native.Input input)
        {
            if (Native.SendInput(1, new[] { input }, InputSize) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未接受输入，请检查两个程序的权限级别。");
            return true;
        }

        private bool SendMouse(FarmMouse button, bool down)
        {
            uint flag = button == FarmMouse.Left ? (down ? LeftDown : LeftUp) :
                button == FarmMouse.Right ? (down ? RightDown : RightUp) : (down ? MiddleDown : MiddleUp);
            return Send(Mouse(flag));
        }

        private static bool MoveCursor(Point screen)
        {
            return Native.SetCursorPos(screen.X, screen.Y);
        }

        internal static ushort VirtualKey(char key)
        {
            char upper = char.ToUpperInvariant(key);
            if (upper >= 'A' && upper <= 'Z')
                return upper;
            if (key >= '0' && key <= '9')
                return key;
            switch (upper)
            {
                case ' ': return 0x20;
                case '\u001b': return 0x1B;
                case '\u0010': return 0x10;
                case '\u0011': return 0x11;
                case '\u0012': return 0x12;
                default: return 0;
            }
        }

        private static Native.Input Mouse(uint flags)
        {
            return new Native.Input
            {
                Payload = new Native.InputPayload { Mouse = new Native.MouseInput { Flags = flags } }
            };
        }

        private static Native.Input WheelInput(int delta)
        {
            return new Native.Input
            {
                Payload = new Native.InputPayload
                {
                    Mouse = new Native.MouseInput { Flags = Wheel, MouseData = unchecked((uint)delta) }
                }
            };
        }

        private static Native.Input Keyboard(ushort scan, uint flags)
        {
            return new Native.Input
            {
                Type = 1,
                Payload = new Native.InputPayload { Keyboard = new Native.KeyboardInput { ScanCode = scan, Flags = flags } }
            };
        }
    }

    internal static class FarmCursor
    {
        internal static Point ToClient(Point frame, Size frameSize, Size clientSize)
        {
            int x = (int)Math.Round(frame.X * (double)clientSize.Width / Math.Max(1, frameSize.Width));
            int y = (int)Math.Round(frame.Y * (double)clientSize.Height / Math.Max(1, frameSize.Height));
            return new Point(Math.Max(0, Math.Min(clientSize.Width - 1, x)),
                Math.Max(0, Math.Min(clientSize.Height - 1, y)));
        }

        internal static Point ToScreen(Point frame, Size frameSize, Rectangle bounds)
        {
            Point client = ToClient(frame, frameSize, bounds.Size);
            return new Point(bounds.X + client.X, bounds.Y + client.Y);
        }

        internal static Point FromRatio(double x, double y, Size frameSize)
        {
            return new Point((int)Math.Round(x * frameSize.Width), (int)Math.Round(y * frameSize.Height));
        }
    }
}
