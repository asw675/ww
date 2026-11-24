// Behavior port of OnnxOCR onnxocr-ppocrv6 0.0.1 (utils.py and
// rec_postprocess.py), derived from the PaddlePaddle OCR implementation.
// Attribution: PaddlePaddle Authors. Licensed under Apache-2.0; see the
// bundled OCR third-party license and provenance notes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    // Recognition helpers for the locked onnxocr-ppocrv6 pipeline: perspective
    // text rectification (OpenCV cubic interpolation) and CTC label decoding.
    // This code consumes only the local already-captured OCR source pixels.
    internal static class OcrRecognition
    {
        private const int PhaseBits = 5, Phases = 1 << PhaseBits;
        private const int CoefficientBits = 15, CoefficientScale = 1 << CoefficientBits;
        private static readonly int[] CubicWeights = MakeCubicWeights();

        // Input order is the reference detector's top-left, top-right,
        // bottom-right, bottom-left. Invalid/degenerate detections have no crop.
        internal static PixelFrame Crop(PixelFrame frame, PointF[] sourceQuad)
        {
            return Crop(frame, sourceQuad, new Rectangle(0, 0, frame.Width, frame.Height));
        }

        // The OCR caller may have passed only a region to the reference
        // engine. Preserve full-frame point coordinates while replicating
        // that region's edges, rather than sampling surrounding screen pixels.
        internal static PixelFrame Crop(PixelFrame frame, PointF[] sourceQuad, Rectangle sourceBounds)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || sourceQuad == null || sourceQuad.Length != 4)
                return default(PixelFrame);
            sourceBounds.Intersect(new Rectangle(0, 0, frame.Width, frame.Height));
            if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0) return default(PixelFrame);
            for (int i = 0; i < sourceQuad.Length; i++)
                if (float.IsNaN(sourceQuad[i].X) || float.IsInfinity(sourceQuad[i].X) ||
                    float.IsNaN(sourceQuad[i].Y) || float.IsInfinity(sourceQuad[i].Y))
                    return default(PixelFrame);

            int width = (int)Math.Max(Distance(sourceQuad[0], sourceQuad[1]), Distance(sourceQuad[2], sourceQuad[3]));
            int height = (int)Math.Max(Distance(sourceQuad[0], sourceQuad[3]), Distance(sourceQuad[1], sourceQuad[2]));
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue / 4)
                return default(PixelFrame);

            // getPerspectiveTransform(points, [(0,0),(w,0),(w,h),(0,h)])
            // followed by warpPerspective maps each destination pixel back to
            // the source. Calculate that inverse directly on the unit square.
            PointF a = sourceQuad[0], b = sourceQuad[1], c = sourceQuad[2], d = sourceQuad[3];
            double dx1 = (double)b.X - c.X, dx2 = (double)d.X - c.X;
            double dy1 = (double)b.Y - c.Y, dy2 = (double)d.Y - c.Y;
            double dx3 = (double)a.X - b.X + c.X - d.X, dy3 = (double)a.Y - b.Y + c.Y - d.Y;
            double det = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(det) < 1e-9) return default(PixelFrame);
            double g = (dx3 * dy2 - dx2 * dy3) / det;
            double h = (dx1 * dy3 - dx3 * dy1) / det;
            double m00 = ((double)b.X - a.X + g * b.X) / width;
            double m01 = ((double)d.X - a.X + h * d.X) / height;
            double m10 = ((double)b.Y - a.Y + g * b.Y) / width;
            double m11 = ((double)d.Y - a.Y + h * d.Y) / height;
            double m20 = g / width, m21 = h / height;

            bool rotate = (double)height / width >= 1.5;
            int resultWidth = rotate ? height : width, resultHeight = rotate ? width : height;
            var pixels = new byte[resultWidth * resultHeight * 4];
            for (int y = 0; y < height; y++)
            {
                double x0 = a.X + m01 * y, y0 = a.Y + m11 * y, w0 = 1 + m21 * y;
                for (int x = 0; x < width; x++)
                {
                    double denominator = w0 + m20 * x;
                    double phaseScale = denominator == 0 ? 0 : Phases / denominator;
                    int sourceX = RoundedInt((x0 + m00 * x) * phaseScale);
                    int sourceY = RoundedInt((y0 + m10 * x) * phaseScale);
                    // np.rot90 is a counter-clockwise rotation of the output.
                    int offset = (rotate ? (width - 1 - x) * resultWidth + y : y * resultWidth + x) * 4;
                    SampleCubic(frame, sourceBounds, sourceX, sourceY, pixels, offset);
                }
            }
            return new PixelFrame(pixels, resultWidth, resultHeight);
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static int RoundedInt(double value)
        {
            if (double.IsNaN(value)) return 0;
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(value, MidpointRounding.ToEven)));
        }

        private static float[] CubicCoefficients(float x)
        {
            const float a = -.75f;
            float u = x + 1, v = 1 - x;
            var value = new float[4];
            value[0] = ((a * u - 5 * a) * u + 8 * a) * u - 4 * a;
            value[1] = ((a + 2) * x - (a + 3)) * x * x + 1;
            value[2] = ((a + 2) * v - (a + 3)) * v * v + 1;
            value[3] = 1 - value[0] - value[1] - value[2];
            return value;
        }

        private static int[] MakeCubicWeights()
        {
            // OpenCV remap quantizes interpolation fractions to 1/32 and its
            // 8-bit path uses signed 15-bit fixed-point 2D weights.
            var result = new int[Phases * Phases * 16];
            for (int y = 0; y < Phases; y++)
            {
                float[] cy = CubicCoefficients(y / (float)Phases);
                for (int x = 0; x < Phases; x++)
                {
                    float[] cx = CubicCoefficients(x / (float)Phases);
                    int offset = (y * Phases + x) * 16, total = 0;
                    for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++)
                        {
                            int weight = Math.Max(short.MinValue, Math.Min(short.MaxValue,
                                RoundedInt(cy[row] * cx[column] * CoefficientScale)));
                            result[offset + 4 * row + column] = weight;
                            total += weight;
                        }
                    // Keep the fixed-point 2D sum exactly unity. As in the
                    // cubic remap table, correct a coefficient in the bottom
                    // right 2x2 region after quantization.
                    int smallest = offset + 10, largest = smallest;
                    for (int row = 2; row < 4; row++)
                        for (int column = 2; column < 4; column++)
                        {
                            int index = offset + row * 4 + column;
                            if (result[index] < result[smallest]) smallest = index;
                            if (result[index] > result[largest]) largest = index;
                        }
                    int diff = total - CoefficientScale;
                    if (diff != 0) result[diff < 0 ? largest : smallest] -= diff;
                }
            }
            return result;
        }

        private static byte Quantize(int value)
        {
            return (byte)Math.Max(0, Math.Min(255, (value + CoefficientScale / 2) >> CoefficientBits));
        }

        private static void SampleCubic(PixelFrame frame, Rectangle bounds, int x, int y, byte[] target, int offset)
        {
            int startX = (x >> PhaseBits) - 1, startY = (y >> PhaseBits) - 1;
            int coefficient = (((y & (Phases - 1)) * Phases) + (x & (Phases - 1))) * 16;
            int blue = 0, green = 0, red = 0;
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                {
                    // BORDER_REPLICATE: a sample outside the image uses the
                    // nearest valid boundary pixel, not a black/zero edge.
                    int sx = Math.Max(bounds.Left, Math.Min(bounds.Right - 1, startX + column));
                    int sy = Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, startY + row));
                    int r, g, b;
                    frame.Rgb(sx, sy, out r, out g, out b);
                    int weight = CubicWeights[coefficient + row * 4 + column];
                    red += r * weight;
                    green += g * weight;
                    blue += b * weight;
                }
            target[offset] = Quantize(blue);
            target[offset + 1] = Quantize(green);
            target[offset + 2] = Quantize(red);
            target[offset + 3] = 255;
        }

        internal static OcrLine Decode(Tensor<float> logits, int batch, string[] dictionary, Rectangle region)
        {
            int[] dimensions = logits.Dimensions.ToArray();
            if (dimensions.Length != 3 || batch < 0 || batch >= dimensions[0] || dimensions[2] < 1)
                throw new InvalidDataException("OCR recognition must return [batch,time,classes].");
            var text = new StringBuilder();
            var confidence = new List<float>();
            int previous = -1;
            for (int step = 0; step < dimensions[1]; step++)
            {
                int best = 0, offset = (batch * dimensions[1] + step) * dimensions[2];
                float score = logits.GetValue(offset);
                for (int label = 1; label < dimensions[2]; label++)
                {
                    float candidate = logits.GetValue(offset + label);
                    // Numpy argmax selects the first equal maximum, or the
                    // first NaN; max propagates NaN. Real model scores are finite.
                    if (!float.IsNaN(score) && (float.IsNaN(candidate) || candidate > score))
                    {
                        score = candidate;
                        best = label;
                    }
                }
                if (best != 0 && best != previous)
                {
                    if (best - 1 >= dictionary.Length)
                        throw new InvalidDataException("OCR recognition label exceeds the configured dictionary.");
                    text.Append(dictionary[best - 1]);
                    confidence.Add(score);
                }
                previous = best;
            }
            // CTC drops blank and adjacent repeats before the mean. Keep spaces
            // and all other original text: normalization belongs to the caller.
            float mean = confidence.Count == 0 ? 0 : PairwiseSum(confidence, 0, confidence.Count) / confidence.Count;
            string decoded = text.ToString();
            return new OcrLine { Box = region, Text = decoded, RawText = decoded, Score = mean };
        }

        private static float PairwiseSum(List<float> values, int start, int count)
        {
            // The reference np.mean reduces float32 confidence values. Match
            // its contiguous pairwise grouping to avoid double-precision drift.
            if (count < 8)
            {
                float value = -0f;
                for (int i = 0; i < count; i++) value += values[start + i];
                return value;
            }
            if (count <= 128)
            {
                var partial = new float[8];
                for (int i = 0; i < 8; i++) partial[i] = values[start + i];
                int end = count - count % 8, at = 8;
                for (; at < end; at += 8)
                    for (int i = 0; i < 8; i++) partial[i] += values[start + at + i];
                float value = ((partial[0] + partial[1]) + (partial[2] + partial[3])) +
                    ((partial[4] + partial[5]) + (partial[6] + partial[7]));
                for (; at < count; at++) value += values[start + at];
                return value;
            }
            int split = count / 2;
            split -= split % 8;
            return PairwiseSum(values, start, split) + PairwiseSum(values, start + split, count - split);
        }
    }
}
