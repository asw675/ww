using System;
using System.Drawing;

namespace TideCombat
{
    internal static class GuidebookList
    {
        internal const double DefaultTop = 424.0 / 2160;

        internal static Point PointAt(Size size, double x, double y)
        {
            if (size.Height <= 0 || size.Width <= 0) return Point.Empty;
            if (Math.Abs(size.Width / (double)size.Height - 16.0 / 9) <= .01)
                return new Point((int)(x * size.Width), (int)(y * size.Height));
            double width = Math.Min(size.Width, size.Height * (16.0 / 9));
            return new Point(Anchor(x, size.Width, width), Anchor(y, size.Height, width * (9.0 / 16)));
        }

        internal static Rectangle Area(Size size, double left, double top, double right, double bottom)
        {
            double width = size.Width, height = size.Height;
            if (width <= 0 || height <= 0) return Rectangle.Empty;
            bool anchored = Math.Abs(width / height - 16.0 / 9) > .01;
            if (anchored)
            {
                width = Math.Min(width, height * (16.0 / 9));
                height = width * (9.0 / 16);
            }
            int x = anchored ? Anchor(left, size.Width, width) : (int)Math.Round(left * width);
            int y = anchored ? Anchor(top, size.Height, height) : (int)Math.Round(top * height);
            return Rectangle.Intersect(new Rectangle(Point.Empty, size), new Rectangle(x, y,
                (int)Math.Round((right - left) * width), (int)Math.Round((bottom - top) * height)));
        }

        private static int Anchor(double offset, int actual, double design)
        {
            return offset > .5 ? actual - (int)Math.Round((1 - offset) * design) : (int)Math.Round(offset * design);
        }

        internal static double ScrollTop(PixelFrame frame)
        {
            frame = frame.FullResolution;
            Rectangle area = Area(new Size(frame.Width, frame.Height), .969, .191, .978, .271);
            if (area.Width <= 0 || area.Height <= 0) return DefaultTop;
            var mask = new bool[area.Width * area.Height];
            for (int y = 0; y < area.Height; y++)
                for (int x = 0; x < area.Width; x++)
                    mask[y * area.Width + x] = frame.Pale(x + area.X, y + area.Y, 190, 255);
            int row = FindBar(mask, area.Width, area.Height, frame.Width * 5 / 2560, frame.Height * 10 / 1440);
            return row < 0 ? DefaultTop : (area.Y + row) / (double)frame.Height;
        }

        internal static Rectangle FindVerticalThumb(PixelFrame frame, Rectangle area,
            int minimumLength, int minimumLight, int saturation)
        {
            Rectangle thumb = Rectangle.Empty;
            for (int x = area.Left; x < area.Right; x++)
            {
                int top = area.Top;
                for (int y = area.Top; y <= area.Bottom; y++)
                {
                    if (y < area.Bottom && frame.Pale(x, y, minimumLight, saturation)) continue;
                    if (y - top >= minimumLength && y - top > thumb.Height)
                        thumb = new Rectangle(x, top, 1, y - top);
                    top = y + 1;
                }
            }
            return thumb;
        }

        // The game can draw several pale regions in this narrow strip. Follow contour
        // discovery order, with foreground connected diagonally and enclosed holes by edges.
        internal static int FindBar(bool[] mask, int width, int height, int minWidth, int minHeight)
        {
            var seen = new bool[mask.Length];
            var queue = new int[mask.Length];
            int result = -1;
            for (int start = 0; start < mask.Length; start++)
            {
                if (seen[start]) continue;
                bool pale = mask[start];
                int left = start % width, right = left, top = start / width, bottom = top;
                int count = 1;
                queue[0] = start;
                seen[start] = true;
                for (int index = 0; index < count; index++)
                {
                    int x = queue[index] % width, y = queue[index] / width;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                    for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                        for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                        {
                            if (!pale && nx != x && ny != y) continue;
                            int next = ny * width + nx;
                            if (seen[next] || mask[next] != pale) continue;
                            seen[next] = true;
                            queue[count++] = next;
                        }
                }
                if (!pale)
                {
                    if (left == 0 || top == 0 || right == width - 1 || bottom == height - 1) continue;
                    left--; top--; right++; bottom++;
                }
                int w = right - left + 1, h = bottom - top + 1;
                if (w < minWidth || h < minHeight) continue;
                int filled = 0;
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                        if (mask[y * width + x]) filled++;
                if (filled * 5 >= w * h * 4) result = top;
            }
            return result;
        }
    }
}
