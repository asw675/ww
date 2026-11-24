using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal sealed class DailyEvidence
    {
        private const string LogArea = "一条龙截图";
        private const int RetainedRuns = 10;
        private const int ExtraScreenshotsPerStep = 12;
        private const int SampleWidth = 32, SampleHeight = 18;
        private static readonly object FolderGate = new object();
        private static readonly Regex RunName = new Regex(@"^daily-[0-9]{8}-[0-9]{9}-[0-9a-f]{12}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex StageName = new Regex(@"^[a-z0-9][a-z0-9_-]{0,47}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex FileName = new Regex(
            @"^[0-9]+-[A-Za-z0-9]+-[0-9]+-[a-z0-9][a-z0-9_-]{0,47}\.png(?:\.tmp)?$",
            RegexOptions.CultureInvariant);

        private readonly string logRoot;
        private readonly string runName = "daily-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff",
            CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        private bool folderUnavailable;
        private string step;
        private int sequence, extras;
        private byte[] beforeSample;
        private Size beforeSize;
        internal string DirectoryPath { get; private set; }

        internal DailyEvidence(string logRoot)
        {
            this.logRoot = logRoot;
        }

        internal string Begin(DailyTask task, int oneBasedIndex, PixelFrame frame)
        {
            StartStep(task, oneBasedIndex);
            return Save("before", frame);
        }

        internal void StartStep(DailyTask task, int oneBasedIndex)
        {
            step = oneBasedIndex.ToString("D2", CultureInfo.InvariantCulture) + "-" + task;
            extras = 0;
            beforeSample = null;
            beforeSize = Size.Empty;
        }

        internal string Save(string stage, PixelFrame frame)
        {
            if (step == null)
                return null;
            stage = (stage ?? string.Empty).ToLowerInvariant();
            if (!StageName.IsMatch(stage))
                stage = "checkpoint";
            if (frame.Width <= 0 || frame.Height <= 0)
            {
                RunLog.Warn(LogArea, step + " " + stage + "｜没有可用游戏画面");
                return null;
            }
            bool isBefore = stage == "before";
            bool isAfter = stage == "after" || stage.StartsWith("after-", StringComparison.Ordinal);
            if (!isBefore && !isAfter && extras >= ExtraScreenshotsPerStep)
                return null;
            if (!EnsureDirectory())
                return null;

            var area = new Rectangle(0, 0, frame.Width, frame.Height);
            string baseName = step + "-" + (++sequence).ToString("D3", CultureInfo.InvariantCulture) + "-";
            // Keep the fallback path usable by .NET Framework even from a deep portable installation.
            int stageLength = Math.Max(1, 248 - DirectoryPath.Length - baseName.Length - ".png.tmp".Length);
            string fileStage = stage.Substring(0, Math.Min(stage.Length, stageLength));
            string name = baseName + fileStage + ".png";
            string path = Path.Combine(DirectoryPath, name);
            string temporary = path + ".tmp";
            try
            {
                Screenshots.WritePng(frame, area, temporary);
                File.Move(temporary, path);
                if (isBefore)
                {
                    beforeSample = Sample(frame);
                    beforeSize = area.Size;
                }
                if (!isBefore && !isAfter)
                    extras++;
                string comparison = isAfter ? CompareToBefore(frame, area.Size) : string.Empty;
                RunLog.Info(LogArea, step + " " + stage + "｜" + path + comparison);
                return path;
            }
            catch (Exception error)
            {
                if (!IsEvidenceError(error)) throw;
                TryDelete(temporary);
                RunLog.Error(LogArea, step + " " + stage + "｜保存截图失败", error);
                return null;
            }
        }

        private string CompareToBefore(PixelFrame frame, Size size)
        {
            if (beforeSample == null)
                return "｜缺少项目起始画面";
            if (beforeSize != size)
                return "｜起止画面尺寸不同";
            byte[] after = Sample(frame);
            int total = 0;
            for (int i = 0; i < after.Length; i++)
                total += Math.Abs(beforeSample[i] - after[i]);
            double change = total / (after.Length * 255.0);
            return "｜起止画面抽样平均色差=" + change.ToString("P1", CultureInfo.InvariantCulture) + "（诊断）";
        }

        private static byte[] Sample(PixelFrame frame)
        {
            var sample = new byte[SampleWidth * SampleHeight * 3];
            int offset = 0;
            for (int row = 0; row < SampleHeight; row++)
            {
                int y = ((2 * row + 1) * frame.Height) / (2 * SampleHeight);
                for (int column = 0; column < SampleWidth; column++)
                {
                    int x = ((2 * column + 1) * frame.Width) / (2 * SampleWidth);
                    int red, green, blue;
                    frame.Rgb(x, y, out red, out green, out blue);
                    sample[offset++] = (byte)red;
                    sample[offset++] = (byte)green;
                    sample[offset++] = (byte)blue;
                }
            }
            return sample;
        }

        private bool EnsureDirectory()
        {
            if (DirectoryPath != null)
                return true;
            if (folderUnavailable || string.IsNullOrWhiteSpace(logRoot))
                return false;
            lock (FolderGate)
            {
                try
                {
                    string root = Path.GetFullPath(Path.Combine(logRoot, "evidence"));
                    var folder = Directory.CreateDirectory(root);
                    if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("截图目录是重解析点。");
                    var run = Directory.CreateDirectory(Path.Combine(root, runName));
                    if ((run.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("本次截图目录是重解析点。");
                    DirectoryPath = run.FullName;
                    Prune(root, DirectoryPath);
                    return true;
                }
                catch (Exception error)
                {
                    if (!IsEvidenceError(error)) throw;
                    folderUnavailable = true;
                    RunLog.Error(LogArea, "无法创建截图证据目录", error);
                    return false;
                }
            }
        }

        private static void Prune(string root, string current)
        {
            try
            {
                var older = Directory.GetDirectories(root)
                    .Where(path => !string.Equals(path, current, StringComparison.OrdinalIgnoreCase) &&
                        RunName.IsMatch(Path.GetFileName(path)))
                    .OrderByDescending(path => path, StringComparer.Ordinal)
                    .Skip(RetainedRuns - 1);
                foreach (string path in older)
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    foreach (string file in Directory.GetFiles(path))
                        if (FileName.IsMatch(Path.GetFileName(file)))
                            TryDelete(file);
                    if (!Directory.EnumerateFileSystemEntries(path).Any())
                        Directory.Delete(path);
                }
            }
            catch (Exception error)
            {
                if (!IsEvidenceError(error)) throw;
                RunLog.Warn(LogArea, "未能清理旧截图证据目录");
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception error) { if (!IsEvidenceError(error)) throw; }
        }

        private static bool IsEvidenceError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is SecurityException ||
                error is ArgumentException || error is NotSupportedException || error is ExternalException;
        }
    }
}
