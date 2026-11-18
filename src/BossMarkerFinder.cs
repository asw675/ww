using System;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    // Fallback map marker locator when the checked boss icon is absent: the reference
    // retries the unchecked marker rotated by 0/90/180 degrees.
    internal sealed class BossMarkerFinder
    {
        private const int Side = 17, Rotations = 3;

        private readonly byte[][] templates = new byte[Rotations][];
        private readonly int[] patch = new int[Side * Side];

        internal static string DataPath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                {
                    Path.Combine(baseDir, "assets", "map", "boss_marker.bin"),
                    Path.Combine("assets", "map", "boss_marker.bin"),
                    Path.Combine(baseDir, "..", "..", "assets", "map", "boss_marker.bin")
                };
                foreach (string candidate in candidates)
                    if (File.Exists(candidate))
                        return candidate;
                return candidates[0];
            }
        }

        internal BossMarkerFinder(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < Rotations * Side * Side)
                throw new InvalidDataException("地图标记模板不完整。");
            for (int index = 0; index < Rotations; index++)
            {
                templates[index] = new byte[Side * Side];
                Buffer.BlockCopy(data, index * Side * Side, templates[index], 0, Side * Side);
            }
        }

        internal bool TryFind(PixelFrame frame, out Rectangle found)
        {
            found = Rectangle.Empty;
            var map = new Rectangle(Math.Max(0, (frame.Width - 640) / 2), 270, 640, 360);
            Rectangle limit = Rectangle.Intersect(map, new Rectangle(0, 0, frame.Width, frame.Height));
            if (limit.Width <= Side || limit.Height <= Side)
                return false;
            double best = double.MinValue;
            int bestX = 0, bestY = 0;
            for (int y = limit.Top; y <= limit.Bottom - Side; y += 6)
            {
                for (int x = limit.Left; x <= limit.Right - Side; x += 6)
                {
                    FillPatch(frame, x, y);
                    for (int index = 0; index < Rotations; index++)
                    {
                        double score = Similarity(patch, templates[index]);
                        if (score <= best) continue;
                        best = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
            if (best < .65)
                return false;
            found = new Rectangle(bestX, bestY, Side, Side);
            return true;
        }

        private void FillPatch(PixelFrame frame, int left, int top)
        {
            for (int y = 0; y < Side; y++)
                for (int x = 0; x < Side; x++)
                    patch[y * Side + x] = frame.Light(left + x, top + y);
        }

        private static double Similarity(int[] values, byte[] template)
        {
            double sumA = 0, sumB = 0;
            for (int index = 0; index < values.Length; index++)
            {
                sumA += values[index];
                sumB += template[index];
            }
            double meanA = sumA / values.Length, meanB = sumB / values.Length;
            double covariance = 0, varianceA = 0, varianceB = 0;
            for (int index = 0; index < values.Length; index++)
            {
                double a = values[index] - meanA;
                double b = template[index] - meanB;
                covariance += a * b;
                varianceA += a * a;
                varianceB += b * b;
            }
            double denominator = Math.Sqrt(varianceA * varianceB);
            return denominator <= 1e-6 ? 0 : covariance / denominator;
        }
    }
}
