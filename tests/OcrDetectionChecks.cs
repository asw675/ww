using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal static class OcrDetectionChecks
    {
        internal static void Run(Action<string, Action> check)
        {
            check("DB projects the expanded quadrilateral and uses actual map shape", ExpandedProjection);
            check("DB thresholds, minimum sides, and original-image clip agree with the reference", Thresholds);
            check("DB scans outer and hole contours in RETR_LIST order", ContourList);
            check("DB limits candidates before score/size filtering", CandidateLimit);
            check("DB rounded unclip retains a rotated rectangle", RotatedUnclip);
            check("DB scoring fills clipped ROI polygons including integer scanline interiors", ClippedMask);
            check("DB rotating rectangle area agrees with an independent exhaustive geometric oracle", GeometryArea);
            check("DB 8-connected outer borders and 4-connected holes preserve contour topology", ContourTopology);
        }

        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private static DenseTensor<float> MakeMap(int width, int height, params Rectangle[] rectangles)
        {
            float[] values = new float[width * height];
            foreach (Rectangle box in rectangles) Paint(values, width, box, .9f);
            return new DenseTensor<float>(values, new[] { 1, 1, height, width });
        }

        private static void Paint(float[] values, int width, Rectangle box, float value)
        {
            for (int y = box.Top; y < box.Bottom; y++) for (int x = box.Left; x < box.Right; x++)
                values[y * width + x] = value;
        }

        private static void ExpandedProjection()
        {
            var map = MakeMap(64, 32, new Rectangle(10, 8, 8, 5));
            List<PointF[]> boxes = OcrDbPostprocess.Detect(map, 128, 64);
            var expected = new[] { new PointF(16, 12), new PointF(38, 12), new PointF(38, 28), new PointF(16, 28) };
            Require(boxes.Count == 1 && boxes[0].SequenceEqual(expected), "expected projected TL,TR,BR,BL from the expanded 7x4 map rectangle: " +
                string.Join(";", boxes.Select(points => string.Join(",", points.Select(p => p.X + ":" + p.Y)))));

            // Preprocessor shape_list retains the unpadded original dimensions.
            Require(OcrDbPostprocess.Detect(map, 10, 5).Count == 0,
                "small original ROI must be projected and then rejected by original-coordinate min side");
        }

        private static void Thresholds()
        {
            const int width = 64, height = 32;
            var values = new float[width * height];
            Paint(values, width, new Rectangle(10, 10, 12, 6), .3f);
            var map = new DenseTensor<float>(values, new[] { 1, height, width });
            Require(OcrDbPostprocess.Detect(map, width, height).Count == 0, "bitmap uses > .3, not >= .3");
            Paint(values, width, new Rectangle(10, 10, 12, 6), .49f);
            map = new DenseTensor<float>(values, new[] { 1, 1, height, width });
            Require(OcrDbPostprocess.Detect(map, width, height).Count == 0, "full polygon mean below .5 is rejected");
            Paint(values, width, new Rectangle(10, 10, 12, 6), .5f);
            map = new DenseTensor<float>(values, new[] { 1, 1, height, width });
            Require(OcrDbPostprocess.Detect(map, width, height).Count == 1, ".5 score is inclusive");

            Require(OcrDbPostprocess.Detect(MakeMap(width, height, new Rectangle(8, 7, 12, 3)), width, height).Count == 0,
                "pre-clip short side < 3 is rejected");

            PointF[] clipped = OcrDbPostprocess.ProjectAndFilter(new[] { new PointF(-2, -3),
                new PointF(64, -3), new PointF(64, 32), new PointF(-2, 32) }, width, height, width, height);
            Require(clipped != null && clipped.SequenceEqual(new[] { new PointF(0, 0), new PointF(63, 0), new PointF(63, 31), new PointF(0, 31) }),
                "DB first clips to dest, then TextDetector clips to width-1/height-1");
            Require(OcrDbPostprocess.ProjectAndFilter(new[] { new PointF(0, 0), new PointF(3, 0),
                new PointF(3, 20), new PointF(0, 20) }, width, height, width, height) == null,
                "the final int(norm) dimensions must exceed 3, not merely equal 3");
            PointF[] ties = OcrDbPostprocess.ProjectAndFilter(new[] { new PointF(2.5f, 4.5f), new PointF(12.5f, 4.5f),
                new PointF(12.5f, 16.5f), new PointF(2.5f, 16.5f) }, width, height, width, height);
            Require(ties != null && ties[0] == new PointF(2, 4) && ties[2] == new PointF(12, 16),
                "np.round uses ties to even, before corner sorting");
        }

        private static void ContourList()
        {
            const int width = 64, height = 32;
            var values = new float[width * height];
            Paint(values, width, new Rectangle(3, 3, 17, 17), .9f);
            Paint(values, width, new Rectangle(8, 8, 7, 7), 0);
            Paint(values, width, new Rectangle(40, 21, 14, 7), .9f);
            var map = new DenseTensor<float>(values, new[] { 1, 1, height, width });
            List<List<Point>> contours = OcrDbPostprocess.TraceContours(map, width, height);
            Require(contours.Count == 3 && contours[0].Min(p => p.Y) == 21 &&
                contours[1].Min(p => p.Y) == 7 && contours[2].Min(p => p.Y) == 3,
                "RETR_LIST reverses raster discovery, including the nested hole border; actual minima=" +
                string.Join(",", contours.Select(c => c.Min(p => p.Y))));
        }

        private static void CandidateLimit()
        {
            const int width = 96, height = 96;
            var values = new float[width * height];
            Paint(values, width, new Rectangle(4, 2, 25, 6), .9f);
            for (int y = 18; y < height - 1; y += 2)
            for (int x = 1; x < width - 1; x += 2)
                values[y * width + x] = .9f;
            var map = new DenseTensor<float>(values, new[] { 1, 1, height, width });
            Require(OcrDbPostprocess.TraceContours(map, width, height).Count == 1000 &&
                OcrDbPostprocess.Detect(map, width, height).Count == 0,
                "the first 1000 returned candidate contours are limited before small/noisy boxes are filtered");
        }

        private static void RotatedUnclip()
        {
            PointF[] box = { new PointF(18, 7), new PointF(34, 19), new PointF(28, 27), new PointF(12, 15) };
            List<Point> expanded = OcrDbPostprocess.Unclip(box);
            PointF[] fitted = null;
            Require(expanded.Count > 4 && OcrDetectionBox.Fit(expanded, out fitted, 5), "rounded unclip must create a nondegenerate expanded polygon");
            fitted = OcrDbPostprocess.MiniBoxOrder(fitted);
            Require(Math.Abs(fitted[0].Y - fitted[1].Y) > 5 && Math.Abs(fitted[0].X - fitted[3].X) > 3,
                "expanded text region must remain rotated instead of collapsing to an axis-aligned hull");
        }

        private static void ClippedMask()
        {
            const int width = 10, height = 9;
            var values = new float[width * height];
            // This clipped slanted box has inclusive row spans at y=3..8:
            // [4,4], [3,5], [2,6], [2,7], [1,8], [0,9]. The unbounded-edge
            // Bresenham min/max approach missed (2,5),(0,8) and overincluded
            // the last pixels of three rows. Verify the 33-pixel filled mask.
            values[5 * width + 2] = values[8 * width] = 1;
            values[5 * width + 7] = values[6 * width + 8] = values[7 * width + 9] = 1;
            var map = new DenseTensor<float>(values, new[] { 1, 1, height, width });
            PointF[] q = { new PointF(10, 8), new PointF(5, 14), new PointF(0, 9), new PointF(4, 3) };
            Require(Math.Abs(OcrDetectionBox.Score(map, width, height, q) - 2.0 / 33) < 1e-9,
                "clipped LINE_8 border plus fixed-point interior must score the local polygon mask, not the extended edge runs");
            var full = MakeMap(width, height, new Rectangle(0, 0, width, height));
            PointF[] spanning = { new PointF(-10, -10), new PointF(20, -10), new PointF(20, 20), new PointF(-10, 20) };
            Require(Math.Abs(OcrDetectionBox.Score(full, width, height, spanning) - .9) < 1e-6,
                "a convex polygon covering the full ROI must fill it even when all raster edges lie outside");
        }

        private static double OracleArea(List<Point> points)
        {
            double best = double.PositiveInfinity;
            // The minimum rectangle of the convex hull has an edge collinear
            // with some point pair. Exhaust all pairs independently of Fit's
            // rotating support-line implementation.
            foreach (Point a in points)
                foreach (Point b in points)
                {
                    double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
                    if (length == 0) continue;
                    double loU = double.PositiveInfinity, loV = loU, hiU = double.NegativeInfinity, hiV = hiU;
                    foreach (Point p in points)
                    {
                        double u = (p.X * dx + p.Y * dy) / length, v = (-p.X * dy + p.Y * dx) / length;
                        loU = Math.Min(loU, u); hiU = Math.Max(hiU, u);
                        loV = Math.Min(loV, v); hiV = Math.Max(hiV, v);
                    }
                    best = Math.Min(best, (hiU - loU) * (hiV - loV));
                }
            return best;
        }

        private static void GeometryArea()
        {
            var random = new Random(774);
            for (int i = 0; i < 300; i++)
            {
                var points = new List<Point>();
                int count = random.Next(4, 18), x = random.Next(0, 950), y = random.Next(0, 950);
                for (int j = 0; j < count; j++) points.Add(new Point(x + random.Next(0, 40), y + random.Next(0, 40)));
                double expected = OracleArea(points);
                if (expected <= 1e-6 || double.IsInfinity(expected)) continue;
                PointF[] q;
                Require(OcrDetectionBox.Fit(points, out q, 0), "a nondegenerate convex hull must yield a rotated rectangle");
                double dx = q[1].X - q[0].X, dy = q[1].Y - q[0].Y, ex = q[3].X - q[0].X, ey = q[3].Y - q[0].Y;
                Require(Math.Abs(Math.Abs(dx * ey - dy * ex) - expected) < .05,
                    "rotating support-line geometry must match independently enumerated minimum rectangle area");
            }
        }

        private static int Components(float[] values, int width, int height, bool foreground, bool diagonals)
        {
            var seen = new bool[values.Length];
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (seen[i] || (values[i] > .3f) != foreground) continue;
                bool touchesBorder = false;
                var queue = new Queue<int>();
                queue.Enqueue(i); seen[i] = true;
                while (queue.Count > 0)
                {
                    int at = queue.Dequeue(), x = at % width, y = at / width;
                    touchesBorder |= x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0 || !diagonals && dx != 0 && dy != 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int next = ny * width + nx;
                            if (seen[next] || (values[next] > .3f) != foreground) continue;
                            seen[next] = true; queue.Enqueue(next);
                        }
                }
                if (foreground || !touchesBorder) count++;
            }
            return count;
        }

        private static void ContourTopology()
        {
            const int width = 18, height = 16;
            var random = new Random(24);
            for (int test = 0; test < 300; test++)
            {
                var values = new float[width * height];
                double density = random.NextDouble();
                for (int i = 0; i < values.Length; i++) values[i] = random.NextDouble() < density ? .9f : 0;
                int expected = Components(values, width, height, true, true) + Components(values, width, height, false, false);
                var map = new DenseTensor<float>(values, new[] { 1, 1, height, width });
                Require(OcrDbPostprocess.TraceContours(map, width, height).Count == expected,
                    "all 8-connected outer foreground components and enclosed 4-connected background holes must be returned");
            }
        }
    }
}
