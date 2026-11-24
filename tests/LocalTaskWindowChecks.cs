using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class LocalTaskWindowChecks
    {
        private const string FixtureClass = "UnrealWindow";
        private const int Hide = 0, ShowWithoutActivation = 4, MinimizeWithoutActivation = 7;
        private const uint Popup = 0x80000000, NoActivate = 0x08000000, ToolWindow = 0x00000080;
        private const uint CloseWindow = 0x0010, Destroyed = 0x0002;
        private static readonly WindowProcedure DispatchFixtureMessage = FixtureMessage;
        private static IntPtr fixtureGame;

        private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr flags, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Style;
            public WindowProcedure Procedure;
            public int ClassExtra, WindowExtra;
            public IntPtr Instance, Icon, Cursor, Background;
            public string Menu, Name;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WindowClass definition);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
            int left, int top, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr flags, IntPtr data);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint options);
        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int code);

        internal static bool CanStartFixture()
        {
            // A running copy of TideCombat could discover the fixture while waiting for the real game.
            return !HasProcess(GameWindow.LocalProcessName) && !HasProcess("TideCombat");
        }

        private static bool HasProcess(string name)
        {
            Process[] processes = Process.GetProcessesByName(name);
            foreach (Process process in processes)
                process.Dispose();
            return processes.Length != 0;
        }

        internal static void DiscoversBackgroundWindow()
        {
            string directory = Path.Combine(Path.GetTempPath(), "TideCombat-window-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                // The production matcher checks the process basename as well as the window class.
                string source = typeof(LocalTaskWindowChecks).Assembly.Location;
                string executable = Path.Combine(directory, GameWindow.LocalProcessName + ".exe");
                File.Copy(source, executable);
                if (File.Exists(source + ".config"))
                    File.Copy(source + ".config", executable + ".config");
                var start = new ProcessStartInfo(executable, "--local-window-fixture")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true
                };
                using (Process child = Process.Start(start))
                {
                    try { CheckFixture(child); }
                    finally
                    {
                        child.StandardInput.Close();
                        if (!child.WaitForExit(5000) && !child.HasExited)
                        {
                            child.Kill();
                            Require(child.WaitForExit(5000), "The test game process did not exit.");
                        }
                    }
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void CheckFixture(Process child)
        {
            var ready = child.StandardOutput.ReadLineAsync();
            Require(ready.Wait(5000) && !string.IsNullOrEmpty(ready.Result), "The test game did not create its windows.");
            string[] handles = ready.Result.Split(' ');
            Require(handles.Length == 2, "The test game returned invalid window identifiers.");
            var game = new IntPtr(long.Parse(handles[0], CultureInfo.InvariantCulture));
            var splash = new IntPtr(long.Parse(handles[1], CultureInfo.InvariantCulture));
            uint pid = (uint)child.Id;
            Require(GameWindow.Owns(game, pid) && GameWindow.Owns(splash, pid), "The windows do not belong to the test game.");
            Require(Native.IsWindowVisible(game) && Native.IsWindowVisible(splash) &&
                Native.GetForegroundWindow() != game && Native.GetForegroundWindow() != splash,
                "The test windows must remain visible without taking focus.");

            IntPtr first = IntPtr.Zero;
            Native.EnumWindows((window, unused) =>
            {
                if (window != game && window != splash)
                    return true;
                first = window;
                return false;
            }, IntPtr.Zero);
            Require(first == splash, "The splash must be enumerated before the game.");

            var windowForTask = GameWindow.ForTask(GameSource.AutoLocal);
            Rectangle bounds;
            string reason;
            Require(windowForTask.TryLocate(out bounds, out reason) && windowForTask.Handle == game &&
                windowForTask.ProcessId == pid && windowForTask.Mode == GameSource.Background &&
                bounds.Size == new Size(1280, 720) && windowForTask.Available(),
                "Automatic task mode did not find the background game behind its splash: " + reason);
            var foregroundOnly = GameWindow.ForTask(GameSource.Foreground);
            Require(!foregroundOnly.TryLocate(out bounds, out reason), "The manual foreground setting started in the background.");

            ChangeVisibility(splash, pid, Hide, () => !Native.IsWindowVisible(splash));
            ChangeVisibility(game, pid, MinimizeWithoutActivation, () => Native.IsIconic(game));
            Require(!windowForTask.Available() && !windowForTask.TryLocate(out bounds, out reason) &&
                reason.Contains("最小化"), "A minimized task must wait for restoration.");
            ChangeVisibility(game, pid, ShowWithoutActivation, () => Native.IsWindowVisible(game) && !Native.IsIconic(game));
            Require(windowForTask.TryLocate(out bounds, out reason) && windowForTask.Handle == game &&
                windowForTask.Mode == GameSource.Background && Native.GetForegroundWindow() != game,
                "Automatic task mode did not recover from minimization in the background: " + reason);
        }

        private static void ChangeVisibility(IntPtr window, uint pid, int command, Func<bool> changed)
        {
            Require(GameWindow.Owns(window, pid) && ShowWindowAsync(window, command) && SpinWait.SpinUntil(changed, 5000),
                "A test game window did not change visibility.");
        }

        internal static int RunFixture()
        {
            IntPtr instance = GetModuleHandle(null);
            var definition = new WindowClass { Procedure = DispatchFixtureMessage, Instance = instance, Name = FixtureClass };
            if (RegisterClass(ref definition) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            fixtureGame = MakeFixtureWindow(instance, 1280, 720);
            IntPtr splash = MakeFixtureWindow(instance, 320, 180);
            const uint noMoveSizeActivation = 0x0001 | 0x0002 | 0x0010;
            if (!SetWindowPos(splash, IntPtr.Zero, 0, 0, 0, 0, noMoveSizeActivation))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} {1}", fixtureGame.ToInt64(), splash.ToInt64()));

            var closeOnInputEnd = new Thread(() =>
            {
                Console.ReadLine();
                Native.PostMessageW(fixtureGame, CloseWindow, IntPtr.Zero, IntPtr.Zero);
            }) { IsBackground = true };
            closeOnInputEnd.Start();
            Application.Run();
            DestroyWindow(splash);
            return 0;
        }

        private static IntPtr MakeFixtureWindow(IntPtr instance, int width, int height)
        {
            IntPtr window = CreateWindowEx(NoActivate | ToolWindow, FixtureClass, "TideCombat offline window fixture", Popup,
                -20000, -20000, width, height, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (window == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            ShowWindow(window, ShowWithoutActivation);
            return window;
        }

        private static IntPtr FixtureMessage(IntPtr window, uint message, IntPtr flags, IntPtr data)
        {
            if (window == fixtureGame && message == Destroyed)
                PostQuitMessage(0);
            return DefWindowProc(window, message, flags, data);
        }

        private static void Require(bool condition, string failure)
        {
            if (!condition) throw new InvalidOperationException(failure);
        }
    }
}
