using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TideCombat
{
    internal sealed class InputElevationRequiredException : Exception
    {
        internal InputElevationRequiredException() : base("《鸣潮》的运行权限更高，Windows 会拦截工具发送的按键。") { }
    }

    internal static class InputIntegrity
    {
        private const int AdministratorLevel = 0x3000;
        private const uint QueryLimitedProcess = 0x1000, QueryToken = 0x0008;
        private const int TokenIntegrityLevel = 25;
        private static readonly int CurrentLevel = ReadLevel(GetCurrentProcess());

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int infoClass,
            IntPtr data, uint size, out uint returnedSize);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        internal static bool CanRequestAdministrator { get { return CurrentLevel > 0 && CurrentLevel < AdministratorLevel; } }

        internal static bool IsBlocked(uint process)
        {
            return IsBlocked(CurrentLevel, GetProcessLevel(process));
        }

        internal static bool IsBlocked(int senderLevel, int receiverLevel)
        {
            return senderLevel > 0 && receiverLevel > senderLevel;
        }

        internal static bool LocalGameNeedsElevation()
        {
            int session;
            using (Process self = Process.GetCurrentProcess()) session = self.SessionId;
            bool higherGame = false, compatibleGame = false;
            foreach (Process process in Process.GetProcessesByName(GameWindow.LocalProcessName))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != session) continue;
                        if (IsBlocked((uint)process.Id)) higherGame = true;
                        else compatibleGame = true;
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            return higherGame && !compatibleGame;
        }

        internal static int GetProcessLevel(uint process)
        {
            IntPtr handle = OpenProcess(QueryLimitedProcess, false, process);
            if (handle == IntPtr.Zero) return 0;
            try { return ReadLevel(handle); }
            finally { CloseHandle(handle); }
        }

        private static int ReadLevel(IntPtr process)
        {
            IntPtr token;
            if (!OpenProcessToken(process, QueryToken, out token)) return 0;
            try
            {
                uint size;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out size);
                if (size == 0) return 0;
                IntPtr data = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, data, size, out size)) return 0;
                    IntPtr sid = Marshal.ReadIntPtr(data);
                    if (sid == IntPtr.Zero) return 0;
                    byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return count == 0 ? 0 : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                }
                finally { Marshal.FreeHGlobal(data); }
            }
            finally { CloseHandle(token); }
        }
    }
}
