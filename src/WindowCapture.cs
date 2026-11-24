using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace TideCombat
{
    internal sealed class WindowCapture : IDisposable
    {
        private readonly FrameReader reader = new FrameReader();
        private Bitmap bitmap, fullBitmap;
        internal string LastMethod { get; private set; }

        internal bool TryRead(IntPtr window, uint expectedOwner, Size size, out PixelFrame frame, bool retainEcho = false)
        {
            frame = default(PixelFrame);
            LastMethod = null;
            if (size.IsEmpty || !GameWindow.Owns(window, expectedOwner) || Native.IsIconic(window) || Native.IsHungAppWindow(window)) return false;
            if (bitmap == null || bitmap.Size != size)
            {
                if (bitmap != null) bitmap.Dispose();
                bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
            }
            if (PrintClient(window, expectedOwner, bitmap, size, 3) && ReadVisible(bitmap, new Rectangle(Point.Empty, size), out frame, retainEcho))
            {
                LastMethod = "PrintWindow RenderFullContent+ClientOnly";
                return true;
            }
            if (TryRenderFull(window, expectedOwner, size, out frame, retainEcho))
            {
                LastMethod = "PrintWindow RenderFullContent（客户端裁剪）";
                return true;
            }
            if (PrintClient(window, expectedOwner, bitmap, size, 1) && ReadVisible(bitmap, new Rectangle(Point.Empty, size), out frame, retainEcho))
            {
                LastMethod = "PrintWindow ClientOnly";
                return true;
            }
            frame = default(PixelFrame);
            return false;
        }

        // A complete-window render supplies a distinct fallback when drivers
        // do not honor ClientOnly with RenderFullContent. Render only the
        // verified target HWND into our own bitmap and extract its client
        // bounds; never BitBlt the desktop or an occluding application.
        private bool TryRenderFull(IntPtr window, uint expectedOwner, Size size, out PixelFrame frame, bool retainEcho)
        {
            frame = default(PixelFrame);
            Rectangle client, outer;
            if (!GameWindow.Owns(window, expectedOwner) || !ReadGeometry(window, out client, out outer)) return false;
            Rectangle crop = FullRenderCrop(client, outer, size);
            if (crop.IsEmpty) return false;
            if (fullBitmap == null || fullBitmap.Size != outer.Size)
            {
                if (fullBitmap != null) fullBitmap.Dispose();
                fullBitmap = new Bitmap(outer.Width, outer.Height, PixelFormat.Format32bppRgb);
            }
            if (!Print(window, expectedOwner, fullBitmap, 2)) return false;
            Rectangle afterClient, afterOuter;
            return GameWindow.Owns(window, expectedOwner) && !Native.IsIconic(window) &&
                ReadGeometry(window, out afterClient, out afterOuter) && afterClient == client && afterOuter == outer &&
                ReadVisible(fullBitmap, crop, out frame, retainEcho);
        }

        internal static Rectangle FullRenderCrop(Rectangle client, Rectangle outer, Size expected)
        {
            if (expected.IsEmpty || client.Size != expected || outer.Width <= 0 || outer.Height <= 0 ||
                (long)outer.Width * outer.Height > 64L * 1024 * 1024 || !outer.Contains(client)) return Rectangle.Empty;
            return GraphicsCapture.CropToClient(client, outer, outer.Size);
        }

        private static bool ReadGeometry(IntPtr window, out Rectangle client, out Rectangle outer)
        {
            client = outer = Rectangle.Empty;
            Native.Rect nativeClient, nativeOuter;
            var origin = new Native.Point();
            if (!Native.GetClientRect(window, out nativeClient) || !Native.ClientToScreen(window, ref origin) ||
                !Native.GetWindowRect(window, out nativeOuter)) return false;
            client = new Rectangle(origin.X, origin.Y, nativeClient.Right - nativeClient.Left, nativeClient.Bottom - nativeClient.Top);
            outer = Rectangle.FromLTRB(nativeOuter.Left, nativeOuter.Top, nativeOuter.Right, nativeOuter.Bottom);
            return true;
        }

        private static bool Print(IntPtr window, uint expectedOwner, Bitmap target, uint flags)
        {
            if (!GameWindow.Owns(window, expectedOwner) || Native.IsIconic(window) || Native.IsHungAppWindow(window)) return false;
            using (Graphics graphics = Graphics.FromImage(target))
            {
                graphics.Clear(Color.Black);
                IntPtr dc = graphics.GetHdc();
                try { return GameWindow.Owns(window, expectedOwner) && Native.PrintWindow(window, dc, flags) &&
                    GameWindow.Owns(window, expectedOwner); }
                finally { graphics.ReleaseHdc(dc); }
            }
        }

        private static bool PrintClient(IntPtr window, uint expectedOwner, Bitmap target, Size size, uint flags)
        {
            return MatchesClientSize(window, size) && Print(window, expectedOwner, target, flags) && MatchesClientSize(window, size);
        }

        private static bool MatchesClientSize(IntPtr window, Size size)
        {
            Native.Rect client;
            return Native.GetClientRect(window, out client) &&
                new Size(client.Right - client.Left, client.Bottom - client.Top) == size;
        }

        private bool ReadVisible(Bitmap source, Rectangle crop, out PixelFrame frame, bool retainEcho)
        {
            frame = reader.Read(source, crop, retainEcho);
            for (int y = 16; y < frame.Height; y += 64)
                for (int x = 16; x < frame.Width; x += 64)
                    if (frame.Light(x, y) > 8) return true;
            return false;
        }

        public void Dispose()
        {
            if (bitmap != null) bitmap.Dispose();
            if (fullBitmap != null) fullBitmap.Dispose();
            reader.Dispose();
        }
    }
}
