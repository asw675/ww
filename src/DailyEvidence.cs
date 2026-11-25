using System;
using System.Collections.Generic;
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
        private const int RetainedRuns = 10;
        private const int ExtraScreenshotsPerStep = 12;
        private const int RecentGardenScreenshotsPerStep = 4;
        private const int SampleWidth = 32, SampleHeight = 18;
        private static readonly object FolderGate = new object();
        private static readonly Regex StageName = new Regex(@"^[a-z0-9][a-z0-9_-]{0,47}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex FileName = new Regex(
            @"^[0-9]+-[A-Za-z0-9]+-[0-9]+-[a-z0-9][a-z0-9_-]{0,47}\.png(?:\.tmp)?$",
            RegexOptions.CultureInvariant);

        private readonly string logRoot, logArea, runName;
        private readonly Regex runNamePattern;
        private readonly Queue<string> recentGardenScreenshots = new Queue<string>();
        private bool folderUnavailable, gardenStep;
        private string step;
        private int sequence, extras;
        private byte[] beforeSample;
        private Size beforeSize;
        internal string DirectoryPath { get; private set; }

        internal DailyEvidence(string logRoot, bool standaloneGarden = false)
        {
            this.logRoot = logRoot;
            string prefix = standaloneGarden ? "garden-" : "daily-";
            logArea = standaloneGarden ? "周常乐园截图" : "一条龙截图";
            runName = prefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            runNamePattern = new Regex("^" + prefix + @"[0-9]{8}-[0-9]{9}-[0-9a-f]{12}$",
                RegexOptions.CultureInvariant);
        }

        internal string Begin(DailyTask task, int oneBasedIndex, PixelFrame frame)
        {
            StartStep(task, oneBasedIndex);
            return Save("before", frame);
        }

        internal void StartStep(DailyTask task, int oneBasedIndex)
        {
            step = oneBasedIndex.ToString("D2", CultureInfo.InvariantCulture) + "-" + task;
            gardenStep = task == DailyTask.Garden;
            extras = 0;
            recentGardenScreenshots.Clear();
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
                RunLog.Warn(logArea, step + " " + stage + "｜没有可用游戏画面");
                return null;
            }
            bool isBefore = stage == "before";
            bool isAfter = stage == "after" || stage.StartsWith("after-", StringComparison.Ordinal);
            bool isExtra = !isBefore && !isAfter;
            bool keepRecent = isExtra && extras >= ExtraScreenshotsPerStep;
            if (keepRecent && !gardenStep)
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
                if (keepRecent && recentGardenScreenshots.Count == RecentGardenScreenshotsPerStep)
                {
                    string oldest = recentGardenScreenshots.Peek();
                    TryDelete(oldest);
                    if (File.Exists(oldest))
                    {
                        RunLog.Warn(logArea, step + "｜未能轮换近期乐园截图");
                        return null;
                    }
                    recentGardenScreenshots.Dequeue();
                }
                Screenshots.WritePng(frame, area, temporary);
                File.Move(temporary, path);
                if (isBefore)
                {
                    beforeSample = Sample(frame);
                    beforeSize = area.Size;
                }
                if (keepRecent)
                    recentGardenScreenshots.Enqueue(path);
                else if (isExtra)
                    extras++;
                string comparison = isAfter ? CompareToBefore(frame, area.Size) : string.Empty;
                RunLog.Info(logArea, step + " " + stage + "｜" + path + comparison);
                return path;
            }
            catch (Exception error)
            {
                if (!IsEvidenceError(error)) throw;
                TryDelete(temporary);
                RunLog.Error(logArea, step + " " + stage + "｜保存截图失败", error);
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
                    RunLog.Error(logArea, "无法创建截图证据目录", error);
                    return false;
                }
            }
        }

        private void Prune(string root, string current)
        {
            try
            {
                var older = Directory.GetDirectories(root)
                    .Where(path => !string.Equals(path, current, StringComparison.OrdinalIgnoreCase) &&
                        runNamePattern.IsMatch(Path.GetFileName(path)))
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
                RunLog.Warn(logArea, "未能清理旧截图证据目录");
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
