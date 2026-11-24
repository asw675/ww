using System;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    // Fallback map marker locator when the checked boss icon is absent: the reference
    // retries the unchecked marker rotated by 0/90/180 degrees.
    internal sealed class BossMarkerFinder
    {
        private const int LegacySide = 17, Rotations = 3;

        private readonly byte[][] templates = new byte[Rotations][];
        private readonly Size[] sizes = new Size[Rotations];
        private readonly int[] templateSums = new int[Rotations];
        private readonly long[] templateVariance = new long[Rotations];

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
            bool rectangular = data.Length >= 4 && data[0] == 'W' && data[1] == 'W' && data[2] == 'M' && data[3] == '1';
            int offset = rectangular ? 4 : 0;
            for (int index = 0; index < Rotations; index++)
            {
                if (rectangular && data.Length - offset < 2)
                    throw new InvalidDataException("地图标记模板不完整。");
                int width = rectangular ? data[offset++] : LegacySide;
                int height = rectangular ? data[offset++] : LegacySide;
                int length = width * height;
                if (width <= 0 || height <= 0 || width > 64 || height > 64 || data.Length - offset < length)
                    throw new InvalidDataException("地图标记模板不完整。");
                sizes[index] = new Size(width, height);
                templates[index] = new byte[length];
                Buffer.BlockCopy(data, offset, templates[index], 0, length);
                offset += length;
                int sum = 0, squares = 0;
                foreach (int light in templates[index])
                {
                    sum += light;
                    squares += light * light;
                }
                templateSums[index] = sum;
                templateVariance[index] = (long)squares * length - (long)sum * sum;
            }
        }

        internal bool TryFind(PixelFrame frame, out Rectangle found)
        {
            found = Rectangle.Empty;
            var map = Rectangle.FromLTRB((int)(frame.Width * .30), (int)(frame.Height * .30),
                (int)(frame.Width * .70), (int)(frame.Height * .70));
            Rectangle limit = Rectangle.Intersect(map, new Rectangle(0, 0, frame.Width, frame.Height));
            double best = .70;
            for (int index = 0; index < Rotations; index++)
            {
                Size size = sizes[index];
                if (templateVariance[index] == 0)
                    continue;
                for (int y = limit.Top; y <= limit.Bottom - size.Height; y += 2)
                    for (int x = limit.Left; x <= limit.Right - size.Width; x += 2)
                    {
                        double score = Similarity(frame, x, y, index);
                        if (score <= best) continue;
                        best = score;
                        found = new Rectangle(x, y, size.Width, size.Height);
                    }
            }
            return !found.IsEmpty;
        }

        private double Similarity(PixelFrame frame, int left, int top, int index)
        {
            Size size = sizes[index];
            byte[] template = templates[index];
            int sum = 0, squares = 0, dot = 0, position = 0;
            for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                {
                    int light = frame.Light(left + x, top + y);
                    sum += light;
                    squares += light * light;
                    dot += light * template[position++];
                }
            long variance = (long)squares * template.Length - (long)sum * sum;
            if (variance == 0) return 0;
            long covariance = (long)dot * template.Length - (long)sum * templateSums[index];
            return covariance / Math.Sqrt((double)variance * templateVariance[index]);
        }
    }
}
