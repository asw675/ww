using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal static class OcrDetectionBox
    {
        private static long Cross(Point a, Point b, Point c)
        {
            return (long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X);
        }

        private static List<Point> Hull(List<Point> pixels)
        {
            int top = pixels.Min(p => p.Y), bottom = pixels.Max(p => p.Y);
            var min = Enumerable.Repeat(int.MaxValue, bottom - top + 1).ToArray();
            var max = Enumerable.Repeat(int.MinValue, min.Length).ToArray();
            foreach (Point p in pixels)
            {
                min[p.Y - top] = Math.Min(min[p.Y - top], p.X);
                max[p.Y - top] = Math.Max(max[p.Y - top], p.X);
            }
            var points = new List<Point>();
            for (int row = 0; row < min.Length; row++)
            {
                if (min[row] > max[row]) continue;
                points.Add(new Point(min[row], row + top));
                if (min[row] != max[row]) points.Add(new Point(max[row], row + top));
            }
            points.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            var hull = new List<Point>();
            foreach (Point point in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                Point point = points[i];
                while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static Point SideDirection(Point edge, int side)
        {
            switch (side)
            {
                case 1: return new Point(edge.Y, -edge.X);
                case 2: return new Point(-edge.X, -edge.Y);
                case 3: return new Point(-edge.Y, edge.X);
                default: return edge;
            }
        }

        internal static bool Fit(List<Point> pixels, out PointF[] corners)
        {
            corners = null;
            if (pixels.Count < 3) return false;
            List<Point> hull = Hull(pixels);
            if (hull.Count < 3) return false;
            int count = hull.Count;
            var edges = new Point[count];
            int topLeft = 0;
            for (int i = 0; i < count; i++)
            {
                Point p = hull[i], q = hull[(i + 1) % count];
                edges[i] = new Point(q.X - p.X, q.Y - p.Y);
                if (p.Y < hull[topLeft].Y || (p.Y == hull[topLeft].Y && p.X < hull[topLeft].X)) topLeft = i;
            }
            // Start the supporting lines in the same order as an outer contour traced from its top row.
            int firstSupport = (topLeft + 1) % count;
            var support = Enumerable.Repeat(firstSupport, 4).ToArray();
            for (int step = 1; step < count; step++)
            {
                int i = (firstSupport + step) % count;
                if (hull[i].Y < hull[support[0]].Y) support[0] = i;
                if (hull[i].X > hull[support[1]].X) support[1] = i;
                if (hull[i].Y > hull[support[2]].Y) support[2] = i;
                if (hull[i].X < hull[support[3]].X) support[3] = i;
            }
            float best = float.PositiveInfinity, bestWidth = 0, bestHeight = 0, angle = 0, cx = 0, cy = 0;
            for (int step = 0; step < count; step++)
            {
                int next = 0;
                Point direction = edges[support[0]];
                for (int side = 1; side < 4; side++)
                {
                    Point candidate = SideDirection(edges[support[side]], side);
                    if ((long)candidate.X * direction.Y > (long)candidate.Y * direction.X)
                    {
                        direction = candidate;
                        next = side;
                    }
                }
                support[next] = (support[next] + 1) % count;
                float inverseLength = (float)(1 / Math.Sqrt((double)direction.X * direction.X + (double)direction.Y * direction.Y));
                float a = direction.X * inverseLength, b = direction.Y * inverseLength;
                Point top = hull[support[0]], right = hull[support[1]], bottom = hull[support[2]], left = hull[support[3]];
                float width = a * (right.X - left.X) + b * (right.Y - left.Y);
                float height = a * (bottom.Y - top.Y) - b * (bottom.X - top.X);
                float area = width * height;
                if (area > best) continue;
                best = area;
                float minU = a * left.X + b * left.Y, minV = a * top.Y - b * top.X;
                float inverseDet = 1f / (a * a + b * b);
                float ux = a * width, uy = b * width, vx = -b * height, vy = a * height;
                cx = (minU * a - b * minV) * inverseDet + (ux + vx) * .5f;
                cy = (a * minV + minU * b) * inverseDet + (uy + vy) * .5f;
                bestWidth = (float)Math.Sqrt((double)ux * ux + (double)uy * uy);
                bestHeight = (float)Math.Sqrt((double)vx * vx + (double)vy * vy);
                angle = (float)((float)Math.Atan2(uy, ux) * 180 / Math.PI);
            }
            if (Math.Min(bestWidth, bestHeight) < 3) return false;
            double radians = angle * (Math.PI / 180);
            float cos = (float)Math.Cos(radians) * .5f, sin = (float)Math.Sin(radians) * .5f;
            PointF first = new PointF(cx - sin * bestHeight - cos * bestWidth, cy + cos * bestHeight - sin * bestWidth);
            PointF second = new PointF(cx + sin * bestHeight - cos * bestWidth, cy - cos * bestHeight - sin * bestWidth);
            corners = new[] { first, second, new PointF(2 * cx - first.X, 2 * cy - first.Y),
                new PointF(2 * cx - second.X, 2 * cy - second.Y) };
            return true;
        }

        internal static double Score(Tensor<float> map, int width, int height, PointF[] corners)
        {
            int y0 = Math.Max(0, (int)Math.Floor(corners.Min(p => p.Y)));
            int y1 = Math.Min(height - 1, (int)Math.Ceiling(corners.Max(p => p.Y)));
            if (y0 > y1) return 0;
            var left = Enumerable.Repeat(width, y1 - y0 + 1).ToArray();
            var right = Enumerable.Repeat(-1, left.Length).ToArray();
            for (int i = 0; i < corners.Length; i++)
            {
                Point a = Point.Truncate(corners[i]), b = Point.Truncate(corners[(i + 1) % corners.Length]);
                if (a.X > b.X) { Point swap = a; a = b; b = swap; }
                int dx = b.X - a.X, dy = Math.Abs(b.Y - a.Y), direction = Math.Sign(b.Y - a.Y);
                int major = Math.Max(dx, dy), minor = Math.Min(dx, dy), error = major / 2;
                int x = a.X, y = a.Y;
                for (int j = 0; j <= major; j++)
                {
                    if (y >= y0 && y <= y1)
                    {
                        left[y - y0] = Math.Min(left[y - y0], x);
                        right[y - y0] = Math.Max(right[y - y0], x);
                    }
                    if (dx >= dy) x++; else y += direction;
                    error -= minor;
                    if (error < 0)
                    {
                        if (dx >= dy) y += direction; else x++;
                        error += major;
                    }
                }
            }
            double sum = 0;
            int count = 0;
            for (int row = 0; row < left.Length; row++)
                for (int x = Math.Max(0, left[row]); x <= Math.Min(width - 1, right[row]); x++)
                {
                    sum += map.GetValue((y0 + row) * width + x);
                    count++;
                }
            return count == 0 ? 0 : sum / count;
        }
    }
}
