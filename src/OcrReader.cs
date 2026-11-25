using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal static class OcrText
    {
        private const uint ChineseSimplifiedLocale = 0x0804;
        private const uint SimplifiedChinese = 0x02000000;
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LCMapStringW", ExactSpelling = true)]
        private static extern int MapSimplified(uint locale, uint flags, string source, int sourceLength,
            [Out] char[] destination, int destinationLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct CFRange
        {
            internal IntPtr Location, Length;
        }

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, IntPtr characters, IntPtr length);
        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateMutableCopy(IntPtr allocator, IntPtr maxLength, IntPtr source);
        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFStringTransform(IntPtr source, IntPtr range, IntPtr transform,
            [MarshalAs(UnmanagedType.I1)] bool reverse);
        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringGetLength(IntPtr source);
        [DllImport(CoreFoundation)]
        private static extern void CFStringGetCharacters(IntPtr source, CFRange range, IntPtr buffer);
        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr value);

        private static bool ContainsHan(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int code = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    code = char.ConvertToUtf32(text[i], text[++i]);
                if (code >= 0x3400 && code <= 0x9fff || code >= 0xf900 && code <= 0xfaff ||
                    code >= 0x20000 && code <= 0x323af)
                    return true;
            }
            return false;
        }

        // Normalize recognized Chinese text before it reaches the task-specific
        // matching rules, without translating or modifying English/digits.
        internal static string Simplify(string text)
        {
            if (string.IsNullOrEmpty(text) || !ContainsHan(text))
                return text ?? "";
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return SimplifyWithCoreFoundation(text);
            int length = MapSimplified(ChineseSimplifiedLocale, SimplifiedChinese, text, text.Length, null, 0);
            if (length <= 0) return text;
            var mapped = new char[length];
            int written = MapSimplified(ChineseSimplifiedLocale, SimplifiedChinese, text, text.Length, mapped, mapped.Length);
            return written > 0 ? new string(mapped, 0, written) : text;
        }

        private static IntPtr CreateCFString(string text)
        {
            IntPtr characters = Marshal.StringToHGlobalUni(text);
            try { return CFStringCreateWithCharacters(IntPtr.Zero, characters, new IntPtr(text.Length)); }
            finally { Marshal.FreeHGlobal(characters); }
        }

        // The macOS offline runner previously returned all Han text unchanged.
        // Use the system ICU-backed Traditional-Simplified conversion there, while
        // retaining Windows NLS in production. No external runtime is installed;
        // unsupported Unix platforms safely keep the original text.
        private static string SimplifyWithCoreFoundation(string text)
        {
            IntPtr source = IntPtr.Zero, transform = IntPtr.Zero, mutable = IntPtr.Zero, buffer = IntPtr.Zero;
            try
            {
                source = CreateCFString(text);
                transform = CreateCFString("Traditional-Simplified");
                if (source == IntPtr.Zero || transform == IntPtr.Zero) return text;
                mutable = CFStringCreateMutableCopy(IntPtr.Zero, IntPtr.Zero, source);
                if (mutable == IntPtr.Zero || !CFStringTransform(mutable, IntPtr.Zero, transform, false)) return text;
                long length = CFStringGetLength(mutable).ToInt64();
                if (length < 0 || length > int.MaxValue / 2) return text;
                if (length == 0) return "";
                buffer = Marshal.AllocHGlobal(checked((int)length * 2));
                CFStringGetCharacters(mutable, new CFRange { Length = new IntPtr(length) }, buffer);
                return Marshal.PtrToStringUni(buffer, (int)length);
            }
            catch (DllNotFoundException) { return text; }
            catch (EntryPointNotFoundException) { return text; }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (mutable != IntPtr.Zero) CFRelease(mutable);
                if (transform != IntPtr.Zero) CFRelease(transform);
                if (source != IntPtr.Zero) CFRelease(source);
            }
        }

        internal static string Compact(string text)
        {
            return string.Concat(Simplify((text ?? "").Normalize(NormalizationForm.FormKC))
                .Where(character => !char.IsWhiteSpace(character)));
        }
    }

    internal sealed class OcrLine
    {
        internal Rectangle Box;
        internal string Text;
        // Preserve the engine output and quadrilateral before screen projection
        // and text normalization. Box/Text remain compatible with task callers.
        internal string RawText;
        internal PointF[] NativeQuad;
        internal float Score;
    }

    // PP-OCRv6 text detection and recognition through ONNX Runtime.
    internal sealed class OcrReader : IDisposable
    {
        private const int LimitSide = 960;
        private const int RecHeight = 48, RecWidth = 320;
        private const int RecBatch = 6;

        private sealed class TextRegion
        {
            internal PixelFrame Crop;
            internal Rectangle Bounds;
            internal PointF[] NativeQuad;
            internal int Index;
        }

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
            dictionary = File.ReadAllLines(Path.Combine(directory, "ppocrv6_dict.txt"));
            var withSpace = new List<string>(dictionary) { " " };
            dictionary = withSpace.ToArray();
            using (SessionOptions options = CreateCpuOptions())
            {
                det = new InferenceSession(Path.Combine(directory, "det.onnx"), options);
                try { rec = new InferenceSession(Path.Combine(directory, "rec.onnx"), options); }
                catch { det.Dispose(); throw; }
            }
            detInput = det.InputMetadata.Keys.First();
            recInput = rec.InputMetadata.Keys.First();
        }

        private static SessionOptions CreateCpuOptions()
        {
            // Exact reference CPU fallback explicitly selects CPUExecutionProvider
            // and ORT_ENABLE_ALL. Keep WW's bounded threads for concurrent readers.
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                EnableCpuMemArena = true,
                EnableMemoryPattern = true,
                IntraOpNumThreads = 2,
                InterOpNumThreads = 1
            };
            try { options.AppendExecutionProvider_CPU(1); return options; }
            catch { options.Dispose(); throw; }
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
            List<PointF[]> quads;
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
                det.Run(new[] { NamedOnnxValue.CreateFromTensor(detInput,
                    new DenseTensor<float>(input, new[] { 1, 3, resizedH, resizedW })) }))
            {
                Tensor<float> map = output.First().AsTensor<float>();
                // OnnxOCR 0.0.1 retains the original pre-padding image shape in shape_list.
                // DB coordinates are projected using the actual output map dimensions.
                quads = OcrDbPostprocess.Detect(map, region.Width, region.Height);
            }
            if (quads.Count == 0) return results;
            var regions = new List<TextRegion>();
            foreach (PointF[] quad in SortReferenceBoxes(quads))
            {
                PointF[] source = quad.Select(point => new PointF(point.X + region.X, point.Y + region.Y)).ToArray();
                Rectangle bounds = Rectangle.Intersect(QuadBounds(source), region);
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                // The reference receives an already-cropped ROI. Clamp interpolation
                // to that ROI, without making an additional full-image copy per box.
                PixelFrame crop = OcrRecognition.Crop(frame, source, region);
                if (crop.Width <= 0 || crop.Height <= 0) continue;
                regions.Add(new TextRegion { Crop = crop, Bounds = bounds, NativeQuad = source, Index = regions.Count });
            }
            // Recognition batches are ordered by the actual perspective-cropped width/height.
            // Restore the reference box order after inference, rather than sorting OCR results.
            var ordered = regions.OrderBy(item => item.Crop.Width / (double)item.Crop.Height).ToList();
            var recognized = new OcrLine[regions.Count];
            for (int start = 0; start < ordered.Count; start += RecBatch)
                Recognize(ordered.GetRange(start, Math.Min(RecBatch, ordered.Count - start)), recognized);
            foreach (OcrLine line in recognized)
                if (line != null && line.Score >= .5f)
                    results.Add(line);
            return results;
        }

        internal static List<PointF[]> SortReferenceBoxes(IEnumerable<PointF[]> boxes)
        {
            var ordered = boxes.OrderBy(box => box[0].Y).ThenBy(box => box[0].X).ToList();
            for (int i = 0; i < ordered.Count - 1; i++)
                for (int j = i; j >= 0; j--)
                {
                    PointF left = ordered[j][0], right = ordered[j + 1][0];
                    if (Math.Abs(right.Y - left.Y) >= 10 || right.X >= left.X) break;
                    PointF[] swap = ordered[j];
                    ordered[j] = ordered[j + 1];
                    ordered[j + 1] = swap;
                }
            return ordered;
        }

        internal static Rectangle QuadBounds(PointF[] box)
        {
            return Rectangle.FromLTRB((int)Math.Floor(box.Min(point => point.X)),
                (int)Math.Floor(box.Min(point => point.Y)), (int)Math.Ceiling(box.Max(point => point.X)),
                (int)Math.Ceiling(box.Max(point => point.Y)));
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

        internal static int RecognitionBatchWidth(IEnumerable<PixelFrame> crops)
        {
            double ratio = RecWidth / (double)RecHeight;
            foreach (PixelFrame crop in crops)
                ratio = Math.Max(ratio, crop.Width / (double)crop.Height);
            return checked((int)(RecHeight * ratio));
        }

        private void Recognize(List<TextRegion> regions, OcrLine[] results)
        {
            int width = RecognitionBatchWidth(regions.Select(region => region.Crop));
            int batchStride = checked(3 * RecHeight * width);
            var data = new float[checked(regions.Count * batchStride)];
            for (int i = 0; i < regions.Count; i++)
            {
                PixelFrame crop = regions[i].Crop;
                PrepareRecognition(crop, new Rectangle(0, 0, crop.Width, crop.Height), width, data, i * batchStride);
            }
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
                rec.Run(new[] { NamedOnnxValue.CreateFromTensor(recInput,
                    new DenseTensor<float>(data, new[] { regions.Count, 3, RecHeight, width })) }))
            {
                Tensor<float> logits = output.First().AsTensor<float>();
                for (int i = 0; i < regions.Count; i++)
                {
                    OcrLine line = OcrRecognition.Decode(logits, i, dictionary, regions[i].Bounds);
                    line.NativeQuad = regions[i].NativeQuad;
                    line.Text = OcrText.Simplify(line.RawText);
                    results[regions[i].Index] = line;
                }
            }
        }

        public void Dispose()
        {
            det.Dispose();
            rec.Dispose();
        }
    }
}
