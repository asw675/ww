using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private const uint MouseMove = 0x0001, Absolute = 0x8000, Wheel = 0x0800;
        private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;
        private const uint MiddleDown = 0x0020, MiddleUp = 0x0040;
        private const uint KeyUpFlag = 0x0002;
        private const uint ScanFlag = 0x0008;
        private const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
        private const uint WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;
        private const uint WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmMouseWheel = 0x020A;
        private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101;
        private static readonly int InputSize = Marshal.SizeOf(typeof(Native.Input));
        private const uint GmemMoveable = 0x0002;
        private const uint CfUnicodeText = 13;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);

        private readonly GameWindow window;
        private readonly bool background;
        private readonly Dictionary<char, Native.Input> held = new Dictionary<char, Native.Input>();
        private readonly HashSet<char> heldDown = new HashSet<char>();

        internal FarmInput(GameWindow window)
        {
            this.window = window;
            background = window.Mode == GameSource.Background;
        }

        internal bool Click(Point frame, Size frameSize, Rectangle bounds, FarmMouse button, int holdMs,
            CancellationToken token)
        {
            if (token.IsCancellationRequested || !Available())
                return false;
            Point client = ToClient(frame, frameSize, bounds);
            Point screen = new Point(bounds.X + client.X, bounds.Y + client.Y);
            if (background)
                return PostClick(client, button, holdMs, token);
            Point original = Cursor.Position;
            if (!MoveCursor(screen))
                return false;
            if (!SendMouse(button, true))
                return false;
            try
            {
                token.WaitHandle.WaitOne(Math.Max(1, holdMs));
            }
            finally
            {
                SendMouse(button, false);
            }
            if (Cursor.Position == screen && window.StillForeground())
                Cursor.Position = original;
            return true;
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

        internal bool KeyCode(ushort vk, int holdMs, CancellationToken token)
        {
            if (token.IsCancellationRequested || !Available() || vk == 0)
                return false;
            if (background)
                return PostKey(vk, holdMs, token);
            return SendKey(vk, holdMs, token);
        }

        internal bool KeyDown(char key)
        {
            if (!Available() || heldDown.Contains(key))
                return false;
            ushort vk = VirtualKey(key);
            if (vk == 0)
                return false;
            if (background)
            {
                if (!PostMessage(vk, true))
                    return false;
            }
            else
            {
                ushort scan = (ushort)Native.MapVirtualKey(vk, 0);
                if (scan == 0 || !Send(Keyboard(scan, ScanFlag)))
                    return false;
                held[key] = Keyboard(scan, ScanFlag | KeyUpFlag);
            }
            heldDown.Add(key);
            return true;
        }

        internal bool KeyUp(char key)
        {
            if (!heldDown.Contains(key))
                return false;
            heldDown.Remove(key);
            if (background)
            {
                PostMessage(VirtualKey(key), false);
                return true;
            }
            Native.Input release;
            if (!held.TryGetValue(key, out release))
                return true;
            held.Remove(key);
            Send(release);
            return true;
        }

        internal bool TypeText(string text, CancellationToken token)
        {
            if (string.IsNullOrEmpty(text) || token.IsCancellationRequested || !Available())
                return false;
            if (!SetClipboard(text))
                return false;
            if (!KeyCode(0x11, 40, token))
                return false;
            if (!KeyCode(0x56, 40, token))
                return false;
            KeyCode(0x11, 20, token);
            return true;
        }

        private static bool SetClipboard(string text)
        {
            if (!OpenClipboard(IntPtr.Zero))
                return false;
            try
            {
                EmptyClipboard();
                byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                IntPtr memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
                if (memory == IntPtr.Zero)
                    return false;
                IntPtr target = GlobalLock(memory);
                if (target == IntPtr.Zero)
                    return false;
                Marshal.Copy(bytes, 0, target, bytes.Length);
                GlobalUnlock(memory);
                return SetClipboardData(CfUnicodeText, memory) != IntPtr.Zero;
            }
            finally
            {
                CloseClipboard();
            }
        }

        internal bool Scroll(int clicks, Point frame, Size frameSize, Rectangle bounds, CancellationToken token)
        {
            if (token.IsCancellationRequested || !Available())
                return false;
            Point client = ToClient(frame, frameSize, bounds);
            if (background)
                return PostScroll(client, clicks);
            Point screen = new Point(bounds.X + client.X, bounds.Y + client.Y);
            if (!MoveCursor(screen))
                return false;
            return Send(WheelInput(clicks * 120));
        }

        internal bool ReleaseAll()
        {
            var keys = new List<char>(heldDown);
            foreach (char key in keys)
            {
                try { KeyUp(key); }
                catch (Win32Exception) { }
            }
            return keys.Count > 0;
        }

        private bool Available()
        {
            if (window.InputBlockedByIntegrity)
                throw new InputElevationRequiredException();
            return window.Available();
        }

        private bool PostClick(Point client, FarmMouse button, int holdMs, CancellationToken token)
        {
            int position = (client.X & 0xffff) | (client.Y & 0xffff) << 16;
            uint down = button == FarmMouse.Left ? WmLButtonDown : button == FarmMouse.Right ? WmRButtonDown : WmMButtonDown;
            uint up = button == FarmMouse.Left ? WmLButtonUp : button == FarmMouse.Right ? WmRButtonUp : WmMButtonUp;
            if (!Post(WmMouseMove, IntPtr.Zero, new IntPtr(position)))
                return false;
            if (!Post(down, new IntPtr(1), new IntPtr(position)))
                return false;
            try
            {
                token.WaitHandle.WaitOne(Math.Max(1, holdMs));
            }
            finally
            {
                Post(up, IntPtr.Zero, new IntPtr(position));
            }
            return true;
        }

        private bool PostScroll(Point client, int clicks)
        {
            int position = (client.X & 0xffff) | (client.Y & 0xffff) << 16;
            uint delta = unchecked((uint)(clicks * 120));
            return Post(WmMouseWheel, new IntPtr((long)(delta << 16)), new IntPtr(position));
        }

        private bool PostKey(ushort vk, int holdMs, CancellationToken token)
        {
            if (!PostMessage(vk, true))
                return false;
            try
            {
                token.WaitHandle.WaitOne(Math.Max(1, holdMs));
            }
            finally
            {
                PostMessage(vk, false);
            }
            return true;
        }

        private bool PostMessage(ushort vk, bool down)
        {
            ushort scan = (ushort)Native.MapVirtualKey(vk, 0);
            if (scan == 0)
                return false;
            long state = 1 | (long)scan << 16 | (down ? 0 : 0xc0000000L);
            return Post(down ? WmKeyDown : WmKeyUp, new IntPtr(vk), new IntPtr(state));
        }

        private bool Post(uint code, IntPtr flags, IntPtr arg)
        {
            if (!GameWindow.Owns(window.Handle, window.ProcessId))
                return false;
            if (!Native.PostMessageW(window.Handle, code, flags, arg))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "游戏窗口没有接受后台输入。");
            return true;
        }

        private bool SendKey(ushort vk, int holdMs, CancellationToken token)
        {
            ushort scan = (ushort)Native.MapVirtualKey(vk, 0);
            if (scan == 0)
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

        private static Point ToClient(Point frame, Size frameSize, Rectangle bounds)
        {
            int x = (int)Math.Round(frame.X * (double)bounds.Width / Math.Max(1, frameSize.Width));
            int y = (int)Math.Round(frame.Y * (double)bounds.Height / Math.Max(1, frameSize.Height));
            return new Point(Math.Max(0, Math.Min(bounds.Width - 1, x)), Math.Max(0, Math.Min(bounds.Height - 1, y)));
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
        internal static Point ToScreen(Point frame, Size frameSize, Rectangle bounds)
        {
            int x = (int)Math.Round(frame.X * (double)bounds.Width / Math.Max(1, frameSize.Width));
            int y = (int)Math.Round(frame.Y * (double)bounds.Height / Math.Max(1, frameSize.Height));
            return new Point(bounds.X + Math.Max(0, Math.Min(bounds.Width - 1, x)),
                bounds.Y + Math.Max(0, Math.Min(bounds.Height - 1, y)));
        }

        internal static Point FromRatio(double x, double y, Size frameSize)
        {
            return new Point((int)Math.Round(x * frameSize.Width), (int)Math.Round(y * frameSize.Height));
        }
    }
}
