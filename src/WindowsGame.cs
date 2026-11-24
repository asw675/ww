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
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        internal delegate bool EnumerateWindow(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumerateWindow action, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr flags, IntPtr param);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, uint index, out Rect rect, int bytes);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Point point, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint type);
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

    internal enum GameLanguage { Unknown, SimplifiedChinese, TraditionalChinese, English }

    internal sealed class GameWindow
    {
        internal const string LocalProcessName = "Client-Win64-Shipping";
        private readonly GameSource source;
        private readonly bool requireRestoredWindow;
        private IntPtr hwnd;
        private uint pid;
        private Rectangle lastBounds;
        private string lastLocateReason;
        private GameSource lastLocateMode;
        private Size lastLocateSize;
        internal IntPtr Handle { get { return hwnd; } }
        internal uint ProcessId { get { return pid; } }
        internal bool InputBlockedByIntegrity { get; private set; }
        internal GameSource Mode { get; private set; }

        internal GameWindow(GameSource source = GameSource.Foreground, bool requireRestoredWindow = false)
        {
            this.source = source;
            this.requireRestoredWindow = requireRestoredWindow || source == GameSource.AutoLocal;
            Mode = ResolveMode(source, false);
        }

        internal static GameWindow ForTask(GameSource source)
        {
            return new GameWindow(source, requireRestoredWindow: true);
        }

        internal static GameSource ResolveMode(GameSource source, bool gameForeground)
        {
            if (source != GameSource.AutoLocal)
                return source;
            return gameForeground ? GameSource.Foreground : GameSource.Background;
        }

        internal bool TryLocate(out Rectangle bounds, out string reason)
        {
            IntPtr previousHandle = hwnd;
            uint previousProcess = pid;
            bool located = TryLocateCore(out bounds, out reason);
            if (lastLocateReason != reason || lastLocateMode != Mode || lastLocateSize != bounds.Size ||
                previousHandle != hwnd || previousProcess != pid)
            {
                lastLocateReason = reason;
                lastLocateMode = Mode;
                lastLocateSize = bounds.Size;
                if (located)
                {
                    RunLog.Info("Window", string.Format("游戏窗口可用｜来源={0} 模式={1} PID={2} 尺寸={3}×{4} 输入权限受限={5}",
                        source, Mode, pid, bounds.Width, bounds.Height, InputBlockedByIntegrity));
                }
                else
                {
                    RunLog.Info("Window", string.Format("游戏窗口暂不可用｜来源={0} 模式={1} 原因={2}", source, Mode, reason));
                }
            }
            return located;
        }

        private bool TryLocateCore(out Rectangle bounds, out string reason)
        {
            bounds = Rectangle.Empty;
            IntPtr current = Native.GetForegroundWindow();
            uint currentPid;
            if (source == GameSource.Background || source == GameSource.AutoLocal)
            {
                bool gameForeground = IsCurrentGame(current) && !Native.IsIconic(current);
                Mode = ResolveMode(source, gameForeground);
                if (!gameForeground)
                    current = FindBackground();
            }
            if (current == IntPtr.Zero || Mode != GameSource.Background && Native.IsIconic(current) ||
                Native.GetWindowThreadProcessId(current, out currentPid) == 0)
            {
                reason = WaitingText();
                return false;
            }
            if (current != hwnd || currentPid != pid)
            {
                if (!IsGame(current, currentPid, source))
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
            if (Mode == GameSource.Background && Native.IsIconic(current))
            {
                if (requireRestoredWindow)
                {
                    reason = "游戏窗口已最小化，等待恢复";
                    return false;
                }
                if (!lastBounds.IsEmpty)
                {
                    bounds = lastBounds;
                    reason = string.Empty;
                    return true;
                }
            }
            if (!ReadBounds(current, out bounds, out reason))
                return false;
            lastBounds = bounds;
            return true;
        }

        internal bool TryGetForegroundBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            string reason;
            return Mode != GameSource.Background && Available() &&
                ReadBounds(hwnd, out bounds, out reason) && Available();
        }

        private bool ReadBounds(IntPtr current, out Rectangle bounds, out string reason)
        {
            bounds = Rectangle.Empty;
            Native.Rect client;
            var position = new Native.Point();
            if (!Native.GetClientRect(current, out client) || !Native.ClientToScreen(current, ref position))
            {
                reason = "暂时无法读取游戏窗口";
                return false;
            }

            var size = new Size(client.Right - client.Left, client.Bottom - client.Top);
            if (!SupportedSize(size))
            {
                reason = "请使用 1280×720 或更高的 16:9 游戏画面";
                return false;
            }
            bounds = new Rectangle(position.X, position.Y, size.Width, size.Height);
            if (Mode != GameSource.Background && !Screen.AllScreens.AnyContains(bounds))
            {
                reason = "请把整个游戏窗口移到同一块屏幕上";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        internal bool StillForeground()
        {
            uint foregroundPid;
            return hwnd != IntPtr.Zero && Native.GetForegroundWindow() == hwnd &&
                Native.GetWindowThreadProcessId(hwnd, out foregroundPid) != 0 && foregroundPid == pid && !Native.IsIconic(hwnd);
        }

        internal bool TryActivateForAutoTask()
        {
            if (source != GameSource.AutoLocal || InputBlockedByIntegrity || !Owns(hwnd, pid) ||
                !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
                return false;
            if (StillForeground())
                return true;

            uint ownProcess;
            using (var self = Process.GetCurrentProcess())
                ownProcess = (uint)self.Id;
            if (!Owns(Native.GetForegroundWindow(), ownProcess))
            {
                RunLog.Info("Window", "前台重试未切换窗口，TideCombat 当前不拥有焦点");
                return false;
            }

            bool activated = Owns(hwnd, pid) && Native.SetForegroundWindow(hwnd);
            RunLog.Info("Window", "向 Windows 请求切到已识别的游戏窗口｜PID=" + pid + " 受理=" + activated);
            return activated || StillForeground();
        }

        internal bool Available()
        {
            if (Mode == GameSource.Background)
                return Owns(hwnd, pid) &&
                    (!requireRestoredWindow || Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd)) &&
                    (source != GameSource.AutoLocal || !StillForeground());
            return StillForeground() && (Mode != GameSource.Cloud || IsCloudTitle(hwnd));
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
                (window == hwnd && actual == pid || IsGame(window, actual, source));
        }

        private IntPtr FindBackground()
        {
            if (Owns(hwnd, pid) && Native.IsWindowVisible(hwnd) && CaptureSize(hwnd) > 0)
                return hwnd;
            IntPtr selected = IntPtr.Zero;
            long bestSize = -1;
            Native.EnumWindows((window, data) =>
            {
                if (!Native.IsWindowVisible(window) || !IsCurrentGame(window)) return true;
                long size = CaptureSize(window);
                if (size > bestSize)
                {
                    selected = window;
                    bestSize = size;
                }
                return true;
            }, IntPtr.Zero);
            return selected;
        }

        private static long CaptureSize(IntPtr window)
        {
            Native.Rect client;
            if (Native.IsIconic(window) || !Native.GetClientRect(window, out client))
                return 0;
            var size = new Size(client.Right - client.Left, client.Bottom - client.Top);
            return SupportedSize(size) ? (long)size.Width * size.Height : 0;
        }

        internal static bool SupportedSize(Size size)
        {
            if (size.Width < 1280 || size.Height < 720)
                return false;
            // Match the reference 16:9 capture contract, allowing the nearest
            // pixel when a display's nominal ratio is rounded (e.g. 1366x768).
            return Math.Abs((long)size.Width * 9 - (long)size.Height * 16) <= 9;
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

        internal GameLanguage Language
        {
            get { return DetectLanguage(ReadTitle(hwnd), Mode == GameSource.Cloud || Mode == GameSource.ManagedCloud); }
        }

        internal static GameLanguage DetectLanguage(string title, bool browser)
        {
            if (browser || title != null && title.Contains("鸣潮")) return GameLanguage.SimplifiedChinese;
            if (string.IsNullOrEmpty(title)) return GameLanguage.Unknown;
            if (title.Contains("Wuthering")) return GameLanguage.English;
            return title.Contains("鳴潮") ? GameLanguage.TraditionalChinese : GameLanguage.Unknown;
        }

        private static string ReadTitle(IntPtr window)
        {
            if (window == IntPtr.Zero) return string.Empty;
            var text = new StringBuilder(160);
            Native.GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        private static bool IsCloudTitle(IntPtr window)
        {
            string text = ReadTitle(window);
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

        internal PixelFrame Read(Rectangle bounds, bool retainEcho = false)
        {
            if (source == null || source.Size != bounds.Size)
            {
                ReleaseSource();
                source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
                sourceGraphics = Graphics.FromImage(source);
            }
            sourceGraphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size,
                CopyPixelOperation.SourceCopy);
            return reader.Read(source, retainEcho);
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
        private byte[] pixels, echoPixels;
        private readonly CloudTargetColors cloudColors;
        private readonly CamellyaColors camellyaColors = new CamellyaColors();
        private readonly FramePatch concerto = new FramePatch(), monthlyCard = new FramePatch();

        internal FrameReader(bool retainCloudColors = false)
        {
            if (retainCloudColors) cloudColors = new CloudTargetColors();
        }

        // The frame is valid until this reader is used again.
        internal PixelFrame Read(Bitmap source, bool retainEcho = false)
        {
            return Read(source, new Rectangle(Point.Empty, source.Size), retainEcho);
        }

        internal PixelFrame Read(Bitmap source, Rectangle crop, bool retainEcho = false)
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
            Rectangle cardArea = MonthlyCardVision.SearchArea(crop.Size);
            cardArea.Offset(crop.Location);
            monthlyCard.Read(source, cardArea);
            if (retainEcho)
            {
                int rowBytes = crop.Width * 4, length = rowBytes * crop.Height;
                if (echoPixels == null || echoPixels.Length != length) echoPixels = new byte[length];
                BitmapData raw = source.LockBits(crop, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    if (raw.Stride == rowBytes) Marshal.Copy(raw.Scan0, echoPixels, 0, length);
                    else
                        for (int y = 0; y < crop.Height; y++)
                            Marshal.Copy(IntPtr.Add(raw.Scan0, raw.Stride * y), echoPixels, rowBytes * y, rowBytes);
                }
                finally { source.UnlockBits(raw); }
            }
            return new PixelFrame(pixels, width, StandardHeight, cloudColors, crop.Width, camellyaColors, concerto,
                crop.Height, retainEcho ? echoPixels : null, monthlyCard);
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
        private WindowMessages messages;
        private long nextSkippedInput;

        internal WindowInput(GameWindow window)
        {
            this.window = window;
        }

        internal bool Send(BattleAction action, CancellationToken cancellation)
        {
            if (action.Kind == ActionKind.Release)
            {
                Release(action.Button);
                if (messages != null)
                    messages.Send(action, window.Handle, window.ProcessId, cancellation);
                return true;
            }
            if (window.InputBlockedByIntegrity)
                throw new InputElevationRequiredException();
            if (!window.Available())
            {
                if (!cancellation.IsCancellationRequested)
                    SkipInput("游戏窗口或输入模式已变化");
                return false;
            }
            bool sent;
            if (window.Mode == GameSource.Background)
            {
                if (messages == null) messages = new WindowMessages();
                sent = messages.Send(action, window.Handle, window.ProcessId, cancellation);
                if (!sent && action.Kind != ActionKind.Wait && !cancellation.IsCancellationRequested)
                    SkipInput("游戏窗口未接受后台操作 " + action.Label);
            }
            else
                sent = SendForeground(action, cancellation);
            if (sent && action.Kind != ActionKind.Hold)
                LogAction(action, action.Kind.ToString());
            return sent;
        }

        private void LogAction(BattleAction action, string operation)
        {
            int key = action.Kind == ActionKind.Cast ? action.Key : action.NativeKey;
            if (action.Kind == ActionKind.Switch)
                key = '1' + action.Slot;
            string keyDetail = key == 0 ? string.Empty : " VK=0x" + key.ToString("X2");
            RunLog.Info("Input", string.Format("已发送战斗操作｜模式={0} PID={1} 操作={2} {3}{4}",
                window.Mode, window.ProcessId, operation, action.Label, keyDetail));
        }

        private bool SendForeground(BattleAction action, CancellationToken cancellation)
        {
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
                    LogAction(action, "再次按下");
                }
                return true;
            }
            if (!CanPress(key, cancellation))
                return false;
            InputDown(down);
            held.Add(action.Button, up);
            LogAction(action, "开始长按");
            return true;
        }

        private bool TapMiddleAtCenter(Native.Input down, Native.Input up, int milliseconds, CancellationToken cancellation)
        {
            Rectangle bounds;
            if (!CanPress(0x04, cancellation) || !window.TryGetForegroundBounds(out bounds)) return false;
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
            RunLog.Info("Input", "已松开前台战斗长按｜操作=" + BattleAction.Release(button).Label);
        }

        internal bool ReleaseAll()
        {
            bool released = held.Count > 0;
            Exception failure = null;
            foreach (BattleButton button in new List<BattleButton>(held.Keys))
            {
                try { Release(button); }
                catch (Win32Exception error) { failure = error; }
            }
            if (messages != null)
            {
                try { released |= messages.ReleaseAll(); }
                catch (Win32Exception error) { failure = failure ?? error; }
            }
            if (failure != null) throw failure;
            return released;
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
            if (cancellation.IsCancellationRequested)
                return false;
            if (!window.Available())
                return SkipInput("游戏窗口或前台焦点已变化");
            if (Pressed(key))
                return SkipInput("目标键已被按住");
            foreach (int modifier in Modifiers)
                if (Pressed(modifier)) return SkipInput("检测到修饰键仍在按住");
            return !cancellation.IsCancellationRequested && window.Available();
        }

        private bool SkipInput(string reason)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= nextSkippedInput)
            {
                nextSkippedInput = now + 3 * Stopwatch.Frequency;
                RunLog.Warn("Input", "战斗输入未发出｜原因=" + reason);
            }
            return false;
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
