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
        internal const string ResumeDaily = "--resume-daily";
        internal const string ResumeCommandTask = "--resume-command-task";

        // Preserve the one-based v3.8.4 registration order used by -t N.
        private sealed class ReferenceTask
        {
            internal readonly string Name;
            internal readonly MainForm.WorkMode Mode;
            internal readonly DomainKind? Domain;
            internal ReferenceTask(string name, MainForm.WorkMode mode, DomainKind? domain = null)
            { Name = name; Mode = mode; Domain = domain; }
        }

        private static readonly ReferenceTask[] ReferenceTasks =
        {
            new ReferenceTask("DailyTask", MainForm.WorkMode.Daily),
            new ReferenceTask("FarmEchoTask", MainForm.WorkMode.Farm),
            new ReferenceTask("NightmareNestTask", MainForm.WorkMode.Nightmare),
            new ReferenceTask("TacetTask", MainForm.WorkMode.Domain, DomainKind.Tacet),
            new ReferenceTask("ForgeryTask", MainForm.WorkMode.Domain, DomainKind.Forgery),
            new ReferenceTask("SimulationTask", MainForm.WorkMode.Domain, DomainKind.Simulation),
            new ReferenceTask("MultiAccountDailyTask", MainForm.WorkMode.MultiDaily),
            new ReferenceTask("MultiAccountConfigTask", MainForm.WorkMode.MultiConfig),
            new ReferenceTask("MergeEchoTask", MainForm.WorkMode.Merge),
            new ReferenceTask("EnhanceEchoTask", MainForm.WorkMode.Enhance),
            new ReferenceTask("ChangeEchoTask", MainForm.WorkMode.Change),
            new ReferenceTask("GardenTask", MainForm.WorkMode.Garden)
        };

        // The newer snapshot inserted activity tasks before the existing
        // registrations. Never silently reinterpret a saved numeric command.
        private static readonly ReferenceTask[] ReferenceTasks3814 =
        {
            ReferenceTasks[0],
            new ReferenceTask("GalGameTask", MainForm.WorkMode.SecondSola),
            new ReferenceTask("RahaiBlocksTask", MainForm.WorkMode.Rahai),
            ReferenceTasks[1], ReferenceTasks[2], ReferenceTasks[3], ReferenceTasks[4], ReferenceTasks[5],
            new ReferenceTask("CultivationTask", MainForm.WorkMode.Cultivation),
            ReferenceTasks[6], ReferenceTasks[7], ReferenceTasks[8], ReferenceTasks[9], ReferenceTasks[10], ReferenceTasks[11]
        };

        internal static int ReferenceTaskNumber(MainForm.WorkMode mode, DomainKind? domain)
        {
            for (int i = 0; i < ReferenceTasks.Length; i++)
                if (ReferenceTasks[i].Mode == mode && ReferenceTasks[i].Domain == domain)
                    return i + 1;
            return 0;
        }

        internal static string ReferenceTaskName(MainForm.WorkMode mode, DomainKind? domain)
        {
            foreach (ReferenceTask candidate in ReferenceTasks3814)
                if (candidate.Mode == mode && candidate.Domain == domain)
                    return candidate.Name;
            return null;
        }

        private static ReferenceTask FindReferenceTask(string task, bool newerNumbers)
        {
            int number;
            ReferenceTask[] numbers = newerNumbers ? ReferenceTasks3814 : ReferenceTasks;
            if (int.TryParse(task, out number))
                return number > 0 && number <= numbers.Length ? numbers[number - 1] : null;
            foreach (ReferenceTask candidate in ReferenceTasks3814)
                if (string.Equals(task, candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(task, "src.task." + candidate.Name + "." + candidate.Name, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            if (task == "一条龙" || string.Equals(task, "📅 Daily Task", StringComparison.OrdinalIgnoreCase))
                return ReferenceTasks[0];
            return null;
        }

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

        internal static bool TryReadTaskStart(string[] arguments, out MainForm.WorkMode mode,
            out DomainKind? domain, out bool exitAfter, out string error)
        {
            mode = MainForm.WorkMode.Daily;
            domain = null;
            exitAfter = false;
            error = null;
            bool hasTask = arguments.Length > 0 && arguments[0] == ResumeDaily;
            bool resumeCommand = arguments.Length > 0 && arguments[0] == ResumeCommandTask;
            bool hasVersion = false, newerNumbers = false;
            string selectedTask = null;
            for (int index = hasTask || resumeCommand ? 1 : 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                if (argument == "-e" || argument == "--exit")
                {
                    exitAfter = true;
                    continue;
                }

                if (argument == "--task-version" || argument.StartsWith("--task-version=", StringComparison.Ordinal))
                {
                    string version;
                    if (argument == "--task-version")
                    {
                        if (++index == arguments.Length)
                        {
                            error = "请在 --task-version 后指定 3.8.4 或 3.8.14。";
                            return false;
                        }
                        version = arguments[index];
                    }
                    else version = argument.Substring("--task-version=".Length);
                    if (hasVersion || (version != "3.8.4" && version != "3.8.14"))
                    {
                        error = "任务编号版本无效或重复，请仅指定一次 3.8.4 或 3.8.14。";
                        return false;
                    }
                    hasVersion = true;
                    newerNumbers = version == "3.8.14";
                    continue;
                }

                string task;
                if (argument == "-t" || argument == "--task")
                {
                    if (++index == arguments.Length || arguments[index].StartsWith("-", StringComparison.Ordinal))
                    {
                        error = "请在 -t 或 --task 后指定任务编号或注册名称。";
                        return false;
                    }
                    task = arguments[index];
                }
                else if (argument.StartsWith("--task=", StringComparison.Ordinal))
                    task = argument.Substring("--task=".Length);
                else
                {
                    error = "无法识别命令行参数 " + argument + "。";
                    return false;
                }

                if (hasTask)
                {
                    error = "每次启动请只指定一个任务。";
                    return false;
                }
                selectedTask = task;
                hasTask = true;
            }

            if (selectedTask != null)
            {
                ReferenceTask reference = FindReferenceTask(selectedTask, newerNumbers);
                if (reference == null)
                {
                    error = "命令行任务编号或注册名称无效，收到 " + selectedTask + "。";
                    return false;
                }
                mode = reference.Mode;
                domain = reference.Domain;
            }

            if (!hasTask && arguments.Length != 0)
                error = "请同时指定 -t 任务编号或注册名称。";
            return hasTask;
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
            MainForm.WorkMode resumeMode;
            bool resume = TryReadResume(arguments, out resumeMode);
            bool runTask = false;
            MainForm.WorkMode taskMode = MainForm.WorkMode.Daily;
            DomainKind? taskDomain = null;
            bool exitAfter = false;
            if (!resume)
            {
                string error;
                runTask = TryReadTaskStart(arguments, out taskMode, out taskDomain, out exitAfter, out error);
                if (error != null)
                {
                    RunLog.Warn("程序", error);
                    Console.Error.WriteLine(error);
                    Environment.ExitCode = 2;
                    return;
                }
                resume = runTask && (arguments[0] == ResumeDaily || arguments[0] == ResumeCommandTask);
                if (resume) resumeMode = taskMode;
            }

            Native.EnablePhysicalCoordinates();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (resume)
                RunLog.Info("程序", "准备在提权后恢复任务 " + resumeMode);
            Mutex instance;
            try { instance = new Mutex(false, @"Local\TideCombatDesktop"); }
            catch (UnauthorizedAccessException)
            {
                ShowAlreadyRunning(!runTask);
                return;
            }
            using (instance)
            {
                bool acquired;
                try { acquired = instance.WaitOne(resume ? 12000 : 0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    ShowAlreadyRunning(!runTask);
                    return;
                }
                try
                {
                    using (var form = new MainForm())
                    {
                        if (runTask) form.Shown += delegate { form.StartTaskFromCommandLine(taskMode, taskDomain, exitAfter); };
                        else if (resume) form.Shown += delegate { form.ResumeAfterElevation(resumeMode); };
                        else form.Shown += delegate { form.EnableStartupAssist(); };
                        Application.Run(form);
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
        }

        private static void ShowAlreadyRunning(bool interactive)
        {
            const string message = "自动战斗工具已经打开。请退出旧窗口后再打开此版本。";
            RunLog.Warn("程序", "已有程序实例运行，本次启动结束");
            if (interactive)
                MessageBox.Show(message, "鸣潮自动战斗", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
            {
                Console.Error.WriteLine(message);
                Environment.ExitCode = 1;
            }
        }
    }
}
