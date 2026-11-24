using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TideCombat
{
    // Saves a cropped frame for later review; failures are non-fatal.
    internal static class Screenshots
    {
        internal static void Save(PixelFrame frame, string kind, string reason, Rectangle ratioArea)
        {
            try
            {
                var area = new Rectangle(
                    (int)(frame.Width * ratioArea.X / 1600.0), (int)(frame.Height * ratioArea.Y / 900.0),
                    Math.Max(1, (int)(frame.Width * ratioArea.Width / 1600.0)),
                    Math.Max(1, (int)(frame.Height * ratioArea.Height / 900.0)));
                area = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
                if (area.Width < 4 || area.Height < 4)
                    return;
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots", kind);
                Directory.CreateDirectory(folder);
                string name = DateTime.Now.ToString("HHmmss_fff") +
                    (string.IsNullOrEmpty(reason) ? string.Empty : "_" + Sanitize(reason));
                WritePng(frame, area, Path.Combine(folder, name + ".png"));
            }
            catch (Exception) { }
        }

        internal static void WritePng(PixelFrame frame, Rectangle area, string path)
        {
            var bytes = new byte[checked(area.Width * area.Height * 4)];
            frame.CopyBgra(area, bytes);
            using (var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb))
            {
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, area.Width, area.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try { Marshal.Copy(bytes, 0, data.Scan0, bytes.Length); }
                finally { bitmap.UnlockBits(data); }
                using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    bitmap.Save(output, ImageFormat.Png);
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char character in value)
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
            return builder.ToString();
        }
    }
}
