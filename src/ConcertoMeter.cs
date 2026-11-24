using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace TideCombat
{
    internal enum ConcertoColor { Unknown = -1, Spectro, Electro, Fusion, Glacio, Aero, Havoc }

    internal sealed class ConcertoMeter
    {
        private static readonly byte[,] Ranges =
        {
            { 205, 235, 190, 222, 90, 130 }, { 150, 190, 95, 140, 210, 249 },
            { 200, 230, 100, 130, 75, 105 }, { 60, 95, 150, 180, 210, 245 },
            { 70, 110, 215, 250, 155, 190 }, { 190, 220, 65, 105, 145, 175 }
        };

        private readonly double[] fullAreas = new double[6];
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(double[]));
        private readonly string path;
        private bool changed;
        private readonly int[] colorCounts = new int[6];
        private byte[] marks, raw, dilated, closed;
        private int[] labels, queue;
        private int width, height;

        internal ConcertoMeter(string path = null)
        {
            // The earlier calibration stored values normalized to 2160px. A
            // separate file prevents interpreting those values as raw pixels.
            this.path = path == null ? null : Path.ChangeExtension(path, ".raw.json");
            if (this.path == null || !File.Exists(this.path)) return;
            try
            {
                using (Stream stream = File.OpenRead(this.path))
                {
                    var values = (double[])Serializer.ReadObject(stream);
                    if (values == null || values.Length != fullAreas.Length) return;
                    for (int i = 0; i < fullAreas.Length; i++)
                        if (values[i] > 0) fullAreas[i] = values[i];
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
        }

        internal void Save()
        {
            if (!changed || path == null) return;
            try
            {
                SettingsFile.WriteAtomic(path, stream => Serializer.WriteObject(stream, fullAreas));
                changed = false;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool Matches(int color, int r, int g, int b)
        {
            return r >= Ranges[color, 0] && r <= Ranges[color, 1] &&
                g >= Ranges[color, 2] && g <= Ranges[color, 3] && b >= Ranges[color, 4] && b <= Ranges[color, 5];
        }

        internal double Read(PixelFrame frame, ConcertoColor expected, out ConcertoColor observed)
        {
            observed = ConcertoColor.Unknown;
            FramePatch patch = frame.Concerto;
            if (patch == null || patch.Width < 5 || patch.Height < 5) return 0;
            width = patch.Width;
            height = patch.Height;
            Prepare(width * height);
            Array.Clear(colorCounts, 0, colorCounts.Length);
            int expectedIndex = (int)expected;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int r, g, b;
                patch.Rgb(x, y, out r, out g, out b);
                byte color = 0;
                if (expectedIndex >= 0)
                {
                    if (Matches(expectedIndex, r, g, b)) color = (byte)(expectedIndex + 1);
                }
                else for (int i = 0; i < colorCounts.Length; i++)
                {
                    if (!Matches(i, r, g, b)) continue;
                    colorCounts[i]++;
                    color = (byte)(i + 1);
                    break;
                }
                marks[y * width + x] = color;
            }
            int target = expectedIndex < 0 ? 0 : expectedIndex;
            if (expectedIndex < 0)
                for (int i = 1; i < colorCounts.Length; i++)
                    if (colorCounts[i] > colorCounts[target]) target = i;
            observed = (ConcertoColor)target;

            int centerX = width / 2, centerY = height / 2;
            int inner = (int)(height * .35119), outer = (int)Math.Ceiling(height * .42261);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int dx = x - centerX, dy = y - centerY;
                int square = dx * dx + dy * dy, index = y * width + x;
                raw[index] = (byte)(square > inner * inner && square <= outer * outer && marks[index] == target + 1 ? 1 : 0);
            }
            CloseGaps(centerX, centerY);
            int selectedLabel;
            double potentialArea = 1500.0 * frame.SourceWidth * frame.SourceHeight / (3840.0 * 2160);
            int area = RingComponent(potentialArea, out selectedLabel);
            if (area == 0) return 0;

            return CalibrateRaw((ConcertoColor)target, area, IsReferenceFullRing(width, height, labels, selectedLabel));
        }

        internal double CalibrateRaw(ConcertoColor color, int area, bool full)
        {
            int target = (int)color;
            if (target < 0 || target >= fullAreas.Length) return 0;
            if (full && area > 0)
            {
                changed |= fullAreas[target] != area;
                fullAreas[target] = area;
                return 1;
            }
            return fullAreas[target] <= 0 ? 0 : Math.Min(.99, area / fullAreas[target]);
        }

        private void Prepare(int size)
        {
            if (marks != null && marks.Length == size) return;
            marks = new byte[size];
            raw = new byte[size];
            dilated = new byte[size];
            closed = new byte[size];
            labels = new int[size];
            queue = new int[size];
        }

        private void CloseGaps(int centerX, int centerY)
        {
            Array.Clear(dilated, 0, dilated.Length);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (raw[y * width + x] == 0) continue;
                for (int dy = Math.Max(0, y - 1); dy <= Math.Min(height - 1, y + 1); dy++)
                for (int dx = Math.Max(0, x - 1); dx <= Math.Min(width - 1, x + 1); dx++)
                    dilated[dy * width + dx] = 1;
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (Math.Abs(y - centerY) <= 1 && x > centerX)
                {
                    closed[index] = raw[index];
                    continue;
                }
                byte value = 1;
                for (int dy = Math.Max(0, y - 1); dy <= Math.Min(height - 1, y + 1) && value != 0; dy++)
                for (int dx = Math.Max(0, x - 1); dx <= Math.Min(width - 1, x + 1); dx++)
                    value &= dilated[dy * width + dx];
                closed[index] = value;
            }
        }

        private int RingComponent(double minBounds, out int selectedLabel)
        {
            Array.Clear(labels, 0, labels.Length);
            selectedLabel = 0;
            int area = 0, label = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                if (closed[i] == 0 || labels[i] != 0) continue;
                int tail = 1, left = width, right = 0, top = height, bottom = 0;
                queue[0] = i;
                labels[i] = ++label;
                for (int head = 0; head < tail; head++)
                {
                    int cell = queue[head], x = cell % width, y = cell / width;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                    for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                    {
                        int near = ny * width + nx;
                        if (closed[near] == 0 || labels[near] != 0) continue;
                        labels[near] = label;
                        queue[tail++] = near;
                    }
                }
                if ((right - left + 1) * (bottom - top + 1) < minBounds) continue;
                if (selectedLabel != 0) return 0;
                selectedLabel = label;
                area = tail;
            }
            return area;
        }

        private static readonly int[] ContourDx = { -1, -1, 0, 1, 1, 1, 0, -1 };
        private static readonly int[] ContourDy = { 0, -1, -1, -1, 0, 1, 1, 1 };

        // RETR_EXTERNAL + CHAIN_APPROX_SIMPLE / approxPolyDP(0.05 * perimeter)
        // in the reference. Trace the exterior boundary in the same pixel
        // coordinate space; unlike flood-filling the center, this tests the
        // approximated contour for convexity and at least four vertices.
        internal static bool IsReferenceFullRing(int width, int height, int[] components, int selected)
        {
            if (selected <= 0) return false;
            int first = Array.IndexOf(components, selected);
            if (first < 0) return false;
            var contour = new List<Point>();
            var origin = new Point(first % width, first / width);
            var point = origin;
            var previous = new Point(origin.X - 1, origin.Y);
            Point firstNext = Point.Empty;
            bool haveFirst = false, closedContour = false;
            for (int steps = 0; steps <= components.Length * 8; steps++)
            {
                int scan = 0;
                while (scan < 7 && (point.X + ContourDx[scan] != previous.X || point.Y + ContourDy[scan] != previous.Y)) scan++;
                int edge = -1;
                for (int k = 0; k < 8; k++)
                {
                    int direction = (scan + k) % 8;
                    int x = point.X + ContourDx[direction], y = point.Y + ContourDy[direction];
                    if (x >= 0 && x < width && y >= 0 && y < height && components[y * width + x] == selected)
                    { edge = direction; break; }
                }
                if (edge < 0) return false;
                var next = new Point(point.X + ContourDx[edge], point.Y + ContourDy[edge]);
                if (!haveFirst) { firstNext = next; haveFirst = true; }
                else if (point == origin && next == firstNext) { closedContour = true; break; }
                contour.Add(point);
                int preceding = (edge + 7) % 8;
                previous = new Point(point.X + ContourDx[preceding], point.Y + ContourDy[preceding]);
                point = next;
            }
            if (!closedContour || contour.Count < 4) return false;

            double perimeter = 0;
            for (int i = 0; i < contour.Count; i++)
                perimeter += Math.Sqrt(SquaredDistance(contour[i], contour[(i + 1) % contour.Count]));
            int a = 0, b = 0;
            double farthest = 0;
            for (int i = 0; i < contour.Count; i++)
                for (int j = i + 1; j < contour.Count; j++)
                {
                    double distance = SquaredDistance(contour[i], contour[j]);
                    if (distance > farthest) { farthest = distance; a = i; b = j; }
                }
            double epsilon = .05 * perimeter;
            var polygon = new List<Point>();
            Approximate(contour, a, b, epsilon, polygon);
            Approximate(contour, b, a + contour.Count, epsilon, polygon);
            if (polygon.Count < 4) return false;
            int winding = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point p = polygon[i], q = polygon[(i + 1) % polygon.Count], r = polygon[(i + 2) % polygon.Count];
                long cross = (long)(q.X - p.X) * (r.Y - q.Y) - (long)(q.Y - p.Y) * (r.X - q.X);
                int sign = Math.Sign(cross);
                if (sign == 0 || winding != 0 && winding != sign) return false;
                winding = sign;
            }
            return winding != 0;
        }

        private static double SquaredDistance(Point p, Point q)
        {
            double dx = p.X - q.X, dy = p.Y - q.Y;
            return dx * dx + dy * dy;
        }

        private static void Approximate(List<Point> contour, int first, int last, double epsilon, List<Point> polygon)
        {
            Point a = contour[first % contour.Count], b = contour[last % contour.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double longest = 0, threshold = epsilon * Math.Sqrt(dx * dx + dy * dy);
            int split = -1;
            for (int i = first + 1; i < last; i++)
            {
                Point p = contour[i % contour.Count];
                double cross = Math.Abs((p.Y - a.Y) * dx - (p.X - a.X) * dy);
                if (cross > longest) { longest = cross; split = i; }
            }
            if (split < 0 || longest <= threshold) { polygon.Add(a); return; }
            Approximate(contour, first, split, epsilon, polygon);
            Approximate(contour, split, last, epsilon, polygon);
        }
    }
}
