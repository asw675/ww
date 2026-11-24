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
        private readonly byte[] rgb;
        internal readonly byte[] SourcePng;
        internal readonly ReferenceFeature Reference;
        private readonly int lightSum, lightSquares, lightHigh, lightLow, edgeFloor;
        private readonly long redSum, greenSum, blueSum;
        private readonly double templateVariance;

        internal VisualPattern(string label, string fighter, Rectangle area, VisualSignature signature, byte[] light,
            byte[] sourcePng = null, byte[] rgb = null, ReferenceFeature reference = null)
        {
            Label = label;
            Fighter = fighter;
            Area = area;
            Signature = signature;
            this.light = light;
            SourcePng = sourcePng;
            Reference = reference;
            this.rgb = rgb;
            if (rgb != null && rgb.Length == area.Width * area.Height * 3)
            {
                long squares = 0;
                for (int i = 0; i < rgb.Length; i += 3)
                {
                    int r = rgb[i], g = rgb[i + 1], b = rgb[i + 2];
                    redSum += r; greenSum += g; blueSum += b;
                    squares += r * r + g * g + b * b;
                }
                templateVariance = rgb.Length / 3.0 * squares - ((double)redSum * redSum + (double)greenSum * greenSum + (double)blueSum * blueSum);
            }
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
        internal bool HasColorCorrelation { get { return rgb != null && templateVariance > 0; } }

        internal double ColorCorrelation(PixelFrame frame, Rectangle placement)
        {
            if (!HasColorCorrelation || placement.Size != Area.Size || placement.Left < 0 || placement.Top < 0 ||
                placement.Right > frame.Width || placement.Bottom > frame.Height) return 0;
            long sr = 0, sg = 0, sb = 0, squares = 0, product = 0;
            int index = 0;
            for (int y = placement.Top; y < placement.Bottom; y++)
            for (int x = placement.Left; x < placement.Right; x++)
            {
                int r, g, b;
                frame.Rgb(x, y, out r, out g, out b);
                sr += r; sg += g; sb += b;
                squares += r * r + g * g + b * b;
                product += r * rgb[index++] + g * rgb[index++] + b * rgb[index++];
            }
            double n = rgb.Length / 3.0;
            double variance = n * squares - ((double)sr * sr + (double)sg * sg + (double)sb * sb);
            double denominator = Math.Sqrt(templateVariance * Math.Max(0, variance));
            return denominator > 0 ? (n * product - ((double)sr * redSum + (double)sg * greenSum + (double)sb * blueSum)) / denominator : 0;
        }

        internal double BestColorCorrelation(ColorMatchArea region, double minimum)
        {
            return HasColorCorrelation ? region.Best(rgb, Area.Width, Area.Height, redSum, greenSum, blueSum,
                templateVariance, minimum) : 0;
        }

        internal bool HasLightEdge(PixelFrame frame, Rectangle placement, double contrastFraction = 1)
        {
            return light != null && SampleLight(frame, placement, lightHigh) - SampleLight(frame, placement, lightLow) >=
                edgeFloor * contrastFraction;
        }

        private int SampleLight(PixelFrame frame, Rectangle placement, int index)
        {
            int x = index % Area.Width, y = index / Area.Width;
            if (placement.Width != Area.Width || placement.Height != Area.Height)
            {
                x = (2 * x + 1) * placement.Width / (2 * Area.Width);
                y = (2 * y + 1) * placement.Height / (2 * Area.Height);
            }
            return frame.Light(placement.Left + x, placement.Top + y);
        }

        internal bool MatchesLight(PixelFrame frame, Rectangle placement)
        {
            if (light == null) return true;
            return LightSimilarity(frame, placement) >= .63;
        }

        internal double LightSimilarity(PixelFrame frame, Rectangle placement, int negativeThreshold = -1)
        {
            if (light == null) return 0;
            int y = 0, yy = 0, xy = 0;
            for (int i = 0; i < light.Length; i++)
            {
                int a = unchecked((sbyte)light[i]);
                int b = SampleLight(frame, placement, i);
                if (negativeThreshold >= 0)
                    b = b <= negativeThreshold ? 255 : 0;
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
            bool centered = Label.IndexOf("_hcenter", StringComparison.Ordinal) >= 0;
            if (!centered && (Area.Left > 1120 || Label == "box_target_enemy_long" || Label == "target_box_long2"))
                region.X += width - 1600;
            else if (centered || Area.Left >= 80)
                region.X += (width - 1600) / 2;
            if (Fighter != null)
                region.Y += 110 * slot;
            return region;
        }
    }

    // Dense matching in the small declared portrait ROI. Integral sums make
    // normalization O(1) per candidate. Block-mean projection is a mathematical
    // upper bound (Cauchy-Schwarz), not an approximate similarity rejection.
    internal sealed class ColorMatchArea
    {
        private readonly byte[] values;
        private readonly int width, height, stride;
        private readonly int[] red, green, blue, squared;
        internal ColorMatchArea(PixelFrame frame, Rectangle rectangle)
        {
            width = rectangle.Width; height = rectangle.Height; stride = width + 1;
            values = new byte[width * height * 4];
            frame.CopyBgra(rectangle, values);
            red = new int[stride * (height + 1)]; green = new int[red.Length];
            blue = new int[red.Length]; squared = new int[red.Length];
            for (int y = 1; y <= height; y++)
            {
                int rsum = 0, gsum = 0, bsum = 0, ssq = 0;
                for (int x = 1; x <= width; x++)
                {
                    int i = ((y - 1) * width + x - 1) * 4;
                    int r = values[i+2], g = values[i+1], b = values[i];
                    rsum += r; gsum += g; bsum += b; ssq += r*r + g*g + b*b;
                    int at = y*stride+x, prior = at-stride;
                    red[at] = red[prior]+rsum; green[at] = green[prior]+gsum;
                    blue[at] = blue[prior]+bsum; squared[at] = squared[prior]+ssq;
                }
            }
        }

        private int Sum(int[] table, int left, int top, int w, int h)
        {
            int a = top * stride + left, b = a + h * stride;
            return table[b+w] - table[b] - table[a+w] + table[a];
        }

        private struct Block
        {
            internal int Left, Top, Width, Height;
            internal double R, G, B, InverseCount;
        }

        internal double Best(byte[] template, int tw, int th, long tr, long tg, long tb, double templateVariance, double minimum)
        {
            int n = tw * th;
            if (tw > width || th > height || n == 0) return 0;
            double templateEnergy = templateVariance / n;
            double templateMean = ((double)tr*tr + (double)tg*tg + (double)tb*tb) / n;
            var blocks = new List<Block>(16);
            double templateBlockEnergy = -templateMean;
            for (int gy = 0; gy < 4; gy++) for (int gx = 0; gx < 4; gx++)
            {
                int left = gx * tw / 4, right = (gx+1)*tw/4, top = gy*th/4, bottom = (gy+1)*th/4;
                if (right == left || top == bottom) continue;
                var block = new Block { Left=left, Top=top, Width=right-left, Height=bottom-top,
                    InverseCount=1.0/((right-left)*(bottom-top)) };
                for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
                {
                    int at=(y*tw+x)*3; block.R+=template[at]; block.G+=template[at+1]; block.B+=template[at+2];
                }
                templateBlockEnergy += (block.R*block.R + block.G*block.G + block.B*block.B)*block.InverseCount;
                blocks.Add(block);
            }
            double residualT = Math.Max(0, templateEnergy-templateBlockEnergy);
            Block[] parts = blocks.ToArray();
            double best = minimum, result = 0;
            for(int y=0;y<=height-th;y++) for(int x=0;x<=width-tw;x++)
            {
                int sr=Sum(red,x,y,tw,th), sg=Sum(green,x,y,tw,th), sb=Sum(blue,x,y,tw,th);
                double sourceMean=((double)sr*sr+(double)sg*sg+(double)sb*sb)/n;
                double sourceEnergy=Sum(squared,x,y,tw,th)-sourceMean;
                if(sourceEnergy<=0) continue;
                double denominator=Math.Sqrt(sourceEnergy*templateEnergy);
                double meanDot=((double)sr*tr+(double)sg*tg+(double)sb*tb)/n;
                double blockDot=-meanDot, blockEnergy=-sourceMean;
                foreach(Block p in parts)
                {
                    int l=x+p.Left, t=y+p.Top;
                    double r=Sum(red,l,t,p.Width,p.Height), g=Sum(green,l,t,p.Width,p.Height), b=Sum(blue,l,t,p.Width,p.Height);
                    blockDot+=(r*p.R+g*p.G+b*p.B)*p.InverseCount;
                    blockEnergy+=(r*r+g*g+b*b)*p.InverseCount;
                }
                double upper=blockDot+Math.Sqrt(residualT*Math.Max(0,sourceEnergy-blockEnergy));
                if(upper + 1e-6 < denominator*best) continue;
                int product=0;
                for (int ty=0;ty<th;ty++)
                {
                    int at=((y+ty)*width+x)*4, ti=ty*tw*3;
                    for (int end=at+tw*4;at<end;at+=4,ti+=3)
                        product += values[at+2]*template[ti]+values[at+1]*template[ti+1]+values[at]*template[ti+2];
                }
                double score=(product-meanDot)/denominator;
                if(score>=best) { best=score; result=score; }
            }
            return result;
        }
    }

    // Original OKWW annotated crop, independent of the legacy 900p rank
    // features. Keep the original bounds and pixels for reference matching.
    internal sealed class ReferenceFeature
    {
        private readonly int sourceWidth, sourceHeight;
        private readonly Rectangle sourceBounds;
        private readonly byte[] pixels;
        private VisualPattern cached;
        private int cachedWidth, cachedHeight;

        internal ReferenceFeature(int sourceWidth, int sourceHeight, Rectangle bounds, byte[] pixels)
        {
            this.sourceWidth = sourceWidth;
            this.sourceHeight = sourceHeight;
            sourceBounds = bounds;
            this.pixels = pixels;
            if (pixels != null && pixels.Length != bounds.Width * bounds.Height * 3)
                throw new InvalidDataException("Invalid original feature pixels.");
        }

        internal Rectangle Map(int width, int height)
        {
            return Map(sourceWidth, sourceHeight, sourceBounds, width, height);
        }

        internal static Rectangle Map(int sourceWidth, int sourceHeight, Rectangle bounds, int width, int height)
        {
            double scale = Math.Min(width / (double)sourceWidth, height / (double)sourceHeight);
            int x = bounds.X > sourceWidth / 2 ? width - (int)Math.Round((sourceWidth - bounds.X) * scale) : (int)Math.Round(bounds.X * scale);
            int y = bounds.Y > sourceHeight / 2 ? height - (int)Math.Round((sourceHeight - bounds.Y) * scale) : (int)Math.Round(bounds.Y * scale);
            return new Rectangle(x, y, Math.Max(1, (int)Math.Round(bounds.Width * scale)), Math.Max(1, (int)Math.Round(bounds.Height * scale)));
        }

        internal VisualPattern At(string label, string fighter, int width, int height)
        {
            if (pixels == null) return null;
            lock (this)
            {
                if (cached != null && width == cachedWidth && height == cachedHeight) return cached;
                Rectangle area = Map(width, height);
                byte[] rgb = pixels;
                if (area.Size != sourceBounds.Size)
                {
                    rgb = new byte[area.Width * area.Height * 3];
                    for (int y = 0; y < area.Height; y++)
                    for (int x = 0; x < area.Width; x++)
                    {
                        double sx = Math.Max(0, Math.Min(sourceBounds.Width - 1, (x + .5) * sourceBounds.Width / area.Width - .5));
                        double sy = Math.Max(0, Math.Min(sourceBounds.Height - 1, (y + .5) * sourceBounds.Height / area.Height - .5));
                        int x0 = (int)sx, y0 = (int)sy;
                        int x1 = Math.Min(sourceBounds.Width - 1, x0 + 1), y1 = Math.Min(sourceBounds.Height - 1, y0 + 1);
                        double dx = sx - x0, dy = sy - y0;
                        for (int c = 0; c < 3; c++)
                        {
                            double top = pixels[(y0 * sourceBounds.Width + x0) * 3 + c] * (1 - dx) + pixels[(y0 * sourceBounds.Width + x1) * 3 + c] * dx;
                            double bottom = pixels[(y1 * sourceBounds.Width + x0) * 3 + c] * (1 - dx) + pixels[(y1 * sourceBounds.Width + x1) * 3 + c] * dx;
                            rgb[(y * area.Width + x) * 3 + c] = (byte)Math.Round(top * (1 - dy) + bottom * dy);
                        }
                    }
                }
                cachedWidth = width;
                cachedHeight = height;
                return cached = new VisualPattern(label, fighter, area, null, null, rgb: rgb);
            }
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
                        if (cells.Length < 7 || cells.Length > 12) throw new InvalidDataException("Invalid visual signature row.");
                        int[] coordinates = cells.Skip(2).Take(4).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                        ReferenceFeature reference = null;
                        if (cells.Length >= 11 && cells[10].Length != 0)
                        {
                            int[] source = cells[10].Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                            if (source.Length != 6) throw new InvalidDataException("Invalid original feature bounds.");
                            reference = new ReferenceFeature(source[0], source[1], new Rectangle(source[2], source[3], source[4], source[5]),
                                cells.Length == 12 && cells[11].Length > 0 ? Convert.FromBase64String(cells[11]) : null);
                        }
                        result.Add(new VisualPattern(cells[0], cells[1] == "-" ? null : cells[1],
                            new Rectangle(coordinates[0], coordinates[1], coordinates[2], coordinates[3]),
                            VisualSignature.Parse(cells[6]), cells.Length >= 8 && cells[7].Length != 0 ? Convert.FromBase64String(cells[7]) : null,
                            cells.Length >= 9 && cells[8].Length != 0 ? Convert.FromBase64String(cells[8]) : null,
                            cells.Length >= 10 && cells[9].Length != 0 ? Convert.FromBase64String(cells[9]) : null, reference));
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
