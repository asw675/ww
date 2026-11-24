using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Text;

namespace TideCombat
{
    internal static class HudGlyphs
    {
        internal static double ReadChallengeClock(PixelFrame frame)
        {
            int center = frame.Width / 2;
            var area = Rectangle.FromLTRB(center - 42, 111, center + 76, 143);
            var text = new StringBuilder();
            for (int x = area.Left; x < area.Right;)
            {
                int start = x, top = area.Bottom, bottom = area.Top;
                while (x < area.Right)
                {
                    bool column = false;
                    for (int y = area.Top; y < area.Bottom; y++)
                    {
                        if (!frame.Pale(x, y, 209, 46)) continue;
                        column = true;
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y + 1);
                    }
                    if (!column) break;
                    x++;
                }
                if (x == start) { x++; continue; }
                var glyph = Rectangle.FromLTRB(start, top, x, bottom);
                if (glyph.Width <= 6 && glyph.Height <= 13)
                {
                    bool middleBlank = true;
                    for (int dotX = glyph.Left; dotX < glyph.Right; dotX++)
                        middleBlank &= !frame.Pale(dotX, glyph.Top + glyph.Height / 2, 209, 46);
                    if (glyph.Height >= 4 && middleBlank) { text.Append(':'); continue; }
                    if (glyph.Height <= 5) { text.Append('.'); continue; }
                }
                if (glyph.Height >= 10 && glyph.Height <= 28 && glyph.Width <= 20)
                {
                    int digit = SkillHud.ReadDigit(frame, 0, 0, glyph, 209);
                    if (digit < 0) return double.NaN;
                    text.Append((char)('0' + digit));
                }
                else return double.NaN;
            }
            return ParseChallengeClock(text.ToString());
        }

        internal static double ParseChallengeClock(string text)
        {
            string[] values = text.Split(':', '.');
            int minutes, seconds, fraction = 0;
            if (values.Length < 2 || values.Length > 3 || values[0].Length < 1 || values[0].Length > 2 || values[1].Length != 2 ||
                text[values[0].Length] != ':' ||
                !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) ||
                !int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds >= 60)
                return double.NaN;
            if (values.Length == 3 && (values[2].Length < 1 || values[2].Length > 2 ||
                !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out fraction))) return double.NaN;
            return minutes * 60 + seconds + (values.Length == 3 ? fraction / (values[2].Length == 2 ? 100.0 : 10.0) : 0);
        }
    }

    internal struct Badge
    {
        private static readonly int[] EdgePoints = { -3, 0, 3 };
        internal bool Found;
        internal int X, Y;
        internal char Glyph;

        internal static Badge Find(PixelFrame frame, int centerX, int centerY, int horizontal, int vertical, string allowedGlyphs)
        {
            int best = 0;
            var badge = new Badge();
            for (int y = centerY - vertical; y <= centerY + vertical; y++)
            {
                for (int x = centerX - horizontal; x <= centerX + horizontal; x++)
                {
                    for (int halfSize = 6; halfSize <= 8; halfSize++)
                    {
                        int size = halfSize - 2;
                        int topLeft = frame.Light(x - size, y - size);
                        int topRight = frame.Light(x + size, y - size);
                        int bottomLeft = frame.Light(x - size, y + size);
                        int bottomRight = frame.Light(x + size, y + size);
                        int floor = Math.Min(Math.Min(topLeft, topRight), Math.Min(bottomLeft, bottomRight));
                        if (floor < 152)
                            continue;

                        int brightness = (topLeft + topRight + bottomLeft + bottomRight) / 4;
                        int boundary = 0;
                        int edgeStrength = 0;
                        foreach (int point in EdgePoints)
                        {
                            IncludeEdge(frame.Light(x - halfSize - 2, y + point), brightness, ref boundary, ref edgeStrength);
                            IncludeEdge(frame.Light(x + halfSize + 2, y + point), brightness, ref boundary, ref edgeStrength);
                            IncludeEdge(frame.Light(x + point, y - halfSize - 2), brightness, ref boundary, ref edgeStrength);
                            IncludeEdge(frame.Light(x + point, y + halfSize + 2), brightness, ref boundary, ref edgeStrength);
                        }
                        if (boundary < 8)
                            continue;
                        var glyphMask = GlyphReader.DarkInk(frame, x, y, halfSize, brightness - 70);
                        char glyph = GlyphReader.Match(glyphMask, allowedGlyphs);
                        if (glyphMask == null)
                            continue;
                        int score = brightness + edgeStrength / 4 + boundary * 10 +
                            (glyph != '\0' ? 130 : 0) - Math.Abs(x - centerX) - Math.Abs(y - centerY);
                        if (score <= best)
                            continue;
                        best = score;
                        badge = new Badge { Found = true, X = x, Y = y, Glyph = glyph };
                    }
                }
            }
            return badge;
        }

        private static void IncludeEdge(int outside, int inside, ref int edges, ref int strength)
        {
            if (inside - outside < 43)
                return;
            edges++;
            strength += inside - outside;
        }
    }

    internal static class GlyphReader
    {
        private const int GridWidth = 10, GridHeight = 13;
        private static readonly Dictionary<char, bool[][]> Reference = MakeReference();

        internal static bool[] DarkInk(PixelFrame frame, int cx, int cy, int half, int ceiling)
        {
            int width = half * 2 - 3;
            int height = half * 2 - 1;
            int left = cx - width / 2;
            int top = cy - height / 2;
            var pixels = new bool[width * height];
            int total = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool ink = frame.Light(left + x, top + y) <= ceiling;
                    pixels[y * width + x] = ink;
                    if (ink)
                        total++;
                }
            }
            if (total < 7 || total * 2 > width * height)
                return null;
            return Normalize(pixels, width, height);
        }

        internal static char Match(bool[] ink, string allowed, double minimum = .55, double margin = .045)
        {
            if (ink == null)
                return '\0';
            char bestChar = '\0';
            double best = 0, second = 0;
            for (int index = 0; index < allowed.Length; index++)
            {
                char output = allowed[index];
                if (allowed.IndexOf(output) != index) continue;
                bool[][] masks;
                if (!Reference.TryGetValue(output, out masks))
                    continue;
                double highest = 0;
                foreach (bool[] reference in masks)
                    highest = Math.Max(highest, Similarity(ink, reference));
                if (highest > best)
                {
                    second = best;
                    best = highest;
                    bestChar = output;
                }
                else
                    second = Math.Max(second, highest);
            }
            return best >= minimum && best - second >= margin ? bestChar : '\0';
        }

        private static double Similarity(bool[] left, bool[] right)
        {
            int both = 0, either = 0;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] || right[index]) either++;
                if (left[index] && right[index]) both++;
            }
            return either == 0 ? 0 : (double)both / either;
        }

        internal static bool[] Normalize(bool[] source, int width, int height)
        {
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!source[y * width + x])
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            if (right < left || bottom - top < 4)
                return null;
            var result = new bool[GridWidth * GridHeight];
            for (int y = 0; y < GridHeight; y++)
            {
                for (int x = 0; x < GridWidth; x++)
                {
                    int sx = left + (2 * x + 1) * (right + 1 - left) / (2 * GridWidth);
                    int sy = top + (2 * y + 1) * (bottom + 1 - top) / (2 * GridHeight);
                    result[y * GridWidth + x] = source[sy * width + sx];
                }
            }
            return result;
        }

        private static Dictionary<char, bool[][]> MakeReference()
        {
            const int size = 24;
            string[] fonts = { "Arial", "Tahoma", "Segoe UI", "Calibri", "Bahnschrift" };
            var result = new Dictionary<char, bool[][]>();
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                foreach (char letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
                {
                    var references = new List<bool[]>();
                    foreach (string family in fonts)
                    {
                        if (letter >= 'A' && (family == "Calibri" || family == "Bahnschrift")) continue;
                        foreach (int fontSize in new[] { 10, 12, 15 })
                        {
                            using (var font = new Font(family, fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
                            {
                                graphics.Clear(Color.White);
                                graphics.DrawString(letter.ToString(), font, Brushes.Black, 2, 1, StringFormat.GenericTypographic);
                                var source = new bool[size * size];
                                for (int y = 0; y < size; y++)
                                    for (int x = 0; x < size; x++)
                                        source[y * size + x] = bitmap.GetPixel(x, y).R < 120;
                                bool[] pattern = Normalize(source, size, size);
                                if (pattern != null)
                                    references.Add(pattern);
                            }
                        }
                    }
                    result.Add(letter, references.ToArray());
                }
            }
            return result;
        }
    }

    internal static class SkillHud
    {
        internal static SkillState Read(PixelFrame frame, Badge badge, out double cooldownSeconds, out bool lit)
        {
            int centerY = badge.Y - 39;
            bool cooling = ReadDecimalTimer(frame, badge.X, centerY, 198, out cooldownSeconds);
            if (!cooling || double.IsNaN(cooldownSeconds))
            {
                double alternate;
                if (ReadDecimalTimer(frame, badge.X, centerY, 174, out alternate))
                {
                    cooling = true;
                    cooldownSeconds = alternate;
                }
            }
            if (cooling && double.IsNaN(cooldownSeconds))
                ReadDecimalTimer(frame, badge.X, centerY, 160, out cooldownSeconds);

            int white = 0;
            int bright = 0;
            int dark = 0;
            for (int y = -23; y <= 23; y++)
            {
                for (int x = -23; x <= 23; x++)
                {
                    if (x * x + y * y > 23 * 23)
                        continue;
                    int sx = badge.X + x, sy = centerY + y;
                    if ((!cooling || Math.Abs(x) > 16 || Math.Abs(y) > 10) && frame.Pale(sx, sy, 175, 67))
                        white++;
                    int light = frame.Light(sx, sy);
                    if (light >= 188 && (!cooling || Math.Abs(x) > 16 || Math.Abs(y) > 10)) bright++;
                    if (light < 126) dark++;
                }
            }
            lit = dark > 420 && (white >= 38 || bright >= 122);
            return cooling ? SkillState.Cooling : lit ? SkillState.Ready : SkillState.Unavailable;
        }

        private static bool ReadDecimalTimer(PixelFrame frame, int cx, int cy, int brightness, out double seconds)
        {
            seconds = double.NaN;
            // Cooldown digits are centered, bright, and share a baseline with a decimal point.
            const int width = 55, height = 34;
            int left = cx - width / 2;
            int top = cy - height / 2;
            var white = new bool[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    white[y * width + x] = frame.Pale(left + x, top + y, brightness, 56);

            var components = new List<Rectangle>(16);
            AddColumnGroups(white, width, height, components);
            var queue = new int[white.Length];
            for (int cell = 0; cell < white.Length; cell++)
            {
                if (!white[cell])
                    continue;
                int end = 0;
                Enqueue(white, queue, ref end, cell);
                int minX = width, maxX = -1, minY = height, maxY = -1;
                for (int index = 0; index < end; index++)
                {
                    int point = queue[index], x = point % width, y = point / width;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    if (x > 0) Enqueue(white, queue, ref end, point - 1);
                    if (x + 1 < width) Enqueue(white, queue, ref end, point + 1);
                    if (y > 0)
                        EnqueueRow(white, queue, ref end, point - width, x, width);
                    if (y + 1 < height)
                        EnqueueRow(white, queue, ref end, point + width, x, width);
                }
                if (end > 0 && maxX - minX < 16 && maxY - minY < 21)
                    components.Add(Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1));
            }
            bool timerPresent = false;
            foreach (Rectangle dot in components)
            {
                if (dot.Width > 6 || dot.Height > 6 || dot.Top < 12 || dot.Top > 26 ||
                    Math.Abs(dot.X - width / 2) > 13)
                    continue;
                Rectangle before = Rectangle.Empty, after = Rectangle.Empty;
                foreach (Rectangle digit in components)
                {
                    if (!IsTimerDigit(digit, dot.Bottom)) continue;
                    if (dot.Left >= digit.Right && dot.Left - digit.Right <= 10 &&
                        (before.IsEmpty || digit.Right > before.Right || digit.Right == before.Right && digit.Width > before.Width))
                        before = digit;
                    if (digit.Left >= dot.Right && digit.Left - dot.Right <= 10 &&
                        (after.IsEmpty || digit.Left < after.Left || digit.Left == after.Left && digit.Width > after.Width))
                        after = digit;
                }
                if (before.IsEmpty || after.IsEmpty) continue;
                timerPresent = true;
                int unit = ReadDigit(frame, left, top, before, brightness);
                int decimalDigit = ReadDigit(frame, left, top, after, brightness);
                if (unit < 0 || decimalDigit < 0) continue;
                int tens = 0;
                foreach (Rectangle digit in components)
                {
                    if (digit.Right > before.Left || before.Left - digit.Right > 8 || !IsTimerDigit(digit, dot.Bottom)) continue;
                    int value = ReadDigit(frame, left, top, digit, brightness);
                    if (value > 0) { tens = value; break; }
                }
                seconds = tens * 10 + unit + decimalDigit / 10.0;
                return true;
            }
            return timerPresent;
        }

        private static bool IsTimerDigit(Rectangle digit, int baseline)
        {
            return digit.Height >= 8 && digit.Height <= 19 && digit.Width >= 2 && digit.Width <= 15 &&
                Math.Abs(digit.Bottom - baseline) <= 3;
        }

        internal static int ReadDigit(PixelFrame frame, int left, int top, Rectangle rect, int brightness)
        {
            var ink = new bool[rect.Width * rect.Height];
            for (int y = 0; y < rect.Height; y++)
                for (int x = 0; x < rect.Width; x++)
                    ink[y * rect.Width + x] = frame.Pale(left + rect.X + x, top + rect.Y + y, brightness, 56);
            char digit = GlyphReader.Match(GlyphReader.Normalize(ink, rect.Width, rect.Height), "0123456789", .51, .012);
            if ((digit == '6' || digit == '9' || digit == '\0') && HasTwoHoles(ink, rect.Width, rect.Height)) return 8;
            return digit == '\0' ? -1 : digit - '0';
        }

        private static bool HasTwoHoles(bool[] ink, int width, int height)
        {
            var blanks = new bool[ink.Length];
            var queue = new int[ink.Length];
            for (int i = 0; i < ink.Length; i++) blanks[i] = !ink[i];
            int holes = 0;
            for (int cell = 0; cell < blanks.Length; cell++)
            {
                if (!blanks[cell]) continue;
                int count = 0;
                bool touchesEdge = false;
                Enqueue(blanks, queue, ref count, cell);
                for (int i = 0; i < count; i++)
                {
                    int point = queue[i], x = point % width, y = point / width;
                    if (x == 0 || x == width - 1 || y == 0 || y == height - 1) touchesEdge = true;
                    if (x > 0) Enqueue(blanks, queue, ref count, point - 1);
                    if (x < width - 1) Enqueue(blanks, queue, ref count, point + 1);
                    if (y > 0) Enqueue(blanks, queue, ref count, point - width);
                    if (y < height - 1) Enqueue(blanks, queue, ref count, point + width);
                }
                if (!touchesEdge && count >= 3 && ++holes == 2) return true;
            }
            return false;
        }

        private static void Enqueue(bool[] mask, int[] queue, ref int end, int cell)
        {
            if (!mask[cell])
                return;
            mask[cell] = false;
            queue[end++] = cell;
        }

        private static void EnqueueRow(bool[] mask, int[] queue, ref int end, int position, int x, int width)
        {
            Enqueue(mask, queue, ref end, position);
            if (x > 0)
                Enqueue(mask, queue, ref end, position - 1);
            if (x + 1 < width)
                Enqueue(mask, queue, ref end, position + 1);
        }

        private static void AddColumnGroups(bool[] mask, int width, int height, List<Rectangle> components)
        {
            // A diagonal stroke can break into several pieces when a frame is resized.
            // A column group still retains the overall bounds of the digit.
            int left = -1, top = height, bottom = 0;
            for (int x = 0; x <= width; x++)
            {
                int low = height, high = -1;
                if (x < width)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (!mask[y * width + x])
                            continue;
                        low = Math.Min(low, y);
                        high = y;
                    }
                }
                if (high >= 0)
                {
                    if (left < 0) left = x;
                    top = Math.Min(top, low);
                    bottom = Math.Max(bottom, high + 1);
                    continue;
                }
                if (left >= 0)
                {
                    components.Add(Rectangle.FromLTRB(left, top, x, bottom));
                    left = -1;
                    top = height;
                    bottom = 0;
                }
            }
        }
    }

    internal static class PortraitReader
    {
        private const int Side = 10;
        internal const int SampleLength = Side * Side * 3;

        internal static byte[] Sample(PixelFrame frame, int slot)
        {
            var sample = new byte[SampleLength];
            int left = frame.Width - 119;
            int top = 192 + 110 * slot;
            for (int y = 0; y < Side; y++)
            {
                for (int x = 0; x < Side; x++)
                {
                    int offset = (y * Side + x) * 3;
                    int sumRed = 0, sumGreen = 0, sumBlue = 0;
                    for (int dy = 0; dy < 3; dy++)
                    {
                        for (int dx = 0; dx < 3; dx++)
                        {
                            int red, green, blue;
                            frame.Rgb(left + x * 3 + dx, top + y * 3 + dy, out red, out green, out blue);
                            sumRed += red; sumGreen += green; sumBlue += blue;
                        }
                    }
                    sample[offset] = (byte)(sumRed / 9);
                    sample[offset + 1] = (byte)(sumGreen / 9);
                    sample[offset + 2] = (byte)(sumBlue / 9);
                }
            }
            return sample;
        }

        internal static void Identify(PixelFrame frame, HudSnapshot snapshot, List<CharacterProfile> profiles)
        {
            var scores = new double[3];
            for (int slot = 0; slot < 3; slot++)
            {
                if (snapshot.Health[slot] <= 0)
                    continue;
                byte[] observed = Sample(frame, slot);
                CharacterProfile best = null;
                double first = double.MaxValue, second = first;
                foreach (CharacterProfile profile in profiles)
                {
                    double distance = Distance(observed, profile.Portrait);
                    if (distance < first)
                    {
                        second = first;
                        first = distance;
                        best = profile;
                    }
                    else
                        second = Math.Min(second, distance);
                }
                if (first > 18 || second - first < 5)
                    continue;
                for (int previous = 0; previous < slot; previous++)
                {
                    if (!ReferenceEquals(snapshot.Profiles[previous], best))
                        continue;
                    if (scores[previous] <= first)
                        best = null;
                    else
                        snapshot.Profiles[previous] = null;
                    break;
                }
                snapshot.Profiles[slot] = best;
                scores[slot] = first;
            }
        }

        internal static double Distance(byte[] first, byte[] second)
        {
            int sum = 0;
            for (int index = 0; index < SampleLength; index++)
                sum += Math.Abs(first[index] - second[index]);
            return (double)sum / SampleLength;
        }
    }
}
