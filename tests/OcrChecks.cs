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
                    if (full[i].Text != projected[i].Text || projected[i].Box != target)
                        throw new InvalidOperationException("Native OCR text or its projected click bounds have changed.");
                }
            }
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
    }
}
