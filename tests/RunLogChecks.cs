using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TideCombat
{
    internal static class RunLogChecks
    {
        internal static void WritesAndRotates()
        {
            string directory = Path.Combine(Path.GetTempPath(), "TideCombat-logs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                for (int index = 1; index <= 12; index++)
                    File.WriteAllText(Path.Combine(directory, "TideCombat-20000101-000000000-000001-" +
                        index.ToString("D5") + ".log"), "old");
                string note = Path.Combine(directory, "TideCombat-notes.log");
                File.WriteAllText(note, "keep");

                Require(RunLog.Start(directory), "logger could not start in a writable directory");
                RunLog.Warn("巢穴", "等待回执\r\n超时");
                RunLog.Error("任务", "执行失败", new InvalidOperationException("test error"));
                Parallel.For(0, 30, index => RunLog.Info("输入", "操作 " + index));
                string firstFile = Directory.GetFiles(directory, "TideCombat-*.log")
                    .Single(path => new FileInfo(path).Length > 10);
                using (var reader = new StreamReader(new FileStream(firstFile, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8))
                {
                    string[] lines = reader.ReadToEnd().Split(new[] { Environment.NewLine },
                        StringSplitOptions.RemoveEmptyEntries);
                    Require(lines.Length == 32 && lines.Count(line => line.Contains("[INFO] [输入]")) == 30,
                        "entries were missing, split or not flushed");
                    Require(lines[0].Contains("[WARN] [巢穴] 等待回执 | 超时") &&
                        lines[1].Contains("[ERROR] [任务] 执行失败 | System.InvalidOperationException: test error"),
                        "severity, text or exception details were lost");
                }

                string payload = new string('x', 8192);
                for (int index = 0; index < 650; index++)
                    RunLog.Info("测试", payload);
                RunLog.Stop();
                var logs = Directory.GetFiles(directory, "TideCombat-*.log").Select(path => new FileInfo(path)).ToArray();
                Require(logs.Length == 11 && File.Exists(note), "retention changed or removed a foreign file");
                Require(logs.Count(file => file.Length > 10) == 2 && logs.All(file => file.Length <= 5 * 1024 * 1024),
                    "log did not rotate at 5 MiB");
                Require(!RunLog.Start(note) && !string.IsNullOrEmpty(RunLog.Failure),
                    "an unavailable log directory must report failure without throwing");
                RunLog.Info("测试", "logging failure must not stop the task");
            }
            finally
            {
                RunLog.Stop();
                Directory.Delete(directory, true);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
