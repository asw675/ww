using System;
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
            this.path = path;
            if (path == null || !File.Exists(path)) return;
            try
            {
                using (Stream stream = File.OpenRead(path))
                {
                    var values = (double[])Serializer.ReadObject(stream);
                    if (values == null || values.Length != fullAreas.Length) return;
                    for (int i = 0; i < fullAreas.Length; i++)
                        if (values[i] > 0 && values[i] <= 126 * 126) fullAreas[i] = values[i];
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
            int target = expectedIndex;
            if (target < 0)
                for (int i = 0; i < colorCounts.Length; i++)
                    if (colorCounts[i] > (target < 0 ? 0 : colorCounts[target])) target = i;
            if (target < 0) return 0;
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

            double normalizedArea = area * 2160.0 / frame.SourceHeight * 2160 / frame.SourceHeight;
            if (EnclosesCenter(centerX, centerY, selectedLabel))
            {
                changed |= fullAreas[target] != normalizedArea;
                fullAreas[target] = normalizedArea;
                return 1;
            }
            return fullAreas[target] <= 0 ? 0 : Math.Min(.99, normalizedArea / fullAreas[target]);
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

        private bool EnclosesCenter(int x, int y, int blocked)
        {
            int center = y * width + x;
            if (labels[center] == blocked) return false;
            int tail = 0;
            Visit(center, blocked, ref tail);
            for (int head = 0; head < tail; head++)
            {
                int cell = queue[head], row = cell / width, column = cell % width;
                if (column == 0 || column == width - 1 || row == 0 || row == height - 1) return false;
                Visit(cell - 1, blocked, ref tail);
                Visit(cell + 1, blocked, ref tail);
                Visit(cell - width, blocked, ref tail);
                Visit(cell + width, blocked, ref tail);
            }
            return true;
        }

        private void Visit(int cell, int blocked, ref int tail)
        {
            if (labels[cell] == blocked || labels[cell] < 0) return;
            labels[cell] = -1;
            queue[tail++] = cell;
        }
    }
}
