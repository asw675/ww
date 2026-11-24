using System;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    internal static class DailyEvidenceChecks
    {
        internal static void SavesAndKeepsRecentRuns()
        {
            string root = Path.Combine(Path.GetTempPath(), "tc-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(root);
            try
            {
                var evidence = new DailyEvidence(root);
                evidence.Begin(DailyTask.BattlePass, 1, default(PixelFrame));
                Require(evidence.DirectoryPath == null, "unavailable game frame created a run directory");

                var pixels = new byte[4 * 3 * 4];
                var frame = new PixelFrame(pixels, 4, 3);
                SetColor(pixels, Color.Blue);
                string first = evidence.Begin(DailyTask.BattlePass, 1, frame);
                SetColor(pixels, Color.Red);
                string last = evidence.Save("after", frame);
                Require(Path.GetFileName(first).EndsWith("-before.png", StringComparison.Ordinal) &&
                    Path.GetFileName(last).EndsWith("-after.png", StringComparison.Ordinal), "start/end screenshots were not named");
                using (var before = new Bitmap(first))
                using (var after = new Bitmap(last))
                {
                    Require(before.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb() &&
                        after.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "screenshot did not preserve frame pixels");
                }
                string clean = evidence.Save("../private/name", frame);
                Require(Path.GetDirectoryName(clean) == evidence.DirectoryPath &&
                    Path.GetFileName(clean).EndsWith("-checkpoint.png", StringComparison.Ordinal), "stage name escaped the run directory");

                string listRoot = Path.GetDirectoryName(evidence.DirectoryPath);
                string unmanaged = Path.Combine(listRoot, "manual-review");
                Directory.CreateDirectory(unmanaged);
                File.WriteAllText(Path.Combine(unmanaged, "notes.txt"), "manual notes");
                for (int i = 0; i < 11; i++)
                    new DailyEvidence(root).Begin(DailyTask.Mail, 1, frame);
                Require(Directory.GetDirectories(listRoot, "daily-*").Length == 10 &&
                    File.Exists(Path.Combine(unmanaged, "notes.txt")), "run retention removed manual data or exceeded its limit");

                string badRoot = Path.Combine(root, "unwritable.txt");
                File.WriteAllText(badRoot, "regular file");
                var blocked = new DailyEvidence(badRoot);
                blocked.Begin(DailyTask.Mail, 2, frame);
                Require(blocked.Save("after", frame) == null, "I/O failure did not return an unavailable result");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void SetColor(byte[] pixels, Color color)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
