using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TideCombat
{
    internal struct LinearAxis
    {
        internal int Lower, Upper, FirstWeight, SecondWeight;

        internal LinearAxis(int position, int sourceLength, int targetLength, bool horizontal, int offset = 0)
        {
            float source = (float)((position + .5) * ((double)sourceLength / targetLength) - .5);
            int lower = (int)Math.Floor(source);
            float fraction = source - lower;
            if (horizontal && (lower < 0 || lower >= sourceLength - 1)) fraction = 0;
            Lower = offset + Math.Max(0, Math.Min(sourceLength - 1, lower));
            Upper = offset + Math.Max(0, Math.Min(sourceLength - 1, lower + 1));
            FirstWeight = (int)Math.Round((1 - fraction) * 2048);
            SecondWeight = (int)Math.Round(fraction * 2048);
        }

        private static int Interpolate(int a, int b, int c, int d, LinearAxis x, LinearAxis y)
        {
            // INTER_LINEAR on 8-bit channels uses 11-bit weights and quantizes before normalization.
            int top = (a * x.FirstWeight + b * x.SecondWeight) >> 4;
            int bottom = (c * x.FirstWeight + d * x.SecondWeight) >> 4;
            int sample = (((top * y.FirstWeight) >> 16) + ((bottom * y.SecondWeight) >> 16) + 2) >> 2;
            return sample;
        }

        internal static void Sample(PixelFrame frame, LinearAxis column, LinearAxis row, out int red, out int green, out int blue)
        {
            int r00, g00, b00, r01, g01, b01, r10, g10, b10, r11, g11, b11;
            frame.Rgb(column.Lower, row.Lower, out r00, out g00, out b00);
            frame.Rgb(column.Upper, row.Lower, out r01, out g01, out b01);
            frame.Rgb(column.Lower, row.Upper, out r10, out g10, out b10);
            frame.Rgb(column.Upper, row.Upper, out r11, out g11, out b11);
            red = Interpolate(r00, r01, r10, r11, column, row);
            green = Interpolate(g00, g01, g10, g11, column, row);
            blue = Interpolate(b00, b01, b10, b11, column, row);
        }
    }

    internal sealed class FramePatch
    {
        private byte[] pixels;
        internal int Width { get; private set; }
        internal int Height { get; private set; }

        internal static Rectangle ScaleCentered(Rectangle viewport, int width, int height, int left, int top, int right, int bottom)
        {
            double scale = (double)viewport.Height / height;
            double center = viewport.Left + viewport.Width / 2.0;
            int x = (int)(center + (left - width / 2.0) * scale);
            int y = viewport.Top + (int)(top * scale);
            return new Rectangle(x, y, Math.Max(1, (int)((right - left) * scale)),
                Math.Max(1, (int)((bottom - top) * scale)));
        }

        internal void Read(Bitmap bitmap, Rectangle bounds)
        {
            bounds.Intersect(new Rectangle(Point.Empty, bitmap.Size));
            Width = bounds.Width;
            Height = bounds.Height;
            if (Width == 0 || Height == 0) return;
            int scanline = Width * 4;
            if (pixels == null || pixels.Length != scanline * Height) pixels = new byte[scanline * Height];
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * scanline, scanline);
            }
            finally { bitmap.UnlockBits(data); }
        }

        internal PixelFrame Pixels { get { return new PixelFrame(pixels, Width, Height); } }

        internal void Rgb(int x, int y, out int red, out int green, out int blue)
        {
            int offset = (y * Width + x) * 4;
            red = pixels[offset + 2];
            green = pixels[offset + 1];
            blue = pixels[offset];
        }
    }
}
