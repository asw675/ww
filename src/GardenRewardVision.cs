using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TideCombat
{
    internal struct GardenRewardPopup
    {
        internal Rectangle Skip, Confirm, Hide;
    }

    internal static class GardenRewardVision
    {
        internal static bool TryRead(PixelFrame frame, OcrReader ocr, out GardenRewardPopup popup)
        {
            popup = default(GardenRewardPopup);
            if (frame.Width <= 0 || frame.Height <= 0 || ocr == null) return false;
            return TryRead(ocr.ReadRelative(frame, .36, .73, .86, .96, centered: true),
                new Size(frame.Width, frame.Height), out popup);
        }

        internal static bool TryRead(IEnumerable<OcrLine> lines, Size size, out GardenRewardPopup popup)
        {
            popup = default(GardenRewardPopup);
            if (size.Width <= 0 || size.Height <= 0) return false;
            Rectangle skip = FindButton(lines, Region(size, .52, .75, .66, .86), "跳过", "跳過", "Skip");
            Rectangle confirm = FindButton(lines, Region(size, .70, .75, .84, .86), "确认", "確認", "Confirm");
            Rectangle hide = FindButton(lines, Region(size, .38, .87, .62, .955),
                "隐藏当前界面", "隱藏當前界面", "隱藏當前介面", "Hide Current Screen", "Hide Current Interface");
            popup = new GardenRewardPopup { Skip = skip, Confirm = confirm, Hide = hide };
            return !skip.IsEmpty && !confirm.IsEmpty && !hide.IsEmpty;
        }

        internal static bool HasSelectedCard(PixelFrame frame, GardenRewardPopup popup)
        {
            Rectangle card;
            return TryFindSelectedCard(frame, popup, out card);
        }

        internal static bool IsConfirmEnabled(PixelFrame frame, GardenRewardPopup popup)
        {
            return frame.Width > 0 && frame.Height > 0 && !popup.Confirm.IsEmpty &&
                HasConfirmFill(frame, popup.Confirm, Region(new Size(frame.Width, frame.Height), 0, 0, 1, 1).Size,
                    IsSelectionGreen);
        }

        internal static bool IsConfirmDisabled(PixelFrame frame, GardenRewardPopup popup)
        {
            return frame.Width > 0 && frame.Height > 0 && !popup.Confirm.IsEmpty &&
                HasConfirmFill(frame, popup.Confirm, Region(new Size(frame.Width, frame.Height), 0, 0, 1, 1).Size,
                    (red, green, blue) => green >= 115 && green <= 157 && green - red >= 14 && green - blue >= 34);
        }

        internal static int CardPriority(PixelFrame frame, Rectangle header)
        {
            var band = Rectangle.FromLTRB(header.Right - header.Width / 4, header.Top + header.Height / 3,
                header.Right - header.Width / 12, header.Top + header.Height * 2 / 3);
            if (HasFill(frame, band, (red, green, blue) =>
                red >= 205 && green >= 150 && red - green >= 18 && green - blue >= 32)) return 0;
            if (HasFill(frame, band, (red, green, blue) =>
                red >= 145 && red - green >= 24 && blue - green >= 32 && blue - red >= 8)) return 1;
            return 2;
        }

        internal static bool TryFindSelectedCard(PixelFrame frame, GardenRewardPopup popup, out Rectangle card)
        {
            card = Rectangle.Empty;
            if (frame.Width <= 0 || frame.Height <= 0 || popup.Skip.IsEmpty || popup.Confirm.IsEmpty ||
                popup.Hide.IsEmpty) return false;
            var size = new Size(frame.Width, frame.Height);
            Rectangle layout = Region(size, 0, 0, 1, 1);
            if (!HasConfirmFill(frame, popup.Confirm, layout.Size, IsSelectionGreen)) return false;

            // A selected offer has a continuous green lower edge and two green
            // rails down the card. Orange mouse focus does not enable Confirm.
            Rectangle lowerEdge = Region(size, .12, .68, .90, .73);
            Rectangle rails = Region(size, .12, .18, .90, .64);
            int minimumWidth = layout.Width * 16 / 100, maximumWidth = layout.Width * 29 / 100;
            int tolerance = Math.Max(2, layout.Width / 200);
            Rectangle selected = Rectangle.Empty;
            for (int y = lowerEdge.Top; y < lowerEdge.Bottom; y++)
            {
                int x = lowerEdge.Left;
                while (x < lowerEdge.Right)
                {
                    if (!IsSelectionGreen(frame, x, y))
                    {
                        x++;
                        continue;
                    }
                    int start = x;
                    while (x < lowerEdge.Right && IsSelectionGreen(frame, x, y)) x++;
                    int width = x - start;
                    if (width < minimumWidth || width > maximumWidth ||
                        !HasGreenRail(frame, start, rails, tolerance) || !HasGreenRail(frame, x - 1, rails, tolerance))
                        continue;
                    var candidate = Rectangle.FromLTRB(start, layout.Top + layout.Height / 8, x, y + 1);
                    if (!selected.IsEmpty && !selected.IntersectsWith(candidate)) return false;
                    selected = candidate;
                }
            }
            card = selected;
            return !card.IsEmpty;
        }

        private static bool HasConfirmFill(PixelFrame frame, Rectangle confirm, Size layout,
            Func<int, int, int, bool> matches)
        {
            int centerX = confirm.Left + confirm.Width / 2, centerY = confirm.Top + confirm.Height / 2;
            int outer = layout.Width * 8 / 100, inner = layout.Width * 4 / 100;
            int halfHeight = layout.Height / 45;
            if (outer <= inner || halfHeight == 0 || centerX < outer || centerX + outer > frame.Width ||
                centerY < halfHeight || centerY + halfHeight > frame.Height) return false;
            Rectangle left = Rectangle.FromLTRB(centerX - outer, centerY - halfHeight, centerX - inner, centerY + halfHeight);
            Rectangle right = Rectangle.FromLTRB(centerX + inner, centerY - halfHeight, centerX + outer, centerY + halfHeight);
            return HasFill(frame, left, matches) && HasFill(frame, right, matches);
        }

        private static bool HasFill(PixelFrame frame, Rectangle area, Func<int, int, int, bool> matches)
        {
            if (area.Width <= 0 || area.Height <= 0 || !new Rectangle(0, 0, frame.Width, frame.Height).Contains(area))
                return false;
            int matching = 0, samples = 0;
            for (int y = area.Top; y < area.Bottom; y += 2)
                for (int x = area.Left; x < area.Right; x += 2)
                {
                    int red, green, blue;
                    frame.Rgb(x, y, out red, out green, out blue);
                    samples++;
                    if (matches(red, green, blue)) matching++;
                }
            return matching * 3 >= samples * 2;
        }

        private static bool HasGreenRail(PixelFrame frame, int x, Rectangle verticalArea, int tolerance)
        {
            int present = 0, rows = 0;
            for (int y = verticalArea.Top; y < verticalArea.Bottom; y += 4)
            {
                rows++;
                for (int offset = -tolerance; offset <= tolerance; offset++)
                    if (IsSelectionGreen(frame, x + offset, y))
                    {
                        present++;
                        break;
                    }
            }
            return present * 5 >= rows * 4;
        }

        private static bool IsSelectionGreen(PixelFrame frame, int x, int y)
        {
            int red, green, blue;
            frame.Rgb(x, y, out red, out green, out blue);
            return IsSelectionGreen(red, green, blue);
        }

        private static bool IsSelectionGreen(int red, int green, int blue)
        {
            return green >= 165 && green - red >= 18 && green - blue >= 40;
        }

        private static Rectangle FindButton(IEnumerable<OcrLine> lines, Rectangle area, params string[] labels)
        {
            return DailyClaimVision.UniqueButton(lines.Where(line => area.Contains(
                new Point(line.Box.Left + line.Box.Width / 2, line.Box.Top + line.Box.Height / 2))), labels);
        }

        private static Rectangle Region(Size size, double left, double top, double right, double bottom)
        {
            double width = Math.Min(size.Width, size.Height * (16.0 / 9)), height = width * (9.0 / 16);
            return Rectangle.FromLTRB((int)Math.Round(size.Width * .5 + (left - .5) * width),
                (int)Math.Round(size.Height * .5 + (top - .5) * height),
                (int)Math.Round(size.Width * .5 + (right - .5) * width),
                (int)Math.Round(size.Height * .5 + (bottom - .5) * height));
        }
    }
}
