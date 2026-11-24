using System;
using System.Drawing;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TideCombat
{
    internal static class OcrRecognitionChecks
    {
        internal static void Run(Action<string, Action> check)
        {
            check("OCR recognition perspective crop preserves integer-aligned BGR pixels and replicated edges", AxisAndBorder);
            check("OCR recognition perspective uses projective rectification and 8-bit cubic samples", Perspective);
            check("OCR recognition rotates tall text crops 90 degrees counter-clockwise", Vertical);
            check("OCR recognition CTC preserves spaces, first-maximum ties, blanks and repeated labels", Ctc);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static PixelFrame Gradient(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    pixels[offset] = (byte)(x * 10);
                    pixels[offset + 1] = 90;
                    pixels[offset + 2] = (byte)(y * 20);
                    pixels[offset + 3] = 255;
                }
            return new PixelFrame(pixels, width, height);
        }

        private static PointF[] Quad(float left, float top, float right, float bottom)
        {
            return new[] { new PointF(left, top), new PointF(right, top), new PointF(right, bottom), new PointF(left, bottom) };
        }

        private static void Pixel(PixelFrame frame, int x, int y, int red, int green, int blue, string detail)
        {
            int r, g, b;
            frame.Rgb(x, y, out r, out g, out b);
            Require(r == red && g == green && b == blue,
                detail + " expected RGB " + red + "/" + green + "/" + blue + " but got " + r + "/" + g + "/" + b);
        }

        private static void AxisAndBorder()
        {
            var source = Gradient(12, 10);
            PixelFrame crop = OcrRecognition.Crop(source, Quad(2, 1, 8, 5));
            Require(crop.Width == 6 && crop.Height == 4,
                "destination uses the max edge distance, not an inclusive bounding rectangle");
            for (int y = 0; y < crop.Height; y++)
                for (int x = 0; x < crop.Width; x++)
                    Pixel(crop, x, y, (y + 1) * 20, 90, (x + 2) * 10, "integer-aligned source sample");

            crop = OcrRecognition.Crop(source, Quad(-2, -1, 3, 3));
            Require(crop.Width == 5 && crop.Height == 4, "out-of-image corners do not shift destination geometry");
            for (int y = 0; y < crop.Height; y++)
                for (int x = 0; x < crop.Width; x++)
                    Pixel(crop, x, y, Math.Max(0, y - 1) * 20, 90, Math.Max(0, x - 2) * 10, "replicated source border");

            crop = OcrRecognition.Crop(source, Quad(2.25f, 2.25f, 6.25f, 6.25f), new Rectangle(2, 2, 8, 6));
            Pixel(crop, 0, 0, 44, 90, 22, "fractional cubic sample replicates the OCR region edge, not surrounding screenshot");

            Require(OcrRecognition.Crop(source, Quad(1, 1, 1, 5)).Width == 0,
                "degenerate detector geometry must not allocate or recognize a crop");
        }

        private static void Perspective()
        {
            var source = Gradient(12, 10);
            // Bottom edge is ten pixels, the top only six. The destination
            // middle maps to source (5,4.25), not source (5,5) as an axis crop
            // or bilinear interpolation of the four vertices would imply.
            var quad = new[] { new PointF(2, 2), new PointF(8, 2), new PointF(10, 8), new PointF(0, 8) };
            PixelFrame crop = OcrRecognition.Crop(source, quad);
            Require(crop.Width == 10 && crop.Height == 6, "quad uses longer opposite edges for crop dimensions");
            Pixel(crop, 0, 0, 40, 90, 20, "top-left perspective sample");
            // INTER_CUBIC with A=-.75 at phase .25 yields 85.9375 on the
            // red gradient and rounds to 86; INTER_LINEAR would yield 85.
            Pixel(crop, 5, 3, 86, 90, 50, "projective 1/32-phase cubic sample");
        }

        private static void Vertical()
        {
            var source = Gradient(12, 10);
            PixelFrame crop = OcrRecognition.Crop(source, Quad(1, 0, 5, 6));
            Require(crop.Width == 6 && crop.Height == 4, "height/width == 1.5 rotates the crop exactly at the cutoff");
            for (int y = 0; y < crop.Height; y++)
                for (int x = 0; x < crop.Width; x++)
                    Pixel(crop, x, y, x * 20, 90, (4 - y) * 10, "counter-clockwise np.rot90 sample");
            crop = OcrRecognition.Crop(source, Quad(1, 0, 5, 5));
            Require(crop.Width == 4 && crop.Height == 5, "crop below vertical aspect threshold remains in its original orientation");
        }

        private static void Ctc()
        {
            const int steps = 9, classes = 4;
            var data = new float[2 * steps * classes];
            int[] labels = { 3, 3, 1, 1, 0, 1, 2, 3, 3 };
            float[] scores = { .9f, .99f, .8f, .99f, .99f, .6f, .5f, .7f, .99f };
            for (int t = 0; t < steps; t++)
            {
                data[t * classes + labels[t]] = scores[t];
                data[(steps + t) * classes] = .9f;
            }
            data[6 * classes + 3] = .5f;
            var tensor = new DenseTensor<float>(data, new[] { 2, steps, classes });
            var dictionary = new[] { "甲", "乙", " " };
            var region = new Rectangle(10, 20, 30, 50);
            OcrLine line = OcrRecognition.Decode(tensor, 0, dictionary, region);
            Require(line.Text == " 甲甲乙 " && line.Box == region && Math.Abs(line.Score - .7f) < 1e-6f,
                "CTC excludes blank and adjacent duplicate confidence, retains repeats separated by blank and raw space, chooses first max");
            OcrLine empty = OcrRecognition.Decode(tensor, 1, dictionary, region);
            Require(empty.Text == "" && empty.Score == 0, "a batch containing only blank has an empty text and zero score");

            OcrLine space = OcrRecognition.Decode(new DenseTensor<float>(new[] { .1f, .2f, .3f, .9f },
                new[] { 1, 1, classes }), 0, dictionary, region);
            Require(space.Text == " " && Math.Abs(space.Score - .9f) < 1e-6f,
                "the recognition decoder must not trim or drop a detected space");
        }
    }
}
