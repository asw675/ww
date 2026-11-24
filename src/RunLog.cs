using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal static class RunLog
    {
        private const int MaxFileBytes = 5 * 1024 * 1024;
        private const int FileLimit = 10;
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex LogName = new Regex(
            @"^TideCombat-\d{8}-\d{9}-\d{6,10}-\d{5,10}\.log$", RegexOptions.CultureInvariant);

        private static StreamWriter writer;
        private static string sessionName;
        private static int part, bytes;
        internal static string DirectoryPath { get; private set; }
        internal static string Failure { get; private set; }

        internal static bool Start(string directory)
        {
            lock (Gate)
            {
                CloseFile();
                DirectoryPath = directory;
                Failure = null;
                using (Process process = Process.GetCurrentProcess())
                {
                    sessionName = string.Format(CultureInfo.InvariantCulture, "TideCombat-{0:yyyyMMdd-HHmmssfff}-{1:D6}",
                        DateTime.UtcNow, process.Id);
                }
                part = 0;
                try
                {
                    Directory.CreateDirectory(directory);
                    OpenFile();
                    return true;
                }
                catch (Exception error)
                {
                    if (!IsFileError(error)) throw;
                    Failure = error.Message;
                    CloseFile();
                    return false;
                }
            }
        }

        internal static void Stop()
        {
            lock (Gate) CloseFile();
        }

        internal static void Info(string area, string message) { Write("INFO", area, message, null); }
        internal static void Warn(string area, string message) { Write("WARN", area, message, null); }
        internal static void Error(string area, string message, Exception error) { Write("ERROR", area, message, error); }

        private static void Write(string level, string area, string message, Exception error)
        {
            lock (Gate)
            {
                if (writer == null)
                    return;
                string details = error == null ? message : message + " | " + error;
                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff zzz} [{1}] [{2}] {3}", DateTimeOffset.Now, level, area,
                    (details ?? string.Empty).Replace("\r\n", " | ").Replace('\r', ' ').Replace('\n', ' '));
                int size = Utf8.GetByteCount(line) + Environment.NewLine.Length;
                try
                {
                    if (bytes > 0 && (long)bytes + size > MaxFileBytes)
                    {
                        CloseFile();
                        OpenFile();
                    }
                    writer.WriteLine(line);
                    bytes += size;
                }
                catch (Exception failure)
                {
                    if (!IsFileError(failure)) throw;
                    Failure = failure.Message;
                    CloseFile();
                }
            }
        }

        private static void OpenFile()
        {
            string name = sessionName + "-" + (++part).ToString("D5", CultureInfo.InvariantCulture) + ".log";
            string file = Path.Combine(DirectoryPath, name);
            writer = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Utf8);
            writer.AutoFlush = true;
            bytes = 0;
            PruneFiles(file);
        }

        private static void PruneFiles(string current)
        {
            try
            {
                var olderLogs = Directory.GetFiles(DirectoryPath, "TideCombat-*.log")
                    .Where(file => file != current && LogName.IsMatch(Path.GetFileName(file)))
                    .OrderByDescending(file => file, StringComparer.Ordinal)
                    .Skip(FileLimit - 1);
                foreach (string file in olderLogs)
                {
                    try { File.Delete(file); }
                    catch (Exception error) { if (!IsFileError(error)) throw; }
                }
            }
            catch (Exception error) { if (!IsFileError(error)) throw; }
        }

        private static void CloseFile()
        {
            if (writer == null)
                return;
            try { writer.Dispose(); }
            catch (Exception error) { if (!IsFileError(error)) throw; }
            finally { writer = null; }
        }

        private static bool IsFileError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is SecurityException;
        }
    }
}
