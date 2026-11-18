using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TideCombat
{
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

        internal void Rgb(int x, int y, out int red, out int green, out int blue)
        {
            int offset = (y * Width + x) * 4;
            red = pixels[offset + 2];
            green = pixels[offset + 1];
            blue = pixels[offset];
        }
    }
}
