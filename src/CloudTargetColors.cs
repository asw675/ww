using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TideCombat
{
    internal sealed class CloudTargetColors
    {
        private Size sourceSize;
        private Rectangle bounds;
        private byte[] row;
        private int[] gold;
        private int stride;

        internal static Rectangle SearchBounds(int width, int height)
        {
            return Rectangle.FromLTRB((int)(width * .62), (int)(height * .85), (int)(width * .84), (int)(height * .95));
        }

        internal void Read(Bitmap source, Rectangle crop)
        {
            sourceSize = crop.Size;
            bounds = SearchBounds(sourceSize.Width, sourceSize.Height);
            stride = bounds.Width + 1;
            int length = stride * (bounds.Height + 1);
            if (gold == null || gold.Length != length) gold = new int[length];
            if (row == null || row.Length != bounds.Width * 4) row = new byte[bounds.Width * 4];
            Rectangle pixels = bounds;
            pixels.Offset(crop.Location);
            BitmapData data = source.LockBits(pixels, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bounds.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    int count = 0, current = (y + 1) * stride;
                    for (int x = 0, channel = 0; x < bounds.Width; x++, channel += 4)
                    {
                        if (IsGold(row[channel + 2], row[channel + 1], row[channel])) count++;
                        gold[current + x + 1] = count + gold[current + x + 1 - stride];
                    }
                }
            }
            finally { source.UnlockBits(data); }
        }

        internal bool HasGold(Rectangle normalized, int width, int height)
        {
            var actual = Rectangle.FromLTRB(
                (int)Math.Round(sourceSize.Width * (double)normalized.Left / width),
                (int)Math.Round(sourceSize.Height * (double)normalized.Top / height),
                (int)Math.Round(sourceSize.Width * (double)normalized.Right / width),
                (int)Math.Round(sourceSize.Height * (double)normalized.Bottom / height));
            actual.Intersect(bounds);
            if (actual.Width <= 0 || actual.Height <= 0) return false;
            actual.Offset(-bounds.X, -bounds.Y);
            int top = actual.Top * stride, bottom = actual.Bottom * stride;
            int count = gold[bottom + actual.Right] - gold[bottom + actual.Left] -
                gold[top + actual.Right] + gold[top + actual.Left];
            return count * 50 >= actual.Width * actual.Height;
        }

        internal static bool IsGold(int r, int g, int b)
        {
            int high = Math.Max(r, Math.Max(g, b)), chroma = high - b;
            if (high < 150 || chroma * 510 < high * 179 || b > r || b > g) return false;
            // Convert to the reference HSV scale, including rounding to whole hue bins.
            int doubledHue = high == r ? 60 * (g - b) : 120 * chroma + 60 * (b - r);
            return doubledHue >= 29 * chroma && doubledHue < 71 * chroma;
        }
    }
}
