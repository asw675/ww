// Independent C# geometry for the OnnxOCR 0.0.1 DBPostProcess behavior.
// Reference: Copyright (c) 2020 PaddlePaddle Authors, Apache-2.0.
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

        internal static bool Fit(List<Point> pixels, out PointF[] corners, double minimumShortSide = 3)
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
            if (Math.Min(bestWidth, bestHeight) < minimumShortSide) return false;
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
            int x0 = Math.Max(0, (int)Math.Floor(corners.Min(p => p.X)));
            int x1 = Math.Min(width - 1, (int)Math.Ceiling(corners.Max(p => p.X)));
            int y0 = Math.Max(0, (int)Math.Floor(corners.Min(p => p.Y)));
            int y1 = Math.Min(height - 1, (int)Math.Ceiling(corners.Max(p => p.Y)));
            if (x0 > x1 || y0 > y1) return 0;
            int maskWidth = x1 - x0 + 1, maskHeight = y1 - y0 + 1;
            var left = Enumerable.Repeat(maskWidth, maskHeight).ToArray();
            var right = Enumerable.Repeat(-1, left.Length).ToArray();

            // DB.box_score_fast first translates the float quad to a local
            // clipped ROI, then casts to int32 and calls cv2.fillPoly. Model
            // the filled convex polygon AND its LINE_8 border; merely tracing
            // the unbounded edges and taking their min/max does not reproduce
            // clipped borders or the integer interior at subpixel crossings.
            var points = corners.Select(p => new Point((int)(p.X - x0), (int)(p.Y - y0))).ToArray();
            var minX = Enumerable.Repeat(long.MaxValue, maskHeight).ToArray();
            var maxX = Enumerable.Repeat(long.MinValue, maskHeight).ToArray();
            const int shift = 16;
            for (int i = 0; i < corners.Length; i++)
            {
                Point a = points[i], b = points[(i + 1) % points.Length];
                Point top = a.Y <= b.Y ? a : b, bottom = a.Y <= b.Y ? b : a;
                if (top.Y != bottom.Y)
                {
                    long step = ((long)(bottom.X - top.X) << shift) / (bottom.Y - top.Y);
                    int begin = Math.Max(0, top.Y), end = Math.Min(maskHeight, bottom.Y);
                    long edgeX = ((long)top.X << shift) + (begin - (long)top.Y) * step;
                    for (int rowIndex = begin; rowIndex < end; rowIndex++, edgeX += step)
                    {
                        if (edgeX < minX[rowIndex]) minX[rowIndex] = edgeX;
                        if (edgeX > maxX[rowIndex]) maxX[rowIndex] = edgeX;
                    }
                }
                if (!ClipBorder(ref a, ref b, maskWidth, maskHeight)) continue;
                if (a.X > b.X) { Point swap = a; a = b; b = swap; }
                int dx = b.X - a.X, dy = Math.Abs(b.Y - a.Y), direction = Math.Sign(b.Y - a.Y);
                int major = Math.Max(dx, dy), minor = Math.Min(dx, dy), error = major / 2;
                int x = a.X, y = a.Y;
                for (int j = 0; j <= major; j++)
                {
                    if (y >= 0 && y < maskHeight)
                    {
                        left[y] = Math.Min(left[y], x);
                        right[y] = Math.Max(right[y], x);
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
            {
                if (minX[row] != long.MaxValue && maxX[row] != long.MinValue)
                {
                    int interiorLeft = (int)((minX[row] + (1L << shift) - 1) >> shift);
                    int interiorRight = (int)(maxX[row] >> shift);
                    if (interiorLeft <= interiorRight)
                    {
                        left[row] = Math.Min(left[row], interiorLeft);
                        right[row] = Math.Max(right[row], interiorRight);
                    }
                }
                for (int x = Math.Max(0, left[row]); x <= Math.Min(maskWidth - 1, right[row]); x++)
                {
                    sum += map.GetValue((y0 + row) * width + x0 + x);
                    count++;
                }
            }
            return count == 0 ? 0 : sum / count;
        }

        private static int BorderCode(long x, long y, int width, int height)
        {
            return (x < 0 ? 1 : x >= width ? 2 : 0) | (y < 0 ? 4 : y >= height ? 8 : 0);
        }

        // Clip the polygon's raster boundary to the mask before drawing it.
        // The integer quotients match the endpoint truncation of a clipped
        // LINE_8 segment. This does not alter the independent interior fill.
        private static bool ClipBorder(ref Point a, ref Point b, int width, int height)
        {
            long ax = a.X, ay = a.Y, bx = b.X, by = b.Y;
            int ca = BorderCode(ax, ay, width, height), cb = BorderCode(bx, by, width, height);
            if ((ca & cb) != 0) return false;
            if ((ca | cb) != 0)
            {
                if ((ca & 12) != 0)
                {
                    long y = ay < 0 ? 0 : height - 1;
                    ax += (y - ay) * (bx - ax) / (by - ay);
                    ay = y;
                    ca = BorderCode(ax, ay, width, height);
                }
                if ((cb & 12) != 0)
                {
                    long y = by < 0 ? 0 : height - 1;
                    bx += (y - by) * (bx - ax) / (by - ay);
                    by = y;
                    cb = BorderCode(bx, by, width, height);
                }
                if ((ca & cb) != 0) return false;
                if (ca != 0)
                {
                    long x = ax < 0 ? 0 : width - 1;
                    ay += (x - ax) * (by - ay) / (bx - ax);
                    ax = x;
                }
                if (cb != 0)
                {
                    long x = bx < 0 ? 0 : width - 1;
                    by += (x - bx) * (by - ay) / (bx - ax);
                    bx = x;
                }
            }
            if ((BorderCode(ax, ay, width, height) | BorderCode(bx, by, width, height)) != 0) return false;
            a = new Point((int)ax, (int)ay);
            b = new Point((int)bx, (int)by);
            return true;
        }
    }
}
