using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal static class OcrChecks
    {
        internal static void DetectionScore()
        {
            float[,] patch = {
                {0,0,0,.04f,.07f,.09f,.04f,0,0,0}, {0,0,.01f,.19f,.35f,.24f,.12f,0,0,0},
                {0,0,.04f,.60f,.78f,.70f,.29f,.01f,0,0}, {0,0,.12f,.79f,.91f,.81f,.57f,.01f,0,0},
                {0,0,.16f,.74f,.79f,.77f,.33f,.01f,0,0}, {0,0,.16f,.60f,.72f,.55f,.16f,.01f,0,0},
                {0,0,.01f,.33f,.42f,.23f,.04f,0,0,0}, {0,0,0,.04f,.04f,.05f,.01f,0,0,0}
            };
            const int width = 64, height = 32;
            var map = new float[width * height];
            var pixels = new List<Point>();
            for (int y = 0; y < patch.GetLength(0); y++)
                for (int x = 0; x < patch.GetLength(1); x++)
                {
                    var p = new Point(x + 37, y + 10);
                    map[p.Y * width + p.X] = patch[y, x];
                    if (patch[y, x] > .3f) pixels.Add(p);
                }
            PointF[] box;
            if (!OcrDetectionBox.Fit(pixels, out box))
                throw new InvalidOperationException("Expected the small slanted component to reach the scoring stage.");
            double score = OcrDetectionBox.Score(new DenseTensor<float>(map, new[] {1,1,height,width}), width, height, box);
            if (Math.Abs(score - .476) > .005)
                throw new InvalidOperationException("Activity counter decoration must score below 0.5 across the full box.");
        }

        internal static void NarrowRotatedBox()
        {
            var pixels = new List<Point>();
            for (int row = 0; row < 4; row++)
            {
                int left = row < 2 ? 46 : 45;
                for (int x = left; x < left + 3; x++) pixels.Add(new Point(x, 55 + row));
            }
            PointF[] corners;
            if (OcrDetectionBox.Fit(pixels, out corners))
                throw new InvalidOperationException("Tilted component with a 2.24-pixel short side passed the 3-pixel size gate.");
        }

        internal static void NativeCrop(string root)
        {
            using (var image = new Bitmap(Path.Combine(root, "tests", "images", "echo_enhance.png")))
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            {
                PixelFrame frame = reader.Read(image, true);
                PixelFrame native = frame.FullResolution;
                var pixels = new byte[native.Width * native.Height * 4];
                native.CopyBgra(new Rectangle(0, 0, native.Width, native.Height), pixels);
                var blankDisplay = new PixelFrame(new byte[frame.Width * frame.Height * 4], frame.Width, frame.Height,
                    sourceWidth: native.Width, sourceHeight: native.Height, echoPixels: pixels);
                var full = ocr.ReadRelative(native, .72, .12, .99, .85);
                var projected = ocr.ReadRelative(blankDisplay, .72, .12, .99, .85);
                if (full.Count == 0 || projected.Count != full.Count)
                    throw new InvalidOperationException("Expected native screenshot text from the selected region.");
                double x = frame.Width / (double)native.Width, y = frame.Height / (double)native.Height;
                for (int i = 0; i < full.Count; i++)
                {
                    Rectangle box = full[i].Box;
                    Rectangle target = Rectangle.FromLTRB((int)Math.Round(box.Left * x), (int)Math.Round(box.Top * y),
                        (int)Math.Round(box.Right * x), (int)Math.Round(box.Bottom * y));
                    if (full[i].Text != projected[i].Text || full[i].RawText != projected[i].RawText ||
                        full[i].RawText == null || full[i].NativeQuad == null || projected[i].NativeQuad == null ||
                        full[i].NativeQuad.Length != 4 || projected[i].NativeQuad.Length != 4 || projected[i].Box != target)
                        throw new InvalidOperationException("Native OCR text or its projected click bounds have changed.");
                    for (int j = 0; j < 4; j++)
                        if (full[i].NativeQuad[j] != projected[i].NativeQuad[j])
                            throw new InvalidOperationException("Raw native quadrilateral must not be rounded into the displayed bounding rectangle.");
                }
            }
        }

        internal static void TextNormalization()
        {
            const string raw = "領取獎勵 ＡＢＣ１２";
            string simplified = OcrText.Simplify(raw);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT ||
                Directory.Exists("/System/Library/Frameworks/CoreFoundation.framework"))
            {
                if (simplified != "领取奖励 ＡＢＣ１２" || OcrText.Compact(raw) != "领取奖励ABC12")
                    throw new InvalidOperationException("Traditional Chinese OCR normalization must retain Latin/digits and be usable on Windows and macOS: " + simplified);
            }
            if (OcrText.Simplify(simplified) != simplified || OcrText.Simplify("ＡＢＣ１２-abc") != "ＡＢＣ１２-abc" ||
                OcrText.Simplify("领取奖励") != "领取奖励" || OcrText.Simplify(null) != "")
                throw new InvalidOperationException("Simplification must be idempotent; no translation/compatibility normalization of non-Han text.");
            const string supplementary = "\U00020000-A12";
            string preserved = OcrText.Simplify(supplementary);
            if (preserved == null || !preserved.EndsWith("-A12", StringComparison.Ordinal) || char.IsHighSurrogate(preserved[0]) &&
                (preserved.Length < 2 || !char.IsLowSurrogate(preserved[1])))
                throw new InvalidOperationException("Supplementary Han text must not split a surrogate or alter adjacent ASCII characters.");
        }

        internal static void Inputs()
        {
            byte[] pixels = { 0, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255 };
            var frame = new PixelFrame(pixels, 2, 2);
            var crop = new Rectangle(0, 0, 2, 2);
            int width, height;
            float[] detection = OcrReader.PrepareDet(frame, crop, out width, out height);
            if (width != 32 || height != 32 || detection[1] != (1f - .485f) / .229f ||
                detection[32 * 32 + 32] != (1f - .456f) / .224f ||
                detection[2 * 32 * 32 + 33] != (1f - .406f) / .225f || detection[34] != -.485f / .229f)
                throw new InvalidOperationException("Small detection crop must retain its input pixels and black border.");

            const int stride = 64, plane = 48 * stride, offset = 7;
            var recognition = new float[offset + 3 * plane];
            OcrReader.PrepareRecognition(frame, crop, stride, recognition, offset);
            int center = offset + 24 * stride + 24;
            if (recognition[offset] != -1 || recognition[offset + 47] != 1 || recognition[offset + 48] != 0 ||
                Math.Abs(recognition[center] - (64.0 / 255 * 2 - 1)) > 1e-7 || recognition[center + plane] != recognition[center] ||
                Math.Abs(recognition[center + 2 * plane] - (69.0 / 255 * 2 - 1)) > 1e-7 || recognition[offset - 1] != 0)
                throw new InvalidOperationException("Recognition crop must use bilinear BGR samples, signed values and zero padding.");
        }

        internal static void ReferenceOrdering()
        {
            // OnnxOCR uses the top-left vertex, not the axis-aligned bounding box.
            PointF[] right = RectQuad(50, 10, 10, 30), left = RectQuad(4, 18, 10, 6);
            PointF[] next = RectQuad(0, 28, 10, 6), same = RectQuad(80, 10, 5, 5);
            List<PointF[]> sorted = OcrReader.SortReferenceBoxes(new[] { next, same, left, right });
            if (sorted.Count != 4 || !ReferenceEquals(sorted[0], left) || !ReferenceEquals(sorted[1], right) ||
                !ReferenceEquals(sorted[2], same) || !ReferenceEquals(sorted[3], next))
                throw new InvalidOperationException("Reference ordering is stable top-left y/x with adjacent row swaps only for differences below 10 pixels.");
            PointF[] tilted = { new PointF(30, 20), new PointF(50, 25), new PointF(45, 35), new PointF(26, 30) };
            if (OcrReader.QuadBounds(tilted) != Rectangle.FromLTRB(26, 20, 50, 35))
                throw new InvalidOperationException("Coordinates must retain the four-point source box envelope, not recognition-crop dimensions.");
            var square = new PixelFrame(new byte[4 * 16 * 16], 16, 16);
            var wide = new PixelFrame(new byte[4 * 100 * 7], 100, 7);
            if (OcrReader.RecognitionBatchWidth(new[] { square }) != 320 ||
                OcrReader.RecognitionBatchWidth(new[] { square, wide }) != (int)(48 * (100.0 / 7)))
                throw new InvalidOperationException("Reference recognition uses the actual rectified crop aspect ratios and integer batch width.");
        }

        private static PointF[] RectQuad(int x, int y, int width, int height)
        {
            return new[] { new PointF(x, y), new PointF(x + width, y), new PointF(x + width, y + height), new PointF(x, y + height) };
        }
    }
}
