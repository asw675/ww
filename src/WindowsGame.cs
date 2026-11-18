using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { internal int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { internal int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            internal uint Type;
            internal InputPayload Payload;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputPayload
        {
            [FieldOffset(0)] internal KeyboardInput Keyboard;
            [FieldOffset(0)] internal MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput
        {
            internal ushort VirtualKey, ScanCode;
            internal uint Flags, Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            internal int X, Y;
            internal uint MouseData, Flags, Time;
            internal UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        internal delegate bool EnumerateWindow(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumerateWindow action, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr flags, IntPtr param);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, uint index, out Rect rect, int bytes);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint mode);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Native.Input[] inputs, int size);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();

        internal static void EnablePhysicalCoordinates()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    return;
            }
            catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }
    }

    internal sealed class GameWindow
    {
        internal const string LocalProcessName = "Client-Win64-Shipping";
        internal readonly GameSource Mode;
        private IntPtr hwnd;
        private uint pid;
        private Rectangle lastBounds;
        internal IntPtr Handle { get { return hwnd; } }
        internal uint ProcessId { get { return pid; } }
        internal bool InputBlockedByIntegrity { get; private set; }

        internal GameWindow(GameSource source = GameSource.Foreground) { Mode = source; }

        internal bool TryLocate(out Rectangle bounds, out string reason)
        {
            bounds = Rectangle.Empty;
            IntPtr current = Native.GetForegroundWindow();
            uint currentPid;
            if (Mode == GameSource.Background && (!IsCurrentGame(current) || Native.IsIconic(current)))
                current = FindBackground();
            if (current == IntPtr.Zero || Mode != GameSource.Background && Native.IsIconic(current) ||
                Native.GetWindowThreadProcessId(current, out currentPid) == 0)
            {
                reason = WaitingText();
                return false;
            }
            if (current != hwnd || currentPid != pid)
            {
                if (!IsGame(current, currentPid, Mode))
                {
                    reason = WaitingText();
                    return false;
                }
                hwnd = current;
                pid = currentPid;
                InputBlockedByIntegrity = InputIntegrity.IsBlocked(currentPid);
                lastBounds = Rectangle.Empty;
            }
            if (Mode == GameSource.Cloud && !IsCloudTitle(current))
            {
                reason = WaitingText();
                return false;
            }
            if (Mode == GameSource.Background && Native.IsIconic(current) && !lastBounds.IsEmpty)
            {
                bounds = lastBounds;
                reason = string.Empty;
                return true;
            }

            Native.Rect client;
            var position = new Native.Point();
            if (!Native.GetClientRect(current, out client) || !Native.ClientToScreen(current, ref position))
            {
                reason = "暂时无法读取游戏窗口";
                return false;
            }

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            double aspect = height > 0 ? (double)width / height : 0;
            if (width < 1280 || height < 720 || aspect < 1.74 || aspect > 2.48)
            {
                reason = "请使用 1280×720 或更高的 16:9／21:9 游戏画面";
                return false;
            }
            bounds = new Rectangle(position.X, position.Y, width, height);
            if (Mode != GameSource.Background && !Screen.AllScreens.AnyContains(bounds))
            {
                reason = "请把整个游戏窗口移到同一块屏幕上";
                return false;
            }
            lastBounds = bounds;
            reason = string.Empty;
            return true;
        }

        internal bool StillForeground()
        {
            uint foregroundPid;
            return hwnd != IntPtr.Zero && Native.GetForegroundWindow() == hwnd &&
                Native.GetWindowThreadProcessId(hwnd, out foregroundPid) != 0 && foregroundPid == pid && !Native.IsIconic(hwnd);
        }

        internal bool Available()
        {
            return Mode == GameSource.Background ? Owns(hwnd, pid) :
                StillForeground() && (Mode != GameSource.Cloud || IsCloudTitle(hwnd));
        }

        internal static bool Owns(IntPtr window, uint process)
        {
            uint actual;
            return window != IntPtr.Zero && process != 0 &&
                Native.GetWindowThreadProcessId(window, out actual) != 0 && process == actual;
        }

        private bool IsCurrentGame(IntPtr window)
        {
            uint actual;
            return window != IntPtr.Zero && Native.GetWindowThreadProcessId(window, out actual) != 0 &&
                (window == hwnd && actual == pid || IsGame(window, actual, Mode));
        }

        private IntPtr FindBackground()
        {
            if (Owns(hwnd, pid)) return hwnd;
            IntPtr selected = IntPtr.Zero;
            Native.EnumWindows((window, data) =>
            {
                if (!Native.IsWindowVisible(window) || !IsCurrentGame(window)) return true;
                selected = window;
                return false;
            }, IntPtr.Zero);
            return selected;
        }

        private string WaitingText()
        {
            switch (Mode)
            {
                case GameSource.Cloud: return "等待切回鸣潮云游戏浏览器窗口";
                case GameSource.Background: return "等待《鸣潮》客户端窗口";
                default: return "等待切回《鸣潮》客户端";
            }
        }

        private static bool IsGame(IntPtr window, uint pid, GameSource mode)
        {
            var windowClass = new StringBuilder(80);
            Native.GetClassName(window, windowClass, windowClass.Capacity);
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    if (mode == GameSource.Cloud)
                    {
                        string exe = process.ProcessName.ToLowerInvariant();
                        return windowClass.ToString() == "Chrome_WidgetWin_1" &&
                            (exe == "msedge" || exe == "chrome" || exe == "chromium") && IsCloudTitle(window);
                    }
                    return MatchesLocalGame(windowClass.ToString(), process.ProcessName);
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
        }

        internal static bool MatchesLocalGame(string windowClass, string processName)
        {
            return string.Equals(windowClass, "UnrealWindow", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(processName, LocalProcessName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGameName(string value)
        {
            return value.IndexOf("Wuthering Waves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.Contains("鸣潮") || value.Contains("鳴潮");
        }

        private static bool IsCloudTitle(IntPtr window)
        {
            var title = new StringBuilder(160);
            Native.GetWindowText(window, title, title.Capacity);
            string text = title.ToString();
            return IsGameName(text) && (text.Contains("云") || text.Contains("雲") ||
                text.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.StartsWith("鸣潮 - ", StringComparison.Ordinal) || text.StartsWith("鳴潮 - ", StringComparison.Ordinal) ||
                text.StartsWith("Wuthering Waves - ", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class ScreenBounds
    {
        internal static bool AnyContains(this Screen[] screens, Rectangle bounds)
        {
            foreach (Screen screen in screens)
                if (screen.Bounds.Contains(bounds))
                    return true;
            return false;
        }
    }

    internal sealed class GameCapture : IDisposable
    {
        private Bitmap source;
        private Graphics sourceGraphics;
        private readonly FrameReader reader;

        internal GameCapture(bool retainCloudColors = false) { reader = new FrameReader(retainCloudColors); }

        internal PixelFrame Read(Rectangle bounds)
        {
            if (source == null || source.Size != bounds.Size)
            {
                ReleaseSource();
                source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
                sourceGraphics = Graphics.FromImage(source);
            }
            sourceGraphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size,
                CopyPixelOperation.SourceCopy);
            return reader.Read(source);
        }

        private void ReleaseSource()
        {
            if (sourceGraphics != null)
                sourceGraphics.Dispose();
            if (source != null)
                source.Dispose();
        }

        public void Dispose()
        {
            ReleaseSource();
            reader.Dispose();
        }
    }

    internal sealed class FrameReader : IDisposable
    {
        internal const int StandardHeight = 900;
        private Bitmap scaled;
        private Graphics graphics;
        private byte[] pixels;
        private readonly CloudTargetColors cloudColors;
        private readonly CamellyaColors camellyaColors = new CamellyaColors();
        private readonly FramePatch concerto = new FramePatch();

        internal FrameReader(bool retainCloudColors = false)
        {
            if (retainCloudColors) cloudColors = new CloudTargetColors();
        }

        // The frame is valid until this reader is used again.
        internal PixelFrame Read(Bitmap source)
        {
            return Read(source, new Rectangle(Point.Empty, source.Size));
        }

        internal PixelFrame Read(Bitmap source, Rectangle crop)
        {
            int width = (int)Math.Round((double)crop.Width / crop.Height * StandardHeight);
            if (scaled == null || scaled.Width != width)
            {
                Dispose();
                scaled = new Bitmap(width, StandardHeight, PixelFormat.Format32bppRgb);
                graphics = Graphics.FromImage(scaled);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                pixels = new byte[width * StandardHeight * 4];
            }
            graphics.DrawImage(source, new Rectangle(0, 0, width, StandardHeight),
                crop, GraphicsUnit.Pixel);
            BitmapData data = scaled.LockBits(new Rectangle(0, 0, width, StandardHeight), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppRgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { scaled.UnlockBits(data); }
            if (cloudColors != null) cloudColors.Read(source, crop);
            camellyaColors.Read(source, crop);
            concerto.Read(source, FramePatch.ScaleCentered(crop, 3840, 2160, 1431, 1942, 1557, 2068));
            return new PixelFrame(pixels, width, StandardHeight, cloudColors, crop.Width, camellyaColors, concerto, crop.Height);
        }

        public void Dispose()
        {
            if (graphics != null)
                graphics.Dispose();
            if (scaled != null)
                scaled.Dispose();
            graphics = null;
            scaled = null;
        }
    }

    internal sealed class WindowInput
    {
        private const uint ScanCode = 0x0008;
        private const uint KeyUp = 0x0002;
        private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010,
            MiddleDown = 0x0020, MiddleUp = 0x0040;
        private static readonly int InputSize = Marshal.SizeOf(typeof(Native.Input));
        private static readonly int[] Modifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C };

        private readonly GameWindow window;
        private readonly Dictionary<BattleButton, Native.Input> held = new Dictionary<BattleButton, Native.Input>();
        private readonly WindowMessages messages;

        internal WindowInput(GameWindow window)
        {
            this.window = window;
            if (window.Mode == GameSource.Background) messages = new WindowMessages();
        }

        internal bool Send(BattleAction action, CancellationToken cancellation)
        {
            if (action.Kind != ActionKind.Release && window.InputBlockedByIntegrity)
                throw new InputElevationRequiredException();
            if (messages != null)
                return (action.Kind == ActionKind.Release || window.Available()) &&
                    messages.Send(action, window.Handle, window.ProcessId, cancellation);
            switch (action.Kind)
            {
                case ActionKind.Attack: return Tap(Mouse(LeftDown), Mouse(LeftUp), 0x01, 32, cancellation);
                case ActionKind.Target: return Tap(Mouse(MiddleDown), Mouse(MiddleUp), 0x04, 35, cancellation);
                case ActionKind.Cast:
                    return TapKey(action.Key, 35, cancellation);
                case ActionKind.Switch:
                    return TapKey('1' + action.Slot, 45, cancellation);
                case ActionKind.Button:
                case ActionKind.Hold:
                    return SendButton(action, cancellation);
                case ActionKind.Release:
                    Release(action.Button);
                    return true;
                default: return false;
            }
        }

        private bool SendButton(BattleAction action, CancellationToken cancellation)
        {
            Native.Input down, up;
            int key = action.NativeKey;
            switch (action.Button)
            {
                case BattleButton.Attack:
                    key = 0x01; down = Mouse(LeftDown); up = Mouse(LeftUp);
                    break;
                case BattleButton.Dodge:
                    key = 0x02; down = Mouse(RightDown); up = Mouse(RightUp);
                    break;
                case BattleButton.MiddleClick:
                    key = 0x04; down = Mouse(MiddleDown); up = Mouse(MiddleUp);
                    if (action.Kind == ActionKind.Button)
                        return TapMiddleAtCenter(down, up, action.DownMilliseconds > 0 ? action.DownMilliseconds : 36, cancellation);
                    break;
                default:
                    ushort scan = (ushort)Native.MapVirtualKey((uint)key, 0);
                    if (scan == 0) return false;
                    down = Key(scan, ScanCode);
                    up = Key(scan, ScanCode | KeyUp);
                    break;
            }
            if (action.Kind == ActionKind.Button)
                return Tap(down, up, key, action.DownMilliseconds > 0 ? action.DownMilliseconds : 36, cancellation);
            if (held.ContainsKey(action.Button))
            {
                if (action.ReassertHold)
                {
                    if (cancellation.IsCancellationRequested || !window.Available()) return false;
                    InputDown(down);
                }
                return true;
            }
            if (!CanPress(key, cancellation))
                return false;
            InputDown(down);
            held.Add(action.Button, up);
            return true;
        }

        private bool TapMiddleAtCenter(Native.Input down, Native.Input up, int milliseconds, CancellationToken cancellation)
        {
            Rectangle bounds;
            string reason;
            if (!CanPress(0x04, cancellation) || !window.TryLocate(out bounds, out reason)) return false;
            Point original = Cursor.Position;
            Point center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            Cursor.Position = center;
            try { return Tap(down, up, 0x04, milliseconds, cancellation); }
            finally { if (window.StillForeground() && Cursor.Position == center) Cursor.Position = original; }
        }

        private void Release(BattleButton button)
        {
            Native.Input up;
            if (!held.TryGetValue(button, out up)) return;
            InputUp(up);
            held.Remove(button);
        }

        internal bool ReleaseAll()
        {
            if (messages != null) return messages.ReleaseAll();
            if (held.Count == 0) return false;
            Exception failure = null;
            foreach (BattleButton button in new List<BattleButton>(held.Keys))
            {
                try { Release(button); }
                catch (Win32Exception error) { failure = error; }
            }
            if (failure != null) throw failure;
            return true;
        }

        private bool TapKey(int key, int milliseconds, CancellationToken cancellation)
        {
            ushort scan = (ushort)Native.MapVirtualKey((uint)key, 0);
            if (scan == 0)
                return false;
            return Tap(Key(scan, ScanCode), Key(scan, ScanCode | KeyUp), key, milliseconds, cancellation);
        }

        private bool Tap(Native.Input down, Native.Input up, int key, int milliseconds, CancellationToken cancellation)
        {
            if (!CanPress(key, cancellation))
                return false;

            InputDown(down);
            try
            {
                cancellation.WaitHandle.WaitOne(milliseconds);
            }
            finally
            {
                InputUp(up);
            }
            return true;
        }

        private bool CanPress(int key, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested || !window.Available() || Pressed(key))
                return false;
            foreach (int modifier in Modifiers)
                if (Pressed(modifier)) return false;
            return !cancellation.IsCancellationRequested && window.Available();
        }

        private static void InputDown(Native.Input input)
        {
            if (Native.SendInput(1, new[] { input }, InputSize) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未接受游戏输入。请检查两个程序的权限级别。");
        }

        private static void InputUp(Native.Input input)
        {
            var packet = new[] { input };
            if (Native.SendInput(1, packet, InputSize) != 1 && Native.SendInput(1, packet, InputSize) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未接受按键释放，战斗已停止。");
        }

        private static bool Pressed(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }

        private static Native.Input Mouse(uint flags)
        {
            return new Native.Input { Payload = new Native.InputPayload { Mouse = new Native.MouseInput { Flags = flags } } };
        }

        private static Native.Input Key(ushort scan, uint flags)
        {
            return new Native.Input { Type = 1, Payload = new Native.InputPayload
                { Keyboard = new Native.KeyboardInput { ScanCode = scan, Flags = flags } } };
        }
    }
}
