using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace TideCombat
{
    internal sealed class WindowMessages
    {
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
            if (reassert && holds.ContainsKey(button)) return !token.IsCancellationRequested && Post(down);
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
                return true;
            }
            try { token.WaitHandle.WaitOne(downMilliseconds); }
            finally { SendUp(up); }
            return true;
        }

        private static bool Post(Message message)
        {
            if (!GameWindow.Owns(message.Window, message.Owner)) return false;
            if (!Native.PostMessageW(message.Window, message.Code, message.Flags, message.Arg))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "游戏窗口没有接受后台按键。");
            return true;
        }

        private void SendUp(Message message)
        {
            mouseFlags &= ~message.MouseButton;
            if (message.MouseButton != 0) message.Flags = new IntPtr(mouseFlags);
            Post(message);
        }

        private void Release(BattleButton button)
        {
            Message release;
            if (!holds.TryGetValue(button, out release)) return;
            SendUp(release);
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
