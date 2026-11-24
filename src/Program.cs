using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class Program
    {
        internal const string ResumeBattle = "--resume-battle";
        internal const string ResumeTask = "--resume-task";

        internal static bool TryReadResume(string[] arguments, out MainForm.WorkMode mode)
        {
            mode = MainForm.WorkMode.Battle;
            if (arguments.Length == 1 && arguments[0] == ResumeBattle)
                return true;

            MainForm.WorkMode requested;
            if (arguments.Length != 2 || arguments[0] != ResumeTask ||
                !Enum.IsDefined(typeof(MainForm.WorkMode), arguments[1]) ||
                !Enum.TryParse(arguments[1], out requested) ||
                requested == MainForm.WorkMode.Preview)
                return false;

            mode = requested;
            return true;
        }

        [STAThread]
        private static void Main(string[] arguments)
        {
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string primary = string.IsNullOrEmpty(localData) ? null : Path.Combine(localData, "TideCombat", "logs");
            if (primary == null || !RunLog.Start(primary))
            {
                string failure = RunLog.Failure;
                if (RunLog.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")))
                    RunLog.Warn("程序", "本地应用数据目录无法写入日志，已使用程序目录。 " + failure);
            }
            try
            {
                RunLog.Info("程序", "启动 TideCombat，系统=" + Environment.OSVersion.VersionString);
                RunApplication(arguments);
            }
            catch (Exception error)
            {
                RunLog.Error("程序", "未处理的程序异常", error);
                throw;
            }
            finally
            {
                RunLog.Info("程序", "退出 TideCombat");
                RunLog.Stop();
            }
        }

        private static void RunApplication(string[] arguments)
        {
            Native.EnablePhysicalCoordinates();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm.WorkMode resumeMode;
            bool resume = TryReadResume(arguments, out resumeMode);
            if (resume)
                RunLog.Info("程序", "准备在提权后恢复任务 " + resumeMode);
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
                        if (resume) form.Shown += delegate { form.ResumeAfterElevation(resumeMode); };
                        Application.Run(form);
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
        }

        private static void ShowAlreadyRunning()
        {
            RunLog.Warn("程序", "已有程序实例运行，本次启动结束");
            MessageBox.Show("自动战斗工具已经打开。请退出旧窗口后再打开此版本。", "鸣潮自动战斗",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
