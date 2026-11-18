using System;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class Program
    {
        internal const string ResumeBattle = "--resume-battle";

        [STAThread]
        private static void Main(string[] arguments)
        {
            Native.EnablePhysicalCoordinates();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool resume = arguments.Length == 1 && arguments[0] == ResumeBattle;
            Mutex instance;
            try { instance = new Mutex(false, @"Local\TideCombatDesktop"); }
            catch (UnauthorizedAccessException)
            {
                ShowAlreadyRunning();
                return;
            }
            using (instance)
            {
                bool acquired;
                try { acquired = instance.WaitOne(resume ? 12000 : 0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    ShowAlreadyRunning();
                    return;
                }
                try
                {
                    using (var form = new MainForm())
                    {
                        if (resume) form.Shown += delegate { form.ResumeAfterElevation(); };
                        Application.Run(form);
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
        }

        private static void ShowAlreadyRunning()
        {
            MessageBox.Show("自动战斗工具已经打开。请退出旧窗口后再打开此版本。", "鸣潮自动战斗",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
