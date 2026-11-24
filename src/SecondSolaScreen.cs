using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal sealed class SecondSolaScreen
    {
        private static readonly Regex DatePattern = new Regex(@"^(0[1-9]|1[0-2])/(0[1-9]|[12]\d|3[01])(?:周[一二三四五六日])?$");
        internal static readonly Regex Han = new Regex(@"[\u4e00-\u9fff]");
        internal static readonly Regex HanOrNumber = new Regex(@"[\u4e00-\u9fff\d]");

        internal readonly PixelFrame Frame;
        internal readonly List<OcrLine> Lines;

        internal SecondSolaScreen(PixelFrame frame, IEnumerable<OcrLine> lines)
        {
            Frame = frame;
            Lines = lines.Where(line => line.Score >= .60 && !line.Box.IsEmpty && !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new OcrLine { Box = line.Box, Text = OcrText.Compact(line.Text), Score = line.Score }).ToList();
        }

        internal OcrLine Find(string text, double left = 0, double top = 0, double right = 1, double bottom = 1,
            bool contains = false)
        {
            return In(left, top, right, bottom).FirstOrDefault(line => contains ? line.Text.Contains(text) : line.Text == text);
        }

        internal IEnumerable<OcrLine> In(double left, double top, double right, double bottom)
        {
            return Lines.Where(line => X(line) >= left && X(line) <= right && Y(line) >= top && Y(line) <= bottom);
        }

        internal OcrLine ResumeJourney(Func<IEnumerable<OcrLine>> readFocused)
        {
            return Find("继续旅程", .65, .60, 1, .95, true) ??
                new SecondSolaScreen(Frame, readFocused()).Find("继续旅程", .64, .59, .97, .96, true);
        }

        internal int Date
        {
            get
            {
                foreach (OcrLine line in Lines.Where(line => line.Box.X < Frame.Width * .14 && line.Box.Y < Frame.Height * .12))
                {
                    Match match = DatePattern.Match(line.Text);
                    if (match.Success)
                        return int.Parse(match.Groups[1].Value) * 100 + int.Parse(match.Groups[2].Value);
                }
                return 0;
            }
        }

        internal int? Fatigue()
        {
            foreach (OcrLine line in In(.57, 0, .77, .11))
            {
                Match match = Regex.Match(line.Text, @"^(\d{1,3})%$");
                if (match.Success)
                    return int.Parse(match.Groups[1].Value);
            }
            return null;
        }

        internal int? Number(double left, double top, double right, double bottom)
        {
            foreach (OcrLine line in In(left, top, right, bottom))
            {
                int value;
                if (int.TryParse(line.Text.Replace(",", ""), out value) && value >= 0)
                    return value;
            }
            return null;
        }

        internal OcrLine Selected(List<OcrLine> choices, OcrLine key, double left, double right, double half)
        {
            if (key != null)
                return choices.FirstOrDefault(line => Math.Abs(Y(line) - Y(key)) < .026);
            if (choices.Count < 2)
                return null;
            foreach (OcrLine option in choices)
            {
                Rectangle strip = Region(left, Y(option) - half, right, Y(option) + half);
                int matching = 0;
                for (int y = strip.Top; y < strip.Bottom; y++)
                    if (Fraction(new Rectangle(strip.Left, y, strip.Width, 1), (h, s, v) => h > 18 && h < 40 && s > 80 && v > 215) > 0)
                        matching++;
                if (matching > strip.Height * .55)
                    return option;
            }
            return null;
        }

        internal double X(OcrLine line) { return (line.Box.Left + line.Box.Width / 2.0) / Math.Max(1, Frame.Width); }
        internal double Y(OcrLine line) { return (line.Box.Top + line.Box.Height / 2.0) / Math.Max(1, Frame.Height); }

        internal Rectangle Region(double left, double top, double right, double bottom)
        {
            return Rectangle.Intersect(new Rectangle(0, 0, Frame.Width, Frame.Height),
                Rectangle.FromLTRB((int)Math.Round(left * Frame.Width), (int)Math.Round(top * Frame.Height),
                    (int)Math.Round(right * Frame.Width), (int)Math.Round(bottom * Frame.Height)));
        }

        internal double Mean(Rectangle area, int stride = 1)
        {
            long sum = 0;
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y += stride)
                for (int x = area.Left; x < area.Right; x += stride)
                {
                    int r, g, b;
                    Frame.Rgb(x, y, out r, out g, out b);
                    sum += r + g + b;
                    count++;
                }
            return count == 0 ? 0 : sum / (3.0 * count);
        }

        internal double LightFraction(Rectangle area, int level, bool brighter)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int red, green, blue;
                    Frame.Rgb(x, y, out red, out green, out blue);
                    int sum = red + green + blue;
                    if (brighter ? sum > level * 3 : sum < level * 3)
                        count++;
                }
            return area.Width <= 0 || area.Height <= 0 ? 0 : count / (double)(area.Width * area.Height);
        }

        internal double Fraction(Rectangle area, Func<int, int, int, bool> match)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int hue, saturation, value;
                    Hsv(x, y, out hue, out saturation, out value);
                    if (match(hue, saturation, value))
                        count++;
                }
            return area.Width <= 0 || area.Height <= 0 ? 0 : count / (double)(area.Width * area.Height);
        }

        internal Tuple<OcrLine, string> MapMarker()
        {
            string directions = "";
            foreach (Badge mark in RedBadges(Region(0, 0, 1, 1), 8, 175))
            {
                Rectangle box = mark.Box;
                double rx = mark.Center.X / Frame.Width, ry = mark.Center.Y / Frame.Height;
                double width = box.Width / (double)Frame.Width, height = box.Height / (double)Frame.Height;
                double ratio = box.Width / (double)box.Height;
                if (rx <= .025 || rx >= .985 || ry <= .07 || ry >= .96 || width <= .011 || width >= .042 ||
                    height <= .018 || height >= .07 || ratio <= .58 || ratio >= 1.45 ||
                    mark.Area <= .35 * box.Width * box.Height || Fraction(box, (h, s, v) => s < 65 && v > 195) <= .07)
                    continue;
                OcrLine near = Lines.FirstOrDefault(line => rx - X(line) > 0 && rx - X(line) < .09 &&
                    Y(line) - ry > 0 && Y(line) - ry < .065 && line.Text.Length >= 2 && line.Text.Length <= 10 &&
                    !(X(line) > .71 && Y(line) < .36) && IsLocationLabel(line));
                if (near != null)
                    return Tuple.Create(near, "");
                if (width >= .018)
                {
                    string pan = (rx < .22 ? "A" : rx > .84 ? "D" : "") + (ry < .185 ? "W" : ry > .865 ? "S" : "");
                    if (pan.Length > 0)
                        directions = pan;
                }
            }
            return Tuple.Create<OcrLine, string>(null, directions);
        }

        internal static bool IsLocationLabel(OcrLine line)
        {
            return Han.IsMatch(line.Text) && !line.Text.Contains("阶段目标") && !line.Text.Contains("距离") &&
                !line.Text.Contains("还有");
        }

        internal Point? GoalBadge()
        {
            OcrLine timer = Find("还有", .75, .27, .94, .55, true);
            double bottom = timer == null ? .29 : Y(timer) - .035;
            Rectangle area = Region(.93, .17, .97, bottom);
            Badge badge = RedBadges(area, 23, 155).FirstOrDefault(mark => mark.Area > Frame.Width * Frame.Height * .00004);
            if (badge == null)
                return null;
            return new Point((int)(badge.Center.X + Frame.Width * (.927 - .93)),
                (int)(badge.Center.Y + Frame.Height * (.195 - .17)));
        }

        private sealed class Badge
        {
            internal Rectangle Box;
            internal PointF Center;
            internal int Area;
        }

        private IEnumerable<Badge> RedBadges(Rectangle area, int lowHue, int minimumValue)
        {
            if (area.Width <= 0 || area.Height <= 0)
                yield break;
            int width = area.Width, height = area.Height;
            var mask = new bool[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int hue, saturation, value;
                    Hsv(area.X + x, area.Y + y, out hue, out saturation, out value);
                    mask[y * width + x] = (hue < lowHue || hue > 170) && saturation > 145 && value > minimumValue;
                }
            var visit = new Stack<int>();
            for (int index = 0; index < mask.Length; index++)
            {
                if (!mask[index])
                    continue;
                mask[index] = false;
                visit.Push(index);
                int minX = width, minY = height, maxX = 0, maxY = 0, count = 0;
                long sumX = 0, sumY = 0;
                while (visit.Count > 0)
                {
                    int current = visit.Pop();
                    int x = current % width, y = current / width;
                    sumX += x;
                    sumY += y;
                    count++;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                    for (int dy = Math.Max(0, y - 1); dy <= Math.Min(height - 1, y + 1); dy++)
                        for (int dx = Math.Max(0, x - 1); dx <= Math.Min(width - 1, x + 1); dx++)
                        {
                            int neighbor = dy * width + dx;
                            if (!mask[neighbor])
                                continue;
                            mask[neighbor] = false;
                            visit.Push(neighbor);
                        }
                }
                if (count >= 8)
                    yield return new Badge { Area = count,
                        Box = Rectangle.FromLTRB(area.X + minX, area.Y + minY, area.X + maxX + 1, area.Y + maxY + 1),
                        Center = new PointF(area.X + (float)sumX / count, area.Y + (float)sumY / count) };
            }
        }

        private void Hsv(int x, int y, out int h, out int s, out int v)
        {
            int red, green, blue;
            Frame.Rgb(x, y, out red, out green, out blue);
            v = Math.Max(red, Math.Max(green, blue));
            int difference = v - Math.Min(red, Math.Min(green, blue));
            s = v == 0 ? 0 : difference * 255 / v;
            if (difference == 0)
            {
                h = 0;
                return;
            }
            int scaled = v == red ? 30 * (green - blue) : v == green ? 60 * difference + 30 * (blue - red) :
                120 * difference + 30 * (red - green);
            h = (scaled / difference + 180) % 180;
        }
    }
}
