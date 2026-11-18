using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TideCombat
{
    internal sealed class AreaPixels
    {
        private readonly int stride;
        private readonly Rectangle bounds;
        private readonly int[][] integral;

        internal AreaPixels(PixelFrame frame, Rectangle rect)
        {
            bounds = Rectangle.Intersect(rect, new Rectangle(0, 0, frame.Width, frame.Height));
            stride = bounds.Width + 1;
            int length = stride * (bounds.Height + 1);
            integral = new[] { new int[length], new int[length], new int[length] };
            for (int y = 1; y <= bounds.Height; y++)
            {
                int luminance = 0, blueRed = 0, greenMagenta = 0;
                for (int x = 1; x <= bounds.Width; x++)
                {
                    int r, g, b;
                    frame.Rgb(bounds.X + x - 1, bounds.Y + y - 1, out r, out g, out b);
                    luminance += (r * 3 + g * 6 + b) / 10;
                    blueRed += b - r;
                    greenMagenta += g - (r + b) / 2;
                    int index = y * stride + x;
                    integral[0][index] = luminance + integral[0][index - stride];
                    integral[1][index] = blueRed + integral[1][index - stride];
                    integral[2][index] = greenMagenta + integral[2][index - stride];
                }
            }
        }

        internal bool Contains(Rectangle rect) { return bounds.Contains(rect); }

        internal void Average(Rectangle rect, int[] values, int index, int planeSize)
        {
            int left = rect.Left - bounds.Left, top = rect.Top - bounds.Top;
            int a = top * stride + left, b = a + rect.Width;
            int c = a + rect.Height * stride, d = c + rect.Width;
            int area = rect.Width * rect.Height;
            for (int channel = 0; channel < 3; channel++)
            {
                int[] field = integral[channel];
                values[index + channel * planeSize] = (field[d] + field[a] - field[b] - field[c]) / area;
            }
        }
    }

    internal sealed class VisualSignature
    {
        private const int Side = 7;
        private const int PlaneSize = Side * Side;
        private const int Count = 4;
        private static readonly int[] Comparisons = MakeComparisons();
        internal readonly ulong[] Bits = new ulong[Count];
        internal readonly ulong[] Mask = new ulong[Count];

        private static int[] MakeComparisons()
        {
            var comparisons = new int[Count * 64 * 2];
            uint seed = 190523u;
            for (int bit = 0; bit < Count * 64; bit++)
            {
                int channel = bit < 144 ? 0 : bit < 200 ? 1 : 2;
                seed = unchecked(seed * 1664525u + 1013904223u);
                int first = (int)((seed >> 10) % PlaneSize);
                seed = unchecked(seed * 1664525u + 1013904223u);
                int second = (int)((seed >> 10) % (PlaneSize - 1));
                if (second >= first) second++;
                comparisons[2 * bit] = channel * PlaneSize + first;
                comparisons[2 * bit + 1] = channel * PlaneSize + second;
            }
            return comparisons;
        }

        internal static void Sample(AreaPixels image, Rectangle area, int[] values)
        {
            for (int gy = 0; gy < Side; gy++)
            {
                for (int gx = 0; gx < Side; gx++)
                {
                    int left = area.Left + gx * area.Width / Side;
                    int top = area.Top + gy * area.Height / Side;
                    int right = area.Left + (gx + 1) * area.Width / Side;
                    int bottom = area.Top + (gy + 1) * area.Height / Side;
                    image.Average(new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)),
                        values, gy * Side + gx, PlaneSize);
                }
            }
        }

        internal static int[] Buffer() { return new int[PlaneSize * 3]; }

        internal static VisualSignature FromSample(int[] values)
        {
            var signature = new VisualSignature();
            for (int bit = 0; bit < Count * 64; bit++)
            {
                int delta = values[Comparisons[bit * 2]] - values[Comparisons[bit * 2 + 1]];
                ulong mask = 1ul << (bit % 64);
                if (Math.Abs(delta) >= (bit < 144 ? 13 : 10))
                    signature.Mask[bit / 64] |= mask;
                if (delta > 0)
                    signature.Bits[bit / 64] |= mask;
            }
            return signature;
        }

        internal double Similarity(int[] values)
        {
            int total = 0, correct = 0;
            for (int word = 0; word < Count; word++)
            {
                if (Mask[word] == 0) continue;
                int start = word * 64;
                ulong enabled = Mask[word];
                for (int bit = 0; enabled != 0; bit++, enabled >>= 1)
                {
                    if ((enabled & 1) == 0) continue;
                    int index = (start + bit) * 2;
                    int delta = values[Comparisons[index]] - values[Comparisons[index + 1]];
                    if (delta != 0 && ((Bits[word] >> bit & 1) != 0) == (delta > 0))
                        correct++;
                    total++;
                }
            }
            return total >= 27 ? (double)correct / total : 0;
        }

        internal string Encode()
        {
            var bytes = new byte[Count * 16];
            BufferCopy(Bits, bytes, 0);
            BufferCopy(Mask, bytes, Count * 8);
            return Convert.ToBase64String(bytes);
        }

        private static void BufferCopy(ulong[] source, byte[] destination, int offset)
        {
            System.Buffer.BlockCopy(source, 0, destination, offset, source.Length * 8);
        }

        internal static VisualSignature Parse(string value)
        {
            var signature = new VisualSignature();
            byte[] bytes = Convert.FromBase64String(value);
            if (bytes.Length != Count * 16) throw new InvalidDataException("Invalid visual signature.");
            System.Buffer.BlockCopy(bytes, 0, signature.Bits, 0, Count * 8);
            System.Buffer.BlockCopy(bytes, Count * 8, signature.Mask, 0, Count * 8);
            return signature;
        }
    }

    internal sealed class VisualPattern
    {
        internal readonly string Label, Fighter;
        internal readonly Rectangle Area;
        internal readonly VisualSignature Signature;
        private readonly byte[] light;
        private readonly int lightSum, lightSquares, lightHigh, lightLow, edgeFloor;

        internal VisualPattern(string label, string fighter, Rectangle area, VisualSignature signature, byte[] light)
        {
            Label = label;
            Fighter = fighter;
            Area = area;
            Signature = signature;
            this.light = light;
            if (light == null) return;
            for (int index = 0; index < light.Length; index++)
            {
                int value = unchecked((sbyte)light[index]);
                lightSum += value;
                lightSquares += value * value;
                if (value > unchecked((sbyte)light[lightHigh])) lightHigh = index;
                if (value < unchecked((sbyte)light[lightLow])) lightLow = index;
            }
            edgeFloor = unchecked((sbyte)light[lightHigh]) - unchecked((sbyte)light[lightLow]);
        }

        internal bool HasLight { get { return light != null; } }

        internal bool HasLightEdge(PixelFrame frame, Rectangle placement)
        {
            return light != null && frame.Light(placement.X + lightHigh % placement.Width, placement.Y + lightHigh / placement.Width) -
                frame.Light(placement.X + lightLow % placement.Width, placement.Y + lightLow / placement.Width) >= edgeFloor;
        }

        internal bool MatchesLight(PixelFrame frame, Rectangle placement)
        {
            if (light == null) return true;
            return LightSimilarity(frame, placement) >= .63;
        }

        internal double LightSimilarity(PixelFrame frame, Rectangle placement)
        {
            if (light == null) return 0;
            int y = 0, yy = 0, xy = 0;
            for (int i = 0; i < light.Length; i++)
            {
                int a = unchecked((sbyte)light[i]);
                int b = frame.Light(placement.Left + i % placement.Width, placement.Top + i / placement.Width);
                y += b;
                yy += b * b;
                xy += a * b;
            }
            double count = light.Length;
            double variation = (count * lightSquares - (double)lightSum * lightSum) * (count * yy - (double)y * y);
            return variation > 0 ? (count * xy - (double)lightSum * y) / Math.Sqrt(variation) : 0;
        }

        internal Rectangle Map(int width, int slot)
        {
            Rectangle region = Area;
            if (Area.Left > 1120 && Label != "revive_confirm_hcenter_vcenter" ||
                Label == "box_target_enemy_long" || Label == "target_box_long2")
                region.X += width - 1600;
            else if (Area.Left >= 80)
                region.X += (width - 1600) / 2;
            if (Fighter != null)
                region.Y += 110 * slot;
            return region;
        }
    }

    internal static class PatternBank
    {
        private const string Resource = "TideCombat.Signatures.tsv";
        internal static readonly VisualPattern[] All = Load();
        internal static readonly VisualPattern[] Portraits = All.Where(pattern => pattern.Fighter != null).ToArray();
        private static readonly Dictionary<string, VisualPattern[]> Signals = All.Where(pattern => pattern.Fighter == null)
            .GroupBy(pattern => pattern.Label).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        private static VisualPattern[] Load()
        {
            var result = new List<VisualPattern>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null) return result.ToArray();
                using (var reader = new StreamReader(stream))
                {
                    string row;
                    while ((row = reader.ReadLine()) != null)
                    {
                        if (row.Length == 0 || row[0] == '#') continue;
                        var cells = row.Split('\t');
                        if (cells.Length != 7 && cells.Length != 8) throw new InvalidDataException("Invalid visual signature row.");
                        int[] coordinates = cells.Skip(2).Take(4).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                        result.Add(new VisualPattern(cells[0], cells[1] == "-" ? null : cells[1],
                            new Rectangle(coordinates[0], coordinates[1], coordinates[2], coordinates[3]),
                            VisualSignature.Parse(cells[6]), cells.Length == 8 ? Convert.FromBase64String(cells[7]) : null));
                    }
                }
            }
            return result.ToArray();
        }

        internal static VisualPattern[] For(string label)
        {
            VisualPattern[] found;
            return Signals.TryGetValue(label, out found) ? found : new VisualPattern[0];
        }
    }
}
