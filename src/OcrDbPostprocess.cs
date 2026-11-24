// Behavior port of PaddlePaddle DBPostProcess and OnnxOCR 0.0.1 TextDetector.
// Copyright (c) 2020 PaddlePaddle Authors. Reference licensed Apache-2.0.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    // DBPostProcess / TextDetector from the reference onnxocr-ppocrv6 0.0.1.
    // Keep quadrilaterals through expansion and projection. The dependency-free
    // integer contour/rounded offset routines model RETR_LIST and JT_ROUND;
    // native OpenCV/PyClipper raster edge ties still require differential replay.
    internal static class OcrDbPostprocess
    {
        internal const float Threshold = .3f, BoxThreshold = .5f;
        internal const double UnclipRatio = 1.6;
        internal const int MaxCandidates = 1000;
        private static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        internal static List<PointF[]> Detect(Tensor<float> map, int destWidth, int destHeight)
        {
            var results = new List<PointF[]>();
            int[] dimensions = map.Dimensions.ToArray();
            if (dimensions.Length < 2 || destWidth <= 0 || destHeight <= 0) return results;
            int height = dimensions[dimensions.Length - 2], width = dimensions[dimensions.Length - 1];
            if (width <= 0 || height <= 0) return results;
            foreach (List<Point> contour in TraceContours(map, width, height))
            {
                PointF[] corners;
                if (!OcrDetectionBox.Fit(contour, out corners, 3)) continue;
                corners = MiniBoxOrder(corners);
                if (OcrDetectionBox.Score(map, width, height, corners) < BoxThreshold) continue;
                List<Point> expanded = Unclip(corners);
                if (!OcrDetectionBox.Fit(expanded, out corners, 5)) continue;
                PointF[] projected = ProjectAndFilter(MiniBoxOrder(corners), width, height, destWidth, destHeight);
                if (projected != null) results.Add(projected);
            }
            return results;
        }

        // Suzuki-style border following: outer and hole borders are both
        // discovered in raster order, then returned in reverse discovery order
        // (RETR_LIST). Store only the last 1000, as DB truncates before scoring.
        internal static List<List<Point>> TraceContours(Tensor<float> map, int width, int height)
        {
            int stride = width + 2;
            var labels = new int[checked(stride * (height + 2))];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (map.GetValue(y * width + x) > Threshold) labels[(y + 1) * stride + x + 1] = 1;

            var recent = new Queue<List<Point>>(MaxCandidates + 1);
            int border = 1;
            for (int y = 1; y <= height; y++)
            for (int x = 1; x <= width; x++)
            {
                int index = y * stride + x, value = labels[index];
                bool outer = value == 1 && labels[index - 1] == 0;
                bool hole = !outer && value >= 1 && labels[index + 1] == 0;
                if (!outer && !hole) continue;
                recent.Enqueue(FollowBorder(labels, stride, index, outer ? index - 1 : index + 1, ++border));
                if (recent.Count > MaxCandidates) recent.Dequeue();
            }
            var result = recent.ToList();
            result.Reverse();
            return result;
        }

        private static int Direction(int delta, int stride)
        {
            for (int d = 0; d < 8; d++) if (Dx[d] + Dy[d] * stride == delta) return d;
            throw new InvalidOperationException("Contour predecessor is not adjacent.");
        }

        private static int Neighbour(int current, int direction, int stride)
        {
            return current + Dx[direction] + Dy[direction] * stride;
        }

        private static List<Point> FollowBorder(int[] labels, int stride, int origin, int background, int border)
        {
            var contour = new List<Point>();
            int startDirection = Direction(background - origin, stride), first = -1;
            for (int step = 0; step < 8; step++)
            {
                int next = Neighbour(origin, (startDirection + step) % 8, stride);
                if (labels[next] != 0) { first = next; break; }
            }
            if (first < 0)
            {
                labels[origin] = -border;
                contour.Add(new Point(origin % stride - 1, origin / stride - 1));
                return contour;
            }

            int previous = first, current = origin, previousDirection = -1;
            // Each boundary directed edge is visited at most once before the
            // (origin,first) closing pair. The guard also bounds malformed maps.
            long guard = (long)labels.Length * 8;
            for (long step = 0; step < guard; step++)
            {
                int from = Direction(previous - current, stride), next = -1, direction = -1;
                bool rightExaminedAsBackground = false;
                for (int j = 1; j <= 8; j++)
                {
                    int d = (from + 8 - j) % 8;
                    int candidate = Neighbour(current, d, stride);
                    if (labels[candidate] != 0) { next = candidate; direction = d; break; }
                    if (d == 0) rightExaminedAsBackground = true;
                }
                if (next < 0) break;
                if (rightExaminedAsBackground) labels[current] = -border;
                else if (labels[current] == 1) labels[current] = border;

                // CHAIN_APPROX_SIMPLE retains changes of the 8-neighbour
                // direction, not all pixels along straight runs.
                if (direction != previousDirection)
                    contour.Add(new Point(current % stride - 1, current / stride - 1));
                previousDirection = direction;
                if (next == origin && current == first) break;
                previous = current;
                current = next;
            }
            return contour;
        }

        // DBPostProcess.get_mini_boxes orders left and right pairs by Y after
        // a stable X sort. This order is distinct from the final sum/diff order.
        internal static PointF[] MiniBoxOrder(PointF[] corners)
        {
            PointF[] p = corners.OrderBy(point => point.X).ToArray();
            int tl = p[1].Y > p[0].Y ? 0 : 1;
            int tr = p[3].Y > p[2].Y ? 2 : 3;
            return new[] { p[tl], p[tr], p[5 - tr], p[1 - tl] };
        }

        private static int ClipperRound(double value)
        {
            return (int)(value < 0 ? value - .5 : value + .5);
        }

        internal static List<Point> Unclip(PointF[] corners)
        {
            var expanded = new List<Point>();
            double area2 = 0, perimeter = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                PointF a = corners[i], b = corners[(i + 1) % corners.Length];
                area2 += (double)a.X * b.Y - (double)a.Y * b.X;
                double dx = b.X - a.X, dy = b.Y - a.Y;
                perimeter += Math.Sqrt(dx * dx + dy * dy);
            }
            double distance = perimeter > 0 ? Math.Abs(area2) * .5 * UnclipRatio / perimeter : 0;
            if (distance <= 0) return expanded;

            // PyClipper's IntPoint path consumes integer coordinates, while
            // Shapely computes the offset distance from the original float box.
            var path = new List<Point>();
            foreach (PointF corner in corners)
            {
                Point point = Point.Truncate(corner);
                if (path.Count == 0 || path[path.Count - 1] != point) path.Add(point);
            }
            if (path.Count > 1 && path[0] == path[path.Count - 1]) path.RemoveAt(path.Count - 1);
            if (path.Count < 3) return expanded;

            long orientation = 0;
            for (int i = 0; i < path.Count; i++)
            {
                Point a = path[i], b = path[(i + 1) % path.Count];
                orientation += (long)a.X * b.Y - (long)a.Y * b.X;
            }
            if (orientation < 0) path.Reverse();

            // ClipperOffset JT_ROUND default ArcTolerance=0.25, stepping an
            // integer rounded arc along the outward normals of the rectangle.
            double tolerance = Math.Min(.25, distance * .25);
            double fullSteps = Math.PI / Math.Acos(1 - tolerance / distance);
            double radians = 2 * Math.PI / fullSteps;
            double sin = Math.Sin(radians), cos = Math.Cos(radians);
            int count = path.Count;
            var nx = new double[count];
            var ny = new double[count];
            for (int i = 0; i < count; i++)
            {
                Point a = path[i], b = path[(i + 1) % count];
                double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 0) return expanded;
                nx[i] = dy / length;
                ny[i] = -dx / length;
            }
            for (int i = 0; i < count; i++)
            {
                int previous = (i + count - 1) % count;
                Point point = path[i];
                double x = nx[previous], y = ny[previous];
                double cross = x * ny[i] - nx[i] * y;
                double dot = x * nx[i] + y * ny[i];
                if (Math.Abs(cross * distance) < 1 && dot > 0)
                {
                    expanded.Add(new Point(ClipperRound(point.X + x * distance), ClipperRound(point.Y + y * distance)));
                    continue;
                }
                double angle = Math.Atan2(cross, dot);
                int steps = Math.Max(1, ClipperRound(fullSteps / (2 * Math.PI) * Math.Abs(angle)));
                for (int step = 0; step < steps; step++)
                {
                    expanded.Add(new Point(ClipperRound(point.X + x * distance), ClipperRound(point.Y + y * distance)));
                    double oldX = x;
                    x = x * cos - sin * y;
                    y = oldX * sin + y * cos;
                }
                expanded.Add(new Point(ClipperRound(point.X + nx[i] * distance), ClipperRound(point.Y + ny[i] * distance)));
            }
            return expanded;
        }

        // np.round and clip[0,dest] in DB, then TextDetector.order_points_clockwise
        // and clip[0,dest-1] in filter_tag_det_res. The final integer Euclidean
        // width/height both have to exceed 3 in the original ROI.
        internal static PointF[] ProjectAndFilter(PointF[] corners, int width, int height, int destWidth, int destHeight)
        {
            if (corners == null || corners.Length != 4 || width <= 0 || height <= 0 || destWidth <= 0 || destHeight <= 0)
                return null;
            var projected = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                float x = corners[i].X / width * destWidth, y = corners[i].Y / height * destHeight;
                projected[i] = new PointF((int)Math.Max(0, Math.Min(destWidth, Math.Round(x))),
                    (int)Math.Max(0, Math.Min(destHeight, Math.Round(y))));
            }
            int min = 0, max = 0;
            for (int i = 1; i < projected.Length; i++)
            {
                float sum = projected[i].X + projected[i].Y;
                if (sum < projected[min].X + projected[min].Y) min = i;
                if (sum > projected[max].X + projected[max].Y) max = i;
            }
            if (min == max) return null;
            int right = -1, left = -1;
            for (int i = 0; i < projected.Length; i++)
            {
                if (i == min || i == max) continue;
                float difference = projected[i].Y - projected[i].X;
                if (right < 0 || difference < projected[right].Y - projected[right].X) right = i;
                if (left < 0 || difference > projected[left].Y - projected[left].X) left = i;
            }
            var ordered = new[] { projected[min], projected[right], projected[max], projected[left] };
            for (int i = 0; i < ordered.Length; i++)
                ordered[i] = new PointF(Math.Max(0, Math.Min(destWidth - 1, ordered[i].X)),
                    Math.Max(0, Math.Min(destHeight - 1, ordered[i].Y)));
            return (int)Length(ordered[0], ordered[1]) > 3 && (int)Length(ordered[0], ordered[3]) > 3 ? ordered : null;
        }

        private static double Length(PointF a, PointF b)
        {
            double x = a.X - b.X, y = a.Y - b.Y;
            return Math.Sqrt(x * x + y * y);
        }
    }
}
