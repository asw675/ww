using System;
using System.Drawing;

namespace TideCombat
{
    internal sealed class CamellyaColors
    {
        private readonly FramePatch ring = new FramePatch();
        private readonly FramePatch forte = new FramePatch();
        private bool[] filled;
        private double[,] cosines, sines;

        internal void Read(Bitmap source, Rectangle crop)
        {
            ring.Read(source, FramePatch.ScaleCentered(crop, 2560, 1440, 2110, 1236, 2217, 1343));
            forte.Read(source, FramePatch.ScaleCentered(crop, 3840, 2160, 1630, 2002, 2176, 2004));
        }

        internal bool EphemeralReady()
        {
            int inner = (int)(ring.Height * .395), outer = (int)Math.Ceiling(ring.Height * .496);
            int available = 0, red = 0;
            for (int y = 0; y < ring.Height; y++)
            for (int x = 0; x < ring.Width; x++)
            {
                int dx = x - ring.Width / 2, dy = y - ring.Height / 2;
                int distance = dx * dx + dy * dy;
                if (distance <= inner * inner || distance > outer * outer) continue;
                int r, g, b;
                ring.Rgb(x, y, out r, out g, out b);
                if (r != 0 && g != 0 && b != 0) available++;
                if (r >= 234 && r <= 245 && g >= 71 && g <= 82 && b >= 168 && b <= 179) red++;
            }
            return available > 0 && red * 10 > available;
        }

        internal double Charge(bool budding)
        {
            int width = forte.Width, height = forte.Height;
            if (filled == null || filled.Length != width * height) filled = new bool[width * height];
            int matchCount = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int r, g, b;
                forte.Rgb(x, y, out r, out g, out b);
                bool match = budding ? r >= 220 && g >= 161 && g <= 213 && b >= 168 && b <= 225 :
                    r >= 193 && g >= 46 && g <= 93 && b >= 127 && b <= 163;
                filled[y * width + x] = match;
                if (match) matchCount++;
            }
            if (matchCount == 0) return 0;
            if (width < 64) return Round(Math.Min(1, matchCount * 2.0 / filled.Length));

            RemoveShortStripes(width, height);
            int length = Math.Max(8, (int)(width * .045)), step = Math.Max(1, (int)(width * .012));
            int windows = (width - length) / step + 1;
            var scores = new double[windows];
            var fractions = new double[windows];
            var profile = new int[length];
            PrepareFrequencies(length);
            double max = 0;
            for (int i = 0; i < windows; i++)
            {
                int lit = 0;
                for (int x = 0; x < length; x++)
                {
                    profile[x] = 0;
                    for (int y = 0; y < height; y++) if (filled[y * width + i * step + x]) profile[x]++;
                    lit += profile[x];
                }
                fractions[i] = (double)lit / (length * height);
                if (fractions[i] < .05 || fractions[i] > .75) continue;
                scores[i] = FrequencyScore(profile);
                max = Math.Max(max, scores[i]);
            }
            if (max < .001) return 0;

            double threshold = Math.Max(1.5, max * .45);
            int lastWindow = 0;
            for (int i = 0; i < windows; i++)
            {
                if (scores[i] >= threshold) lastWindow = i;
                else if (fractions[i] < .375)
                {
                    for (int later = i + 1; later < windows; later++)
                        if (scores[later] > threshold) return -1;
                    break;
                }
            }
            if (lastWindow == 0) return 0;
            int edge = lastWindow * step;
            return edge + length + step >= width ? 1 : Round((double)edge / width);
        }

        private void RemoveShortStripes(int width, int height)
        {
            int minimum = (int)(width * .009);
            if (minimum <= 1) return;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width;)
                {
                    if (!filled[row + x]) { x++; continue; }
                    int start = x;
                    while (x < width && filled[row + x]) x++;
                    if (x - start < minimum) Array.Clear(filled, row + start, x - start);
                }
            }
        }

        private void PrepareFrequencies(int length)
        {
            if (cosines != null && cosines.GetLength(1) == length) return;
            cosines = new double[length / 2, length];
            sines = new double[length / 2, length];
            for (int frequency = 1; frequency < length / 2; frequency++)
            for (int x = 0; x < length; x++)
            {
                double angle = 2 * Math.PI * frequency * x / length;
                cosines[frequency, x] = Math.Cos(angle);
                sines[frequency, x] = Math.Sin(angle);
            }
        }

        private double FrequencyScore(int[] profile)
        {
            double strongest = 0;
            for (int frequency = 1; frequency < profile.Length / 2; frequency++)
            {
                double real = 0, imaginary = 0;
                for (int x = 0; x < profile.Length; x++)
                {
                    real += cosines[frequency, x] * profile[x];
                    imaginary += sines[frequency, x] * profile[x];
                }
                strongest = Math.Max(strongest, real * real + imaginary * imaginary);
            }
            return Math.Sqrt(strongest);
        }

        private static double Round(double value) { return Math.Round(value, 3, MidpointRounding.AwayFromZero); }

    }
}
