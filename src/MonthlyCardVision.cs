using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    // The card is centered on both axes. Match its original RGB crop before HUD normalization.
    internal sealed class MonthlyCardVision
    {
        private readonly ScreenTemplate template = new ScreenTemplate("monthly_card", Placement, .05, .05, .65);

        private static Rectangle Placement(Size screen)
        {
            return ScreenTemplate.Place(screen, new Size(1920, 1080), new Rectangle(841, 582, 260, 88), true, true);
        }

        internal static Rectangle SearchArea(Size screen) { return ScreenTemplate.SearchArea(screen, Placement(screen), .05, .05); }
        internal bool Find(PixelFrame frame, out Rectangle found) { return template.Find(frame, out found, frame.MonthlyCard); }
    }

    internal sealed class ScreenTemplate
    {
        private readonly Lazy<PixelFrame> original;
        private readonly Func<Size, Rectangle> placement;
        private readonly double horizontalVariance, verticalVariance, threshold;
        private PixelFrame pattern;
        private Moments templateTotal;
        private double templateEnergy;
        private Band[] bands;
        private readonly Integral image = new Integral();
        private long lastCapture = -1;
        private Rectangle lastResult;

        private struct Moments
        {
            internal int Red, Green, Blue;
            internal long Squares;
            internal double Dot(Moments other)
            {
                return (double)Red * other.Red + (double)Green * other.Green + (double)Blue * other.Blue;
            }
        }

        private sealed class Integral
        {
            private Moments[] values;
            private int stride;

            internal void Load(PixelFrame frame)
            {
                stride = frame.Width + 1;
                int length = stride * (frame.Height + 1);
                if (values == null || values.Length != length) values = new Moments[length];
                else Array.Clear(values, 0, stride);
                for (int y = 0; y < frame.Height; y++)
                {
                    int i = (y + 1) * stride;
                    values[i] = new Moments();
                    var row = new Moments();
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int r, g, b;
                        frame.Rgb(x, y, out r, out g, out b);
                        row.Red += r; row.Green += g; row.Blue += b;
                        row.Squares += r * r + g * g + b * b;
                        i++;
                        Moments above = values[i - stride];
                        values[i].Red = above.Red + row.Red;
                        values[i].Green = above.Green + row.Green;
                        values[i].Blue = above.Blue + row.Blue;
                        values[i].Squares = above.Squares + row.Squares;
                    }
                }
            }

            internal Moments Sum(Rectangle area, int dx = 0, int dy = 0)
            {
                int a = (area.Top + dy) * stride + area.Left + dx, b = a + area.Width;
                int c = a + area.Height * stride, d = c + area.Width;
                return new Moments {
                    Red = values[d].Red - values[b].Red - values[c].Red + values[a].Red,
                    Green = values[d].Green - values[b].Green - values[c].Green + values[a].Green,
                    Blue = values[d].Blue - values[b].Blue - values[c].Blue + values[a].Blue,
                    Squares = values[d].Squares - values[b].Squares - values[c].Squares + values[a].Squares };
            }
        }

        private struct Band
        {
            internal Rectangle Area, Half;
            internal double Fraction, Normalizer, R, G, B, Energy, Remaining;
        }

        internal ScreenTemplate(string label, Func<Size, Rectangle> placement, double horizontalVariance,
            double verticalVariance, double threshold)
        {
            original = new Lazy<PixelFrame>(() => LoadOriginal(label));
            this.placement = placement;
            this.horizontalVariance = horizontalVariance;
            this.verticalVariance = verticalVariance;
            this.threshold = threshold;
        }

        private static PixelFrame LoadOriginal(string label)
        {
            var entry = PatternBank.For(label)[0];
            using (var stream = new MemoryStream(entry.SourcePng, false))
            using (var bitmap = new Bitmap(stream))
            {
                var patch = new FramePatch();
                patch.Read(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                return patch.Pixels;
            }
        }

        internal static Rectangle Place(Size screen, Size design, Rectangle box, bool horizontalCenter, bool verticalCenter)
        {
            double scale = Math.Min(screen.Width / (double)design.Width, screen.Height / (double)design.Height);
            return new Rectangle(Anchor(box.X, design.Width, screen.Width, scale, horizontalCenter),
                Anchor(box.Y, design.Height, screen.Height, scale, verticalCenter),
                Math.Max(1, (int)Math.Round(box.Width * scale)), Math.Max(1, (int)Math.Round(box.Height * scale)));
        }

        private static int Anchor(int position, int design, int actual, double scale, bool centered)
        {
            if (centered) return (int)Math.Round(actual * .5 + (position - design * .5) * scale);
            return position > design * .5 ? actual - (int)Math.Round((design - position) * scale) : (int)Math.Round(position * scale);
        }

        internal static Rectangle SearchArea(Size screen, Rectangle box, double horizontal, double vertical)
        {
            return Rectangle.Intersect(new Rectangle(Point.Empty, screen), Rectangle.FromLTRB(
                (int)Math.Round(box.Left - screen.Width * horizontal), (int)Math.Round(box.Top - screen.Height * vertical),
                (int)Math.Round(box.Right + screen.Width * horizontal), (int)Math.Round(box.Bottom + screen.Height * vertical)));
        }

        internal bool Find(PixelFrame frame, out Rectangle found, FramePatch retained = null)
        {
            if (lastCapture == frame.CaptureId) { found = lastResult; return !found.IsEmpty; }
            found = Rectangle.Empty;
            if (frame.Width <= 0 || frame.Height <= 0) return false;
            PixelFrame source = retained == null ? frame.FullResolution : frame;
            var size = retained != null ? new Size(frame.SourceWidth, frame.SourceHeight) : new Size(source.Width, source.Height);
            Rectangle expected = placement(size), area = SearchArea(size, expected, horizontalVariance, verticalVariance);
            if (area.Width < expected.Width || area.Height < expected.Height) return false;
            EnsurePattern(expected.Size);
            PixelFrame patch;
            if (retained != null) patch = retained.Pixels;
            else
            {
                var data = new byte[area.Width * area.Height * 4];
                source.CopyBgra(area, data);
                patch = new PixelFrame(data, area.Width, area.Height);
            }
            Rectangle hit = Search(patch, expected.X - area.X, expected.Y - area.Y);
            if (!hit.IsEmpty)
            {
                hit.Offset(area.Location);
                found = Rectangle.FromLTRB((int)Math.Round(hit.Left * (double)frame.Width / size.Width),
                    (int)Math.Round(hit.Top * (double)frame.Height / size.Height),
                    (int)Math.Round(hit.Right * (double)frame.Width / size.Width),
                    (int)Math.Round(hit.Bottom * (double)frame.Height / size.Height));
            }
            lastCapture = frame.CaptureId;
            lastResult = found;
            return !found.IsEmpty;
        }

        private void EnsurePattern(Size size)
        {
            if (pattern.Width == size.Width && pattern.Height == size.Height) return;
            PixelFrame source = original.Value;
            if (size.Width == source.Width && size.Height == source.Height) pattern = source;
            else
            {
                var data = new byte[size.Width * size.Height * 4];
                var columns = new LinearAxis[size.Width];
                for (int x = 0; x < columns.Length; x++) columns[x] = new LinearAxis(x, source.Width, size.Width, true);
                for (int y = 0; y < size.Height; y++)
                {
                    var row = new LinearAxis(y, source.Height, size.Height, false);
                    for (int x = 0; x < size.Width; x++)
                    {
                        int r, g, b, offset = (y * size.Width + x) * 4;
                        LinearAxis.Sample(source, columns[x], row, out r, out g, out b);
                        data[offset] = (byte)b; data[offset + 1] = (byte)g; data[offset + 2] = (byte)r;
                    }
                }
                pattern = new PixelFrame(data, size.Width, size.Height);
            }
            var totals = new Integral();
            totals.Load(pattern);
            var all = new List<Band>();
            var rect = new Rectangle(Point.Empty, size);
            templateTotal = totals.Sum(rect);
            templateEnergy = templateTotal.Squares - templateTotal.Dot(templateTotal) / (rect.Width * rect.Height);
            Split(totals, rect, all);
            all.Sort((a, b) => b.Energy.CompareTo(a.Energy));
            bands = all.GetRange(0, Math.Min(256, all.Count)).ToArray();
            double remaining = templateEnergy;
            for (int i = 0; i < bands.Length; i++)
                bands[i].Remaining = Math.Max(0, remaining -= bands[i].Energy);
        }

        // Differences of means over disjoint halves form orthogonal components of a centered image.
        private static void Split(Integral totals, Rectangle area, List<Band> all)
        {
            int count = area.Width * area.Height;
            if (count < 8) return;
            Rectangle first = area, second = area;
            if (area.Width >= area.Height)
            {
                first.Width /= 2;
                second.X += first.Width; second.Width -= first.Width;
            }
            else
            {
                first.Height /= 2;
                second.Y += first.Height; second.Height -= first.Height;
            }
            int n = first.Width * first.Height;
            double f = (double)n / count, norm = Math.Sqrt((double)count / (n * (double)(count - n)));
            Moments parent = totals.Sum(area), child = totals.Sum(first);
            double r = (child.Red - f * parent.Red) * norm, g = (child.Green - f * parent.Green) * norm,
                b = (child.Blue - f * parent.Blue) * norm;
            all.Add(new Band { Area = area, Half = first, Fraction = f, Normalizer = norm, R = r, G = g, B = b,
                Energy = r * r + g * g + b * b });
            Split(totals, first, all);
            Split(totals, second, all);
        }

        private Rectangle Search(PixelFrame frame, int expectedX, int expectedY)
        {
            image.Load(frame);
            var box = new Rectangle(0, 0, pattern.Width, pattern.Height);
            double best = threshold;
            Rectangle result = Rectangle.Empty;
            if (expectedX >= 0 && expectedY >= 0 && expectedX + box.Width <= frame.Width && expectedY + box.Height <= frame.Height)
            {
                double center = Score(frame, box, expectedX, expectedY, best);
                if (center >= best)
                {
                    result = new Rectangle(expectedX, expectedY, box.Width, box.Height);
                    best = center;
                    if (center >= 1 - 1e-12) return result;
                }
            }
            for (int y = 0; y <= frame.Height - box.Height; y++)
                for (int x = 0; x <= frame.Width - box.Width; x++)
                {
                    double score = Score(frame, box, x, y, best);
                    if (score < best || score == best && !result.IsEmpty &&
                        (y > result.Y || y == result.Y && x >= result.X)) continue;
                    best = score;
                    result = new Rectangle(x, y, box.Width, box.Height);
                }
            return result;
        }

        private double Score(PixelFrame frame, Rectangle box, int x, int y, double minimum)
        {
            int count = box.Width * box.Height;
            Moments total = image.Sum(box, x, y);
            double energy = total.Squares - total.Dot(total) / count;
            if (energy <= 0 || templateEnergy <= 0) return -1;
            double norm = Math.Sqrt(templateEnergy * energy), needed = minimum * norm;
            double partial = 0, remaining = energy;
            for (int i = 0; i < bands.Length; i++)
            {
                Band band = bands[i];
                Moments parent = image.Sum(band.Area, x, y), child = image.Sum(band.Half, x, y);
                double r = (child.Red - band.Fraction * parent.Red) * band.Normalizer;
                double g = (child.Green - band.Fraction * parent.Green) * band.Normalizer;
                double b = (child.Blue - band.Fraction * parent.Blue) * band.Normalizer;
                partial += band.R * r + band.G * g + band.B * b;
                remaining -= r * r + g * g + b * b;
                // Cauchy-Schwarz bounds the unexamined channels. Only discard impossible matches.
                double gap = needed - partial;
                if (gap > 0 && gap * gap > band.Remaining * Math.Max(0, remaining) + 1e-10 * norm * norm) return -1;
            }
            long dot = 0;
            for (int row = 0; row < box.Height; row++)
                for (int col = 0; col < box.Width; col++)
                {
                    int r, g, b, tr, tg, tb;
                    frame.Rgb(x + col, y + row, out r, out g, out b);
                    pattern.Rgb(col, row, out tr, out tg, out tb);
                    dot += r * tr + g * tg + b * tb;
                }
            return (dot - templateTotal.Dot(total) / count) / norm;
        }
    }
}
