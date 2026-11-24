using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal static class OcrText
    {
        internal static string Compact(string text)
        {
            return string.Concat((text ?? "").Normalize(NormalizationForm.FormKC)
                .Where(character => !char.IsWhiteSpace(character)));
        }
    }

    internal sealed class OcrLine
    {
        internal Rectangle Box;
        internal string Text;
        internal float Score;
    }

    // PP-OCRv6 text detection and recognition through ONNX Runtime.
    internal sealed class OcrReader : IDisposable
    {
        private const int LimitSide = 960;
        private const float DetThreshold = .3f, BoxThreshold = .5f, UnclipRatio = 1.6f;
        private const int RecHeight = 48, RecWidth = 320;

        private readonly InferenceSession det, rec;
        private readonly string[] dictionary;
        private readonly string detInput, recInput;

        internal static string Directory
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                {
                    Path.Combine(baseDir, "assets", "ocr"),
                    Path.Combine("assets", "ocr"),
                    Path.Combine(baseDir, "..", "..", "assets", "ocr")
                };
                foreach (string candidate in candidates)
                    if (File.Exists(Path.Combine(candidate, "det.onnx")))
                        return candidate;
                return candidates[0];
            }
        }

        internal OcrReader(string directory)
        {
            var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
            det = new InferenceSession(Path.Combine(directory, "det.onnx"), options);
            rec = new InferenceSession(Path.Combine(directory, "rec.onnx"), options);
            detInput = det.InputMetadata.Keys.First();
            recInput = rec.InputMetadata.Keys.First();
            dictionary = File.ReadAllLines(Path.Combine(directory, "ppocrv6_dict.txt"));
            var withSpace = new List<string>(dictionary) { " " };
            dictionary = withSpace.ToArray();
        }

        internal List<OcrLine> Read(PixelFrame frame, Rectangle area)
        {
            PixelFrame source = frame.FullResolution;
            return ReadViewport(frame, source, Scale(area, frame.Width, frame.Height, source.Width, source.Height));
        }

        internal List<OcrLine> ReadRelative(PixelFrame frame, double left, double top, double right, double bottom,
            bool centered = false)
        {
            PixelFrame source = frame.FullResolution;
            var area = new Rectangle((int)Math.Round(left * source.Width), (int)Math.Round(top * source.Height),
                (int)Math.Round((right - left) * source.Width), (int)Math.Round((bottom - top) * source.Height));
            if (centered && Math.Abs(source.Width / (double)source.Height - 16.0 / 9) > .01)
            {
                double width = Math.Min(source.Width, source.Height * (16.0 / 9)), height = width * (9.0 / 16);
                area = new Rectangle((int)Math.Round(source.Width * .5 + (left - .5) * width),
                    (int)Math.Round(source.Height * .5 + (top - .5) * height),
                    (int)Math.Round((right - left) * width), (int)Math.Round((bottom - top) * height));
            }
            return ReadViewport(frame, source, area);
        }

        private List<OcrLine> ReadViewport(PixelFrame display, PixelFrame source, Rectangle area)
        {
            List<OcrLine> lines = ReadSource(source, area);
            if (display.Width != source.Width || display.Height != source.Height)
                foreach (OcrLine line in lines)
                    line.Box = Scale(line.Box, source.Width, source.Height, display.Width, display.Height);
            return lines;
        }

        private static Rectangle Scale(Rectangle area, int fromWidth, int fromHeight, int toWidth, int toHeight)
        {
            double x = toWidth / (double)fromWidth, y = toHeight / (double)fromHeight;
            return Rectangle.FromLTRB((int)Math.Round(area.Left * x), (int)Math.Round(area.Top * y),
                (int)Math.Round(area.Right * x), (int)Math.Round(area.Bottom * y));
        }

        private List<OcrLine> ReadSource(PixelFrame frame, Rectangle area)
        {
            var results = new List<OcrLine>();
            Rectangle region = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
            if (region.Width <= 0 || region.Height <= 0)
                return results;
            int resizedW, resizedH;
            float[] input = PrepareDet(frame, region, out resizedW, out resizedH);
            var regions = new List<Rectangle>();
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
                det.Run(new[] { NamedOnnxValue.CreateFromTensor(detInput,
                    new DenseTensor<float>(input, new[] { 1, 3, resizedH, resizedW })) }))
            {
                Tensor<float> map = output.First().AsTensor<float>();
                float scaleX = (float)region.Width / resizedW, scaleY = (float)region.Height / resizedH;
                foreach (Rectangle box in DetBoxes(map, resizedW, resizedH))
                {
                    var source = new Rectangle(
                        region.X + (int)(box.X * scaleX), region.Y + (int)(box.Y * scaleY),
                        Math.Max(2, (int)(box.Width * scaleX)), Math.Max(2, (int)(box.Height * scaleY)));
                    source = Rectangle.Intersect(source, region);
                    if (source.Width < 3 || source.Height < 3)
                        continue;
                    regions.Add(source);
                }
            }
            var ordered = regions.OrderBy(box => box.Width / (double)box.Height).ToList();
            for (int start = 0; start < ordered.Count; start += 6)
                Recognize(frame, ordered.GetRange(start, Math.Min(6, ordered.Count - start)), results);
            results.Sort(CompareLines);
            return results;
        }

        private static int CompareLines(OcrLine left, OcrLine right)
        {
            Rectangle a = left.Box, b = right.Box;
            bool sameRow = a.Top <= b.Bottom && b.Top <= a.Bottom;
            int cmp = sameRow ? a.Left.CompareTo(b.Left) : a.Top.CompareTo(b.Top);
            if (cmp == 0) cmp = sameRow ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left);
            if (cmp == 0) cmp = (int)(left.Score * 1000.0 - right.Score * 1000.0);
            return cmp != 0 ? cmp : string.CompareOrdinal(left.Text, right.Text);
        }

        internal static float[] PrepareDet(PixelFrame frame, Rectangle area, out int width, out int height)
        {
            if (area.Width + area.Height < 64 && (area.Width < 32 || area.Height < 32))
            {
                int paddedWidth = Math.Max(32, area.Width), paddedHeight = Math.Max(32, area.Height);
                var padded = new byte[paddedWidth * paddedHeight * 4];
                var source = new byte[area.Width * area.Height * 4];
                frame.CopyBgra(area, source);
                for (int y = 0; y < area.Height; y++)
                    Buffer.BlockCopy(source, y * area.Width * 4, padded, y * paddedWidth * 4, area.Width * 4);
                frame = new PixelFrame(padded, paddedWidth, paddedHeight);
                area = new Rectangle(0, 0, paddedWidth, paddedHeight);
            }
            double ratio = 1;
            double longest = Math.Max(area.Width, area.Height);
            if (longest > LimitSide)
                ratio = LimitSide / longest;
            width = Math.Max(32, (int)Math.Round((int)(area.Width * ratio) / 32.0) * 32);
            height = Math.Max(32, (int)Math.Round((int)(area.Height * ratio) / 32.0) * 32);
            var data = new float[3 * width * height];
            WriteImage(frame, area, width, height, width, data, 0, true);
            return data;
        }

        private static IEnumerable<Rectangle> DetBoxes(Tensor<float> map, int width, int height)
        {
            var boxes = new List<Rectangle>();
            var visited = new bool[width * height];
            var queue = new Queue<int>();
            for (int index = 0; index < visited.Length; index++)
            {
                if (visited[index] || map.GetValue(index) <= DetThreshold)
                    continue;
                int minX = width, minY = height, maxX = 0, maxY = 0;
                var pixels = new List<Point>();
                visited[index] = true;
                queue.Enqueue(index);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int x = current % width, y = current / width;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    pixels.Add(new Point(x, y));
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int next = ny * width + nx;
                            if (visited[next] || map.GetValue(next) <= DetThreshold) continue;
                            visited[next] = true;
                            queue.Enqueue(next);
                        }
                }
                PointF[] corners;
                if (pixels.Count < 4 || !OcrDetectionBox.Fit(pixels, out corners) ||
                    OcrDetectionBox.Score(map, width, height, corners) < BoxThreshold)
                    continue;
                int boxWidth = maxX - minX + 1, boxHeight = maxY - minY + 1;
                double perimeter = 2.0 * (boxWidth + boxHeight);
                double distance = perimeter <= 0 ? 0 : boxWidth * boxHeight * UnclipRatio / perimeter;
                var box = new Rectangle((int)(minX - distance), (int)(minY - distance),
                    (int)(boxWidth + 2 * distance), (int)(boxHeight + 2 * distance));
                box = Rectangle.Intersect(box, new Rectangle(0, 0, width, height));
                if (box.Width >= 3 && box.Height >= 3)
                    boxes.Add(box);
            }
            boxes.Sort((left, right) => left.Y.CompareTo(right.Y));
            return boxes;
        }

        internal static void PrepareRecognition(PixelFrame frame, Rectangle area, int stride, float[] data, int offset)
        {
            int width = Math.Max(1, (int)Math.Ceiling(RecHeight * (double)area.Width / area.Height));
            WriteImage(frame, area, Math.Min(width, stride), RecHeight, stride, data, offset, false);
        }

        private static void WriteImage(PixelFrame frame, Rectangle area, int width, int height, int stride,
            float[] data, int offset, bool detection)
        {
            int plane = height * stride;
            var columns = new LinearAxis[width];
            for (int x = 0; x < width; x++) columns[x] = new LinearAxis(x, area.Width, width, true, area.X);
            for (int y = 0; y < height; y++)
            {
                var row = new LinearAxis(y, area.Height, height, false, area.Y);
                for (int x = 0; x < width; x++)
                {
                    int r, g, b;
                    LinearAxis.Sample(frame, columns[x], row, out r, out g, out b);
                    int i = offset + y * stride + x;
                    if (detection)
                    {
                        const float scale = 1f / 255f;
                        data[i] = (b * scale - .485f) / .229f;
                        data[i + plane] = (g * scale - .456f) / .224f;
                        data[i + 2 * plane] = (r * scale - .406f) / .225f;
                    }
                    else
                    {
                        data[i] = (b / 255f - .5f) * 2;
                        data[i + plane] = (g / 255f - .5f) * 2;
                        data[i + 2 * plane] = (r / 255f - .5f) * 2;
                    }
                }
            }
        }

        private void Recognize(PixelFrame frame, List<Rectangle> regions, List<OcrLine> results)
        {
            int width = Math.Max(RecWidth, (int)(RecHeight * regions.Max(box => box.Width / (double)box.Height)));
            int batchStride = 3 * RecHeight * width;
            var data = new float[regions.Count * batchStride];
            for (int i = 0; i < regions.Count; i++)
                PrepareRecognition(frame, regions[i], width, data, i * batchStride);
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
                rec.Run(new[] { NamedOnnxValue.CreateFromTensor(recInput,
                    new DenseTensor<float>(data, new[] { regions.Count, 3, RecHeight, width })) }))
            {
                Tensor<float> logits = output.First().AsTensor<float>();
                for (int i = 0; i < regions.Count; i++)
                {
                    OcrLine line = Decode(logits, i, regions[i]);
                    if (line.Score >= .5f && !string.IsNullOrWhiteSpace(line.Text)) results.Add(line);
                }
            }
        }

        private OcrLine Decode(Tensor<float> logits, int batch, Rectangle region)
        {
            int[] dimensions = logits.Dimensions.ToArray();
            var text = new System.Text.StringBuilder();
            int previous = -1;
            double total = 0;
            int counted = 0;
            for (int step = 0; step < dimensions[1]; step++)
            {
                int best = 0;
                int start = (batch * dimensions[1] + step) * dimensions[2];
                float max = logits.GetValue(start);
                for (int label = 1; label < dimensions[2]; label++)
                {
                    float value = logits.GetValue(start + label);
                    if (value <= max) continue;
                    max = value;
                    best = label;
                }
                if (best != 0 && best != previous && best - 1 < dictionary.Length)
                {
                    text.Append(dictionary[best - 1]);
                    total += max;
                    counted++;
                }
                previous = best;
            }
            return new OcrLine { Box = region, Score = counted > 0 ? (float)(total / counted) : 0,
                Text = text.ToString().Trim() };
        }

        public void Dispose()
        {
            det.Dispose();
            rec.Dispose();
        }
    }
}
