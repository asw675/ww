using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal sealed class OcrLine
    {
        internal Rectangle Box;
        internal string Text;
        internal float Score;
    }

    // PP-OCRv6 (DB detection + CTC recognition) through ONNX Runtime, mirroring the
    // reference preprocessing: ImageNet normalisation, 960 max side, DB threshold
    // 0.3 / box threshold 0.6 / unclip 1.5, and a 3x48x320 recognition input.
    internal sealed class OcrReader : IDisposable
    {
        private const int LimitSide = 960;
        private const float DetThreshold = .3f, BoxThreshold = .6f, UnclipRatio = 1.5f;
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
            var results = new List<OcrLine>();
            Rectangle region = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
            if (region.Width < 8 || region.Height < 8)
                return results;
            float[] input = PrepareDet(frame, region, out int resizedW, out int resizedH);
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
                    string text = Recognize(frame, source, out float score);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    results.Add(new OcrLine { Box = source, Text = text.Trim(), Score = score });
                }
            }
            results.Sort((left, right) => left.Box.Y.CompareTo(right.Box.Y));
            return results;
        }

        private float[] PrepareDet(PixelFrame frame, Rectangle area, out int width, out int height)
        {
            double ratio = 1;
            double longest = Math.Max(area.Width, area.Height);
            if (longest > LimitSide)
                ratio = LimitSide / longest;
            width = Math.Max(32, (int)(Math.Round(area.Width * ratio / 32) * 32));
            height = Math.Max(32, (int)(Math.Round(area.Height * ratio / 32) * 32));
            var data = new float[3 * width * height];
            double sx = (double)area.Width / width, sy = (double)area.Height / height;
            double[] mean = { .485, .456, .406 }, std = { .229, .224, .225 };
            for (int y = 0; y < height; y++)
            {
                double sourceY = (y + .5) * sy - .5;
                for (int x = 0; x < width; x++)
                {
                    double sourceX = (x + .5) * sx - .5;
                    frame.Rgb(Clamp((int)Math.Round(sourceX) + area.X, area.Left, area.Right - 1),
                        Clamp((int)Math.Round(sourceY) + area.Top, area.Top, area.Bottom - 1),
                        out int r, out int g, out int b);
                    int plane = y * width + x;
                    data[plane] = (float)((b / 255.0 - mean[0]) / std[0]);
                    data[width * height + plane] = (float)((g / 255.0 - mean[1]) / std[1]);
                    data[2 * width * height + plane] = (float)((r / 255.0 - mean[2]) / std[2]);
                }
            }
            return data;
        }

        private static IEnumerable<Rectangle> DetBoxes(Tensor<float> map, int width, int height)
        {
            var boxes = new List<Rectangle>();
            var visited = new bool[width * height];
            var queue = new Queue<int>();
            for (int index = 0; index < visited.Length; index++)
            {
                if (visited[index] || map[0, 0, index / width, index % width] <= DetThreshold)
                    continue;
                int minX = width, minY = height, maxX = 0, maxY = 0, count = 0;
                double sum = 0;
                visited[index] = true;
                queue.Enqueue(index);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int x = current % width, y = current / width;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    count++;
                    sum += map[0, 0, y, x];
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int next = ny * width + nx;
                            if (visited[next] || map[0, 0, ny, nx] <= DetThreshold) continue;
                            visited[next] = true;
                            queue.Enqueue(next);
                        }
                }
                if (count < 6)
                    continue;
                double score = sum / count;
                if (score < BoxThreshold)
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

        private string Recognize(PixelFrame frame, Rectangle area, out float score)
        {
            score = 0;
            int width = Math.Max(1, (int)Math.Ceiling(RecHeight * (double)area.Width / area.Height));
            width = Math.Min(width, RecWidth);
            var data = new float[3 * RecHeight * RecWidth];
            double sx = (double)area.Width / width, sy = (double)area.Height / RecHeight;
            for (int y = 0; y < RecHeight; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    frame.Rgb(Clamp((int)((x + .5) * sx) + area.X, area.Left, area.Right - 1),
                        Clamp((int)((y + .5) * sy) + area.Top, area.Top, area.Bottom - 1),
                        out int r, out int g, out int b);
                    int plane = y * RecWidth + x;
                    data[plane] = (float)(b / 255.0 - .5);
                    data[RecHeight * RecWidth + plane] = (float)(g / 255.0 - .5);
                    data[2 * RecHeight * RecWidth + plane] = (float)(r / 255.0 - .5);
                }
            }
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
                rec.Run(new[] { NamedOnnxValue.CreateFromTensor(recInput,
                    new DenseTensor<float>(data, new[] { 1, 3, RecHeight, RecWidth })) }))
            {
                Tensor<float> logits = output.First().AsTensor<float>();
                int steps = logits.Dimensions[1];
                int classes = logits.Dimensions[2];
                var text = new System.Text.StringBuilder();
                int previous = -1;
                double total = 0;
                int counted = 0;
                for (int step = 0; step < steps; step++)
                {
                    int best = 0;
                    float max = logits[0, step, 0];
                    for (int label = 1; label < classes; label++)
                    {
                        float value = logits[0, step, label];
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
                score = counted > 0 ? (float)(total / counted) : 0;
                return text.ToString();
            }
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        public void Dispose()
        {
            det.Dispose();
            rec.Dispose();
        }
    }
}
