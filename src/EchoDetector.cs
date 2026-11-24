using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal static class EchoModel
    {
        internal static string ModelPath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                {
                    System.IO.Path.Combine(baseDir, "assets", "echo_model", "echo.onnx"),
                    System.IO.Path.Combine("assets", "echo_model", "echo.onnx"),
                    System.IO.Path.Combine(baseDir, "..", "..", "assets", "echo_model", "echo.onnx")
                };
                foreach (string candidate in candidates)
                    if (System.IO.File.Exists(candidate))
                        return candidate;
                return candidates[0];
            }
        }
    }

    internal struct EchoBox
    {
        internal Rectangle Box;
        internal Size SourceSize;
        internal float Score;

        internal static void OrderOnScreen(List<EchoBox> boxes)
        {
            if (boxes.Count < 2) return;
            // Rows can overlap vertically. Preserve the existing order for equal positions.
            bool descending = CompareLocation(boxes[1], boxes[0]) < 0;
            int sorted = 2;
            while (sorted < boxes.Count && (CompareLocation(boxes[sorted], boxes[sorted - 1]) < 0) == descending) sorted++;
            if (descending) boxes.Reverse(0, sorted);
            for (int i = sorted; i < boxes.Count; i++)
            {
                EchoBox item = boxes[i];
                int low = 0, high = i;
                while (low < high)
                {
                    int mid = (low + high) / 2;
                    if (CompareLocation(item, boxes[mid]) < 0) high = mid;
                    else low = mid + 1;
                }
                for (int j = i; j > low; j--) boxes[j] = boxes[j - 1];
                boxes[low] = item;
            }
        }

        private static int CompareLocation(EchoBox first, EchoBox second)
        {
            Rectangle a = first.Box, b = second.Box;
            bool sameRow = a.Top <= b.Bottom && b.Top <= a.Bottom;
            int horizontal = a.X.CompareTo(b.X), vertical = a.Y.CompareTo(b.Y);
            if (sameRow && horizontal != 0) return horizontal;
            if (vertical != 0) return vertical;
            if (horizontal != 0) return horizontal;
            return (int)(first.Score * 1000.0 - second.Score * 1000.0);
        }
    }

    internal sealed class EchoApproach
    {
        private long missingSince = -1;

        internal char Direction(List<EchoBox> detections, Size screen, char previous, long now)
        {
            if (detections.Count == 0)
            {
                if (missingSince < 0) missingSince = now;
                return now - missingSince > 3000 ? '\0' : 'W';
            }
            missingSince = -1;
            EchoBox echo = detections[0];
            if (!echo.SourceSize.IsEmpty) screen = echo.SourceSize;
            Rectangle box = echo.Box;
            double dx = Math.Round(box.X + box.Width / 2.0) - screen.Width / 2;
            int tolerance = (int)(screen.Height * (previous == '\0' ? .05 : .15));
            if (Math.Abs(dx) >= tolerance) return dx > 0 ? 'D' : 'A';
            return box.Y + box.Height / 3.0 + 1 > (int)(screen.Height * .65) ? 'S' : 'W';
        }
    }

    // YOLOv8 single-class detector for ground echoes. The model follows the
    // reference export: images 1x3x640x640 -> output0 1x5x8400.
    internal sealed class EchoDetector : IDisposable
    {
        private const int Side = 640;
        private const float PadColor = 114f / 255f;
        private const float NmsIou = 0.45f;

        private readonly InferenceSession session;
        private readonly string inputName;
        private readonly float[] data = new float[3 * Side * Side];
        private readonly DenseTensor<float> tensor;
        private readonly List<EchoBox> candidates = new List<EchoBox>();

        internal EchoDetector(string modelPath)
        {
            using (var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 })
                session = new InferenceSession(modelPath, options);
            inputName = session.InputMetadata.Keys.First();
            tensor = new DenseTensor<float>(data, new[] { 1, 3, Side, Side });
        }

        internal List<EchoBox> Detect(PixelFrame frame, float threshold)
        {
            var results = new List<EchoBox>();
            frame = frame.FullResolution;
            if (frame.Width <= 0 || frame.Height <= 0)
                return results;
            double scale = Math.Min((double)Side / frame.Width, (double)Side / frame.Height);
            int width = Math.Max(1, (int)Math.Round(frame.Width * scale));
            int height = Math.Max(1, (int)Math.Round(frame.Height * scale));
            int padX = (Side - width) / 2, padY = (Side - height) / 2;
            FillLetterbox(frame, data, width, height, padX, padY);

            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
                session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) }))
            {
                Tensor<float> values = output.First().AsTensor<float>();
                int[] dimensions = values.Dimensions.ToArray();
                int rank = dimensions.Length;
                int channels = dimensions[rank - 2];
                int anchors = dimensions[rank - 1];
                bool transposed = channels > anchors;
                if (transposed)
                {
                    int swap = channels;
                    channels = anchors;
                    anchors = swap;
                }
                int classes = channels - 4;
                if (classes <= 0)
                    return results;
                candidates.Clear();
                for (int index = 0; index < anchors; index++)
                {
                    float best = transposed ? values[0, index, 4] : values[0, 4, index];
                    if (!(best > threshold)) continue;
                    bool echoClass = true;
                    for (int label = 1; label < classes; label++)
                    {
                        float score = transposed ? values[0, index, 4 + label] : values[0, 4 + label, index];
                        if (score > best) { echoClass = false; break; }
                    }
                    if (!echoClass) continue;
                    float cx = transposed ? values[0, index, 0] : values[0, 0, index];
                    float cy = transposed ? values[0, index, 1] : values[0, 1, index];
                    float bw = transposed ? values[0, index, 2] : values[0, 2, index];
                    float bh = transposed ? values[0, index, 3] : values[0, 3, index];
                    var box = new Rectangle((int)((cx - padX - bw / 2) / (float)scale),
                        (int)((cy - padY - bh / 2) / (float)scale), (int)(bw / (float)scale), (int)(bh / (float)scale));
                    candidates.Add(new EchoBox { Box = box, Score = best, SourceSize = new Size(frame.Width, frame.Height) });
                }
                // Equal scores retain the model's original anchor order.
                foreach (EchoBox candidate in candidates.OrderByDescending(item => item.Score))
                    if (!results.Any(kept => IoU(kept.Box, candidate.Box) > NmsIou))
                        results.Add(candidate);
                EchoBox.OrderOnScreen(results);
            }
            return results;
        }

        internal static void FillLetterbox(PixelFrame frame, float[] destination, int width, int height, int padX, int padY)
        {
            const int plane = Side * Side;
            for (int i = 0; i < destination.Length; i++) destination[i] = PadColor;
            var columns = new LinearAxis[width];
            for (int x = 0; x < width; x++) columns[x] = new LinearAxis(x, frame.Width, width, true);
            for (int y = 0; y < height; y++)
            {
                var row = new LinearAxis(y, frame.Height, height, false);
                int index = (y + padY) * Side + padX;
                for (int x = 0; x < width; x++, index++)
                {
                    LinearAxis column = columns[x];
                    int red, green, blue;
                    LinearAxis.Sample(frame, column, row, out red, out green, out blue);
                    destination[index] = red / 255f;
                    destination[index + plane] = green / 255f;
                    destination[index + 2 * plane] = blue / 255f;
                }
            }
        }

        private static double IoU(Rectangle left, Rectangle right)
        {
            int x1 = Math.Max(left.Left, right.Left), y1 = Math.Max(left.Top, right.Top);
            int x2 = Math.Min(left.Right, right.Right), y2 = Math.Min(left.Bottom, right.Bottom);
            int width = Math.Max(0, x2 - x1), height = Math.Max(0, y2 - y1);
            int intersection = width * height;
            int union = left.Width * left.Height + right.Width * right.Height - intersection;
            return union <= 0 ? 1 : (double)intersection / union;
        }

        public void Dispose()
        {
            session.Dispose();
        }
    }
}
