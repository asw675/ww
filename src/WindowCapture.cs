using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace TideCombat
{
    internal sealed class WindowCapture : IDisposable
    {
        private readonly FrameReader reader = new FrameReader();
        private Bitmap bitmap;

        internal bool TryRead(IntPtr window, Size size, out PixelFrame frame, bool retainEcho = false)
        {
            frame = default(PixelFrame);
            if (size.IsEmpty || Native.IsIconic(window) || Native.IsHungAppWindow(window)) return false;
            if (bitmap == null || bitmap.Size != size)
            {
                if (bitmap != null) bitmap.Dispose();
                bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
            }
            if (Print(window, 3) && ReadVisible(out frame, retainEcho)) return true;
            return Print(window, 1) && ReadVisible(out frame, retainEcho);
        }

        private bool Print(IntPtr window, uint flags)
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                IntPtr dc = graphics.GetHdc();
                try { return Native.PrintWindow(window, dc, flags); }
                finally { graphics.ReleaseHdc(dc); }
            }
        }

        private bool ReadVisible(out PixelFrame frame, bool retainEcho)
        {
            frame = reader.Read(bitmap, retainEcho);
            for (int y = 16; y < frame.Height; y += 64)
                for (int x = 16; x < frame.Width; x += 64)
                    if (frame.Light(x, y) > 8) return true;
            return false;
        }

        public void Dispose()
        {
            if (bitmap != null) bitmap.Dispose();
            reader.Dispose();
        }
    }
}
