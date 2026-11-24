using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace TideCombat
{
    internal sealed class WindowMessages
    {
        private const uint VisibleInteractiveChildren = 0x0001 | 0x0002 | 0x0004;
        private readonly Dictionary<BattleButton, Message> holds = new Dictionary<BattleButton, Message>();
        private int mouseFlags;
        private IntPtr target;
        private uint owner;

        private struct Message
        {
            internal IntPtr Window;
            internal uint Owner;
            internal uint Code;
            internal IntPtr Flags, Arg;
            internal int MouseButton;
        }

        private struct ClickTarget
        {
            internal IntPtr Window;
            internal int Position, ScreenPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            internal int Size, Flags;
            internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal Native.Rect CaretPosition;
        }

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

        internal static bool ClickMenu(IntPtr root, uint process, Point client, FarmMouse button, int holdMs,
            int settleMs, CancellationToken token, Func<bool> stillAt)
        {
            return Click(root, process, client, button, holdMs, settleMs, token, stillAt, true);
        }

        internal static bool ClickBackground(IntPtr root, uint process, Point client, FarmMouse button, int holdMs,
            int settleMs, CancellationToken token, Func<bool> stillAt, int modifiers = 0)
        {
            return Click(root, process, client, button, holdMs, settleMs, token, stillAt, false, modifiers);
        }

        private static bool Click(IntPtr root, uint process, Point client, FarmMouse button, int holdMs,
            int settleMs, CancellationToken token, Func<bool> stillAt, bool notifyActivation, int modifiers = 0)
        {
            ClickTarget target;
            if (token.IsCancellationRequested || !FindClickTarget(root, process, client, out target))
                return false;

            if (notifyActivation && (!PostMenu(root, process, 0x0006, 1, 0) ||
                target.Window != root && !PostMenu(target.Window, process, 0x0006, 1, 0)))
                return false;
            if (!PostMenu(target.Window, process, 0x0200, modifiers, target.Position))
                return false;

            ClickTarget ready;
            if (token.WaitHandle.WaitOne(settleMs) || !stillAt() ||
                !FindClickTarget(root, process, client, out ready) ||
                ready.Window != target.Window || ready.Position != target.Position || token.IsCancellationRequested)
                return false;

            uint down = button == FarmMouse.Left ? 0x0201u : button == FarmMouse.Right ? 0x0204u : 0x0207u;
            int flags = button == FarmMouse.Left ? 1 : button == FarmMouse.Right ? 2 : 16;
            if (!PostMenu(target.Window, process, down, flags | modifiers, target.Position))
                return false;
            bool released;
            try { token.WaitHandle.WaitOne(Math.Max(1, holdMs)); }
            finally { released = PostMenu(target.Window, process, down + 1, modifiers, target.Position); }
            return released;
        }

        internal static bool ScrollBackground(IntPtr root, uint process, Point client, int clicks, int modifiers = 0)
        {
            ClickTarget target;
            if (clicks == 0 || !FindClickTarget(root, process, client, out target))
                return false;
            int flags = ((clicks * 120 & 0xffff) << 16) | modifiers;
            return PostMenu(target.Window, process, 0x020A, flags, target.ScreenPosition);
        }

        private static bool FindClickTarget(IntPtr root, uint process, Point client, out ClickTarget result)
        {
            result = default(ClickTarget);
            var screen = new Native.Point { X = client.X, Y = client.Y };
            if (!GameWindow.Owns(root, process) || !Native.ClientToScreen(root, ref screen))
                return false;

            IntPtr window = root;
            while (GameWindow.Owns(window, process))
            {
                Native.Point local = screen;
                Native.Rect bounds;
                if (!Native.ScreenToClient(window, ref local) || !Native.GetClientRect(window, out bounds) ||
                    local.X < 0 || local.Y < 0 || local.X >= bounds.Right || local.Y >= bounds.Bottom)
                    return false;

                IntPtr child = Native.ChildWindowFromPointEx(window, local, VisibleInteractiveChildren);
                if (child == window || !GameWindow.Owns(child, process))
                {
                    result.Window = window;
                    result.Position = PackPoint(local);
                    result.ScreenPosition = PackPoint(screen);
                    return true;
                }
                window = child;
            }
            return false;
        }

        private static int PackPoint(Native.Point point)
        {
            return (point.X & 0xffff) | (point.Y & 0xffff) << 16;
        }

        private static bool PostMenu(IntPtr window, uint process, uint code, int flags, int position)
        {
            return Post(new Message { Window = window, Owner = process, Code = code,
                Flags = new IntPtr(flags), Arg = new IntPtr(position) });
        }

        internal static bool TapKey(IntPtr window, uint process, ushort key, int holdMs, CancellationToken token,
            Func<bool> ready = null)
        {
            Func<bool> release;
            if (token.IsCancellationRequested || !TryHoldKey(window, process, key, out release, ready))
                return false;
            bool released;
            try { token.WaitHandle.WaitOne(Math.Max(1, holdMs)); }
            finally { released = release(); }
            return released;
        }

        internal static bool TryHoldKey(IntPtr window, uint process, ushort key, out Func<bool> release,
            Func<bool> ready = null)
        {
            release = null;
            uint scan = Native.MapVirtualKey(key, 0);
            uint down = key == 0x12 ? 0x0104u : 0x0100u;
            int altContext = key == 0x12 ? 0x20000000 : 0;
            if (scan == 0 || ready != null && !ready() ||
                !PostMenu(window, process, down, key, KeyState(scan, false) | altContext))
                return false;
            release = () => PostMenu(window, process, down + 1, key, KeyState(scan, true) | altContext);
            return true;
        }

        internal static bool TypeText(IntPtr root, uint process, string text, CancellationToken token)
        {
            if (string.IsNullOrEmpty(text) || token.IsCancellationRequested)
                return false;
            uint actual;
            uint thread = Native.GetWindowThreadProcessId(root, out actual);
            if (thread == 0 || process == 0 || actual != process)
                return false;
            IntPtr target = root;
            var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
            if (GetGUIThreadInfo(thread, ref info) && GameWindow.Owns(info.Focus, process) &&
                Native.GetAncestor(info.Focus, 2) == root)
                target = info.Focus;
            foreach (char code in text)
                if (!PostMenu(target, process, 0x0102, code, 1))
                    return false;
            return true;
        }

        internal bool Send(BattleAction action, IntPtr window, uint process, CancellationToken token)
        {
            if (action.Kind == ActionKind.Release)
            {
                Release(action.Button);
                return true;
            }
            if (token.IsCancellationRequested || !GameWindow.Owns(window, process))
                return false;
            if (target != window || owner != process)
            {
                ReleaseAll();
                target = window;
                owner = process;
            }
            switch (action.Kind)
            {
                case ActionKind.Target: return PressMouse(BattleButton.Attack, 16, false, token);
                case ActionKind.Attack: return PressMouse(BattleButton.Attack, 1, false, token);
                case ActionKind.Switch: return PressKey(BattleButton.Attack, '1' + action.Slot, false, token);
                case ActionKind.Cast: return PressKey(BattleButton.Attack, action.Key, false, token);
                case ActionKind.Hold:
                case ActionKind.Button:
                    bool hold = action.Kind == ActionKind.Hold;
                    int downMilliseconds = action.DownMilliseconds > 0 ? action.DownMilliseconds : 32;
                    if (action.Button == BattleButton.MiddleClick)
                        return PressMouse(action.Button, 16, hold, token, downMilliseconds);
                    if (action.Button == BattleButton.Attack || action.Button == BattleButton.Dodge)
                        return PressMouse(action.Button, action.Button == BattleButton.Attack ? 1 : 2, hold, token, downMilliseconds);
                    return PressKey(action.Button, action.NativeKey, hold, token, downMilliseconds, action.ReassertHold);
                default: return false;
            }
        }

        private bool PressKey(BattleButton button, int key, bool hold, CancellationToken token, int downMilliseconds = 32,
            bool reassert = false)
        {
            uint scan = Native.MapVirtualKey((uint)key, 0);
            if (scan == 0) return false;
            Message down = Make(0x0100, key, KeyState(scan, false), 0);
            if (reassert && holds.ContainsKey(button))
            {
                if (token.IsCancellationRequested || !Post(down)) return false;
                LogHold(button, down, "再次按下");
                return true;
            }
            return Press(button, down, Make(0x0101, key, KeyState(scan, true), 0), hold, token, downMilliseconds);
        }

        internal static int KeyState(uint scan, bool release)
        {
            return unchecked((int)(1u | scan << 16 | (release ? 0xc0000000u : 0)));
        }

        private bool PressMouse(BattleButton button, int flags, bool hold, CancellationToken token, int downMilliseconds = 32)
        {
            if ((mouseFlags & flags) != 0) return hold;
            Native.Rect rect;
            if (!Native.GetClientRect(target, out rect)) return false;
            int xy = (rect.Right / 2 & 0xffff) | (rect.Bottom / 2 & 0xffff) << 16;
            uint code = flags == 1 ? 0x0201u : flags == 2 ? 0x0204u : 0x0207u;
            Message down = Make(code, mouseFlags | flags, xy, flags);
            return Press(button, down, Make(code + 1, mouseFlags, xy, flags), hold, token, downMilliseconds);
        }

        private Message Make(uint code, int flags, int arg, int mouse)
        {
            return new Message { Window = target, Owner = owner,
                Code = code, Flags = new IntPtr(flags), Arg = new IntPtr(arg), MouseButton = mouse };
        }

        private bool Press(BattleButton button, Message down, Message up, bool hold, CancellationToken token, int downMilliseconds)
        {
            if (hold && holds.ContainsKey(button)) return true;
            if (token.IsCancellationRequested || !Post(down)) return false;
            mouseFlags |= down.MouseButton;
            if (hold)
            {
                holds.Add(button, up);
                LogHold(button, down, "开始长按");
                return true;
            }
            try { token.WaitHandle.WaitOne(downMilliseconds); }
            finally { SendUp(up); }
            return true;
        }

        private static void LogHold(BattleButton button, Message message, string operation)
        {
            string key = message.MouseButton == 0 ? " VK=0x" + message.Flags.ToInt64().ToString("X2") : string.Empty;
            RunLog.Info("Input", string.Format("后台战斗输入｜{0} PID={1} 操作={2}{3}", operation, message.Owner, button, key));
        }

        private static bool Post(Message message)
        {
            if (!GameWindow.Owns(message.Window, message.Owner)) return false;
            if (!Native.PostMessageW(message.Window, message.Code, message.Flags, message.Arg))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未能向游戏窗口投递输入消息。");
            return true;
        }

        private bool SendUp(Message message)
        {
            mouseFlags &= ~message.MouseButton;
            if (message.MouseButton != 0) message.Flags = new IntPtr(mouseFlags);
            bool sent = Post(message);
            if (!sent)
                RunLog.Warn("Input", "未投递后台松开消息，目标窗口已失效｜PID=" + message.Owner);
            return sent;
        }

        private void Release(BattleButton button)
        {
            Message release;
            if (!holds.TryGetValue(button, out release)) return;
            if (SendUp(release))
                LogHold(button, release, "松开长按");
            holds.Remove(button);
        }

        internal bool ReleaseAll()
        {
            if (holds.Count == 0) return false;
            Win32Exception error = null;
            foreach (BattleButton button in new List<BattleButton>(holds.Keys))
            {
                try { Release(button); }
                catch (Win32Exception failure) { error = failure; }
            }
            if (error != null) throw error;
            return true;
        }
    }
}
