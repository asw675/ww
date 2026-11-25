using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal struct GardenRewardCard
    {
        internal string Title;
        internal Rectangle Bounds, Header, SafeClick;
    }

    internal static class GardenRewardCards
    {
        internal static List<GardenRewardCard> Read(PixelFrame frame, OcrReader ocr)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || ocr == null) return new List<GardenRewardCard>();
            return Read(frame, ocr.ReadRelative(frame, .153, .162, .855, .224, centered: true));
        }

        internal static List<GardenRewardCard> Read(PixelFrame frame, IList<OcrLine> titles)
        {
            var cards = new List<GardenRewardCard>();
            if (frame.Width <= 0 || frame.Height <= 0 || titles == null || titles.Count == 0) return cards;

            Rectangle layout = CenteredLayout(frame);
            // The lower card body has a continuous pale paper surface in each column.
            Rectangle scan = Region(layout, .145, .48, .87, .65);
            int minimumWidth = layout.Width * 16 / 100, maximumWidth = layout.Width * 24 / 100;
            int sampleStep = Math.Max(1, layout.Height / 120);
            int x = scan.Left;
            while (x < scan.Right)
            {
                if (!HasPaperColumn(frame, scan, x, sampleStep))
                {
                    x++;
                    continue;
                }
                int left = x++;
                while (x < scan.Right && HasPaperColumn(frame, scan, x, sampleStep)) x++;
                if (x - left < minimumWidth || x - left > maximumWidth) continue;
                GardenRewardCard card;
                if (TryReadCard(frame, layout, titles, left, x, out card))
                    cards.Add(card);
            }
            return cards;
        }

        private static bool TryReadCard(PixelFrame frame, Rectangle layout, IList<OcrLine> titles, int left, int right,
            out GardenRewardCard card)
        {
            card = default(GardenRewardCard);
            Rectangle nameArea = Region(layout, .145, .153, .87, .229);
            nameArea = Rectangle.Intersect(nameArea, Rectangle.FromLTRB(left, nameArea.Top, right, nameArea.Bottom));
            OcrLine recognized = null;
            foreach (OcrLine line in titles)
            {
                if (line.Box.IsEmpty || !nameArea.Contains(line.Box) || line.Score < .74f ||
                    (line.Text ?? string.Empty).Trim().Length < 2) continue;
                if (recognized != null) return false;
                recognized = line;
            }
            if (recognized == null) return false;

            int bottom = ReadPaperBottom(frame, layout, left, right);
            int headerTop = Region(layout, 0, .139, 1, 1).Top;
            int headerBottom = Region(layout, 0, .245, 1, 1).Top;
            int clickMargin = (right - left) / 9;
            int titleY = recognized.Box.Top + recognized.Box.Height / 2;
            int clickHalfHeight = Math.Min(Math.Max(1, layout.Height / 100), recognized.Box.Height / 2);
            if (bottom <= Region(layout, 0, .66, 1, 1).Top || clickHalfHeight <= 0) return false;
            card = new GardenRewardCard
            {
                Title = GardenMath.Normalize(recognized.Text),
                Bounds = Rectangle.FromLTRB(left, headerTop, right, bottom),
                Header = Rectangle.FromLTRB(left, headerTop, right, headerBottom),
                SafeClick = Rectangle.FromLTRB(left + clickMargin, titleY - clickHalfHeight,
                    right - clickMargin, titleY + clickHalfHeight)
            };
            return true;
        }

        private static int ReadPaperBottom(PixelFrame frame, Rectangle layout, int left, int right)
        {
            Rectangle lower = Region(layout, 0, .639, 1, .706);
            int bottom = 0;
            int margin = (right - left) / 12;
            int step = Math.Max(1, (right - left) / 10);
            for (int y = lower.Top; y < lower.Bottom; y++)
            {
                int count = 0, samples = 0;
                for (int x = left + margin; x < right - margin; x += step)
                {
                    if (IsPaper(frame, x, y)) count++;
                    samples++;
                }
                if (count * 4 < samples * 3) break;
                bottom = y + 1;
            }
            return bottom;
        }

        private static bool HasPaperColumn(PixelFrame frame, Rectangle area, int x, int sampleStep)
        {
            int matching = 0, samples = 0;
            for (int y = area.Top; y < area.Bottom; y += sampleStep)
            {
                if (IsPaper(frame, x, y)) matching++;
                samples++;
            }
            return matching * 4 >= samples * 3;
        }

        private static bool IsPaper(PixelFrame frame, int x, int y)
        {
            int red, green, blue;
            frame.Rgb(x, y, out red, out green, out blue);
            return red >= 188 && green >= 181 && blue >= 157 &&
                Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) <= 52;
        }

        private static Rectangle CenteredLayout(PixelFrame frame)
        {
            int width = Math.Min(frame.Width, (int)Math.Round(frame.Height * (16.0 / 9)));
            int height = Math.Min(frame.Height, (int)Math.Round(width * (9.0 / 16)));
            return new Rectangle((frame.Width - width) / 2, (frame.Height - height) / 2, width, height);
        }

        private static Rectangle Region(Rectangle layout, double left, double top, double right, double bottom)
        {
            return Rectangle.FromLTRB(layout.Left + (int)Math.Round(layout.Width * left),
                layout.Top + (int)Math.Round(layout.Height * top),
                layout.Left + (int)Math.Round(layout.Width * right),
                layout.Top + (int)Math.Round(layout.Height * bottom));
        }
    }
}
