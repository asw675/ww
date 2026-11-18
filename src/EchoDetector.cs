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
        internal float Score;
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
            var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
            session = new InferenceSession(modelPath, options);
            inputName = session.InputMetadata.Keys.First();
            tensor = new DenseTensor<float>(data, new[] { 1, 3, Side, Side });
        }

        internal List<EchoBox> Detect(PixelFrame frame, float threshold)
        {
            var results = new List<EchoBox>();
            if (frame.Width <= 0 || frame.Height <= 0)
                return results;
            double scale = Math.Min((double)Side / frame.Width, (double)Side / frame.Height);
            int width = Math.Max(1, (int)Math.Round(frame.Width * scale));
            int height = Math.Max(1, (int)Math.Round(frame.Height * scale));
            int padX = (Side - width) / 2, padY = (Side - height) / 2;
            Letterbox(frame, scale, width, height, padX, padY);

            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
                session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) }))
            {
                Tensor<float> values = output.First().AsTensor<float>();
                int rank = values.Dimensions.Length;
                int channels = values.Dimensions[rank - 2];
                int anchors = values.Dimensions[rank - 1];
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
                    float best = 0;
                    for (int label = 0; label < classes; label++)
                    {
                        float score = transposed ? values[0, index, 4 + label] : values[0, 4 + label, index];
                        if (score > best) best = score;
                    }
                    if (best < threshold)
                        continue;
                    float cx = transposed ? values[0, index, 0] : values[0, 0, index];
                    float cy = transposed ? values[0, index, 1] : values[0, 1, index];
                    float bw = transposed ? values[0, index, 2] : values[0, 2, index];
                    float bh = transposed ? values[0, index, 3] : values[0, 3, index];
                    var box = new RectangleF((cx - bw / 2 - padX) / (float)scale, (cy - bh / 2 - padY) / (float)scale,
                        bw / (float)scale, bh / (float)scale);
                    candidates.Add(new EchoBox
                    {
                        Box = new Rectangle((int)Math.Round(box.X), (int)Math.Round(box.Y),
                            Math.Max(1, (int)Math.Round(box.Width)), Math.Max(1, (int)Math.Round(box.Height))),
                        Score = best
                    });
                }
                candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
                foreach (EchoBox candidate in candidates)
                {
                    bool keep = true;
                    foreach (EchoBox kept in results)
                    {
                        if (IoU(kept.Box, candidate.Box) > NmsIou)
                        {
                            keep = false;
                            break;
                        }
                    }
                    if (!keep)
                        continue;
                    var clipped = Rectangle.Intersect(candidate.Box, new Rectangle(0, 0, frame.Width, frame.Height));
                    if (clipped.Width < 2 || clipped.Height < 2)
                        continue;
                    results.Add(new EchoBox { Box = clipped, Score = candidate.Score });
                    if (results.Count >= 20)
                        break;
                }
            }
            return results;
        }

        private void Letterbox(PixelFrame frame, double scale, int width, int height, int padX, int padY)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                int plane = channel * Side * Side;
                for (int y = 0; y < Side; y++)
                {
                    bool insideY = y >= padY && y < padY + height;
                    for (int x = 0; x < Side; x++)
                    {
                        float value = PadColor;
                        if (insideY && x >= padX && x < padX + width)
                        {
                            double sx = (x - padX + 0.5) / scale - 0.5;
                            double sy = (y - padY + 0.5) / scale - 0.5;
                            value = Sample(frame, sx, sy, channel);
                        }
                        data[plane + y * Side + x] = value;
                    }
                }
            }
        }

        private static float Sample(PixelFrame frame, double x, double y, int channel)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            int x1 = Math.Min(frame.Width - 1, Math.Max(0, x0 + 1));
            int y1 = Math.Min(frame.Height - 1, Math.Max(0, y0 + 1));
            x0 = Math.Min(frame.Width - 1, Math.Max(0, x0));
            y0 = Math.Min(frame.Height - 1, Math.Max(0, y0));
            double top = Pixel(frame, x0, y0, channel) * (1 - fx) + Pixel(frame, x1, y0, channel) * fx;
            double bottom = Pixel(frame, x0, y1, channel) * (1 - fx) + Pixel(frame, x1, y1, channel) * fx;
            return (float)((top * (1 - fy) + bottom * fy) / 255.0);
        }

        private static double Pixel(PixelFrame frame, int x, int y, int channel)
        {
            int r, g, b;
            frame.Rgb(x, y, out r, out g, out b);
            return channel == 0 ? r : channel == 1 ? g : b;
        }

        private static double IoU(Rectangle left, Rectangle right)
        {
            int x1 = Math.Max(left.Left, right.Left), y1 = Math.Max(left.Top, right.Top);
            int x2 = Math.Min(left.Right, right.Right), y2 = Math.Min(left.Bottom, right.Bottom);
            int width = Math.Max(0, x2 - x1), height = Math.Max(0, y2 - y1);
            int intersection = width * height;
            int union = left.Width * left.Height + right.Width * right.Height - intersection;
            return union <= 0 ? 0 : (double)intersection / union;
        }

        public void Dispose()
        {
            session.Dispose();
        }
    }
}
