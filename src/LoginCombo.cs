using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TideCombat
{
    // Reads and selects the native KRSDK login ComboBox so account switching also
    // works when the game runs in the background.
    internal sealed class LoginCombo
    {
        private const uint CbGetCount = 0x146, CbGetCurSel = 0x147, CbGetLbText = 0x148;
        private const uint CbGetLbTextLen = 0x149, CbSetCurSel = 0x14E;
        private const uint WmCommand = 0x0111;
        private const int CbnSelChange = 1;

        private delegate bool EnumProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, StringBuilder lparam);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);

        internal IntPtr Handle { get; private set; }

        internal static LoginCombo Find(IntPtr gameWindow, uint gameProcess)
        {
            uint processId;
            if (!IsWindow(gameWindow) || GetWindowThreadProcessId(gameWindow, out processId) == 0 || processId != gameProcess)
                return null;
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate (IntPtr window, IntPtr data)
            {
                if (GetClassNameOf(window) != "#32770" || !IsWindowVisible(window))
                    return true;
                uint process;
                GetWindowThreadProcessId(window, out process);
                if (process != gameProcess)
                    return true;
                EnumChildWindows(window, delegate (IntPtr child, IntPtr unused)
                {
                    if (GetClassNameOf(child) != "ComboBox" || !IsWindowVisible(child))
                        return true;
                    if (SendMessage(child, CbGetCount, IntPtr.Zero, IntPtr.Zero).ToInt64() <= 0)
                        return true;
                    found = child;
                    return false;
                }, IntPtr.Zero);
                return found == IntPtr.Zero;
            }, IntPtr.Zero);
            return found == IntPtr.Zero ? null : new LoginCombo { Handle = found };
        }

        private static string GetClassNameOf(IntPtr window)
        {
            var text = new StringBuilder(64);
            GetClassName(window, text, text.Capacity);
            return text.ToString();
        }

        internal List<string> Items()
        {
            var items = new List<string>();
            if (Handle == IntPtr.Zero)
                return items;
            long count = SendMessage(Handle, CbGetCount, IntPtr.Zero, IntPtr.Zero).ToInt64();
            for (int index = 0; index < count; index++)
            {
                long length = SendMessage(Handle, CbGetLbTextLen, new IntPtr(index), IntPtr.Zero).ToInt64();
                var buffer = new StringBuilder((int)Math.Max(0, length) + 1);
                SendMessage(Handle, CbGetLbText, new IntPtr(index), buffer);
                items.Add(buffer.ToString());
            }
            return items;
        }

        internal string Selected()
        {
            if (Handle == IntPtr.Zero)
                return null;
            long index = SendMessage(Handle, CbGetCurSel, IntPtr.Zero, IntPtr.Zero).ToInt64();
            List<string> items = Items();
            return index >= 0 && index < items.Count ? items[(int)index] : null;
        }

        internal bool Select(int index)
        {
            if (Handle == IntPtr.Zero)
                return false;
            SendMessage(Handle, CbSetCurSel, new IntPtr(index), IntPtr.Zero);
            IntPtr parent = GetParent(Handle);
            long control = GetDlgCtrlID(Handle);
            long command = ((long)CbnSelChange << 16) | (control & 0xFFFF);
            SendMessage(parent, WmCommand, new IntPtr(command), Handle);
            return SendMessage(Handle, CbGetCurSel, IntPtr.Zero, IntPtr.Zero).ToInt64() == index;
        }
    }
}
