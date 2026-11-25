using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TideCombat
{
    internal struct GardenEncounterPopup
    {
        internal Rectangle Title, Decline, Confirm, Hide;
    }

    internal static class GardenEncounterVision
    {
        private static readonly string[] Titles = { "奇缘异遇", "奇緣異遇", "Chance Encounter", "Unexpected Encounter" };
        private static readonly string[] Declines =
        {
            "你对此不感兴趣", "你對此不感興趣", "你对此不感兴趣。", "你對此不感興趣。",
            "对此不感兴趣", "對此不感興趣", "You are not interested", "Not interested"
        };

        internal static bool TryRead(PixelFrame frame, OcrReader ocr, out GardenEncounterPopup popup)
        {
            popup = default(GardenEncounterPopup);
            if (frame.Width <= 0 || frame.Height <= 0 || ocr == null) return false;
            var size = new Size(frame.Width, frame.Height);
            List<OcrLine> heading = ocr.ReadRelative(frame, .51, .135, .75, .235, centered: true);
            if (FindButton(heading, Region(size, .52, .14, .74, .23), Titles).IsEmpty) return false;
            var lines = ocr.ReadRelative(frame, .39, .24, .865, .83, centered: true);
            return TryRead(heading.Concat(lines), size, out popup);
        }

        internal static bool TryRead(IEnumerable<OcrLine> lines, Size size, out GardenEncounterPopup popup)
        {
            popup = default(GardenEncounterPopup);
            if (size.Width <= 0 || size.Height <= 0) return false;
            Rectangle title = FindButton(lines, Region(size, .52, .14, .74, .23), Titles);
            if (title.IsEmpty) return false;
            popup.Title = title;
            popup.Decline = FindButton(lines, Region(size, .405, .265, .69, .59), Declines);
            popup.Confirm = FindButton(lines, Region(size, .68, .564, .82, .69), "确认", "確認", "Confirm");
            popup.Hide = FindButton(lines, Region(size, .425, .745, .59, .82),
                "隐藏当前界面", "隱藏當前界面", "隱藏當前介面", "Hide Current Screen", "Hide Current Interface");
            return !popup.Decline.IsEmpty && !popup.Confirm.IsEmpty;
        }

        internal static bool IsConfirmEnabled(PixelFrame frame, GardenEncounterPopup popup)
        {
            return HasConfirmFill(frame, popup, IsActiveColor);
        }

        internal static bool IsConfirmDisabled(PixelFrame frame, GardenEncounterPopup popup)
        {
            return HasConfirmFill(frame, popup, IsNeutralGray);
        }

        internal static bool CanConfirmDecline(PixelFrame frame, GardenEncounterPopup popup)
        {
            if (popup.Decline.IsEmpty || !IsConfirmEnabled(frame, popup)) return false;
            Rectangle layout = Region(new Size(frame.Width, frame.Height), 0, 0, 1, 1);
            int centerY = popup.Decline.Top + popup.Decline.Height / 2;
            int left = Math.Max(popup.Decline.Right + layout.Width / 35, layout.Left + layout.Width / 2);
            int right = Math.Min(left + layout.Width / 8, layout.Left + layout.Width * 78 / 100);
            int halfHeight = Math.Max(3, layout.Height / 120);
            Rectangle trailingFill = Rectangle.FromLTRB(left, centerY - halfHeight, right, centerY + halfHeight);
            return HasFill(frame, trailingFill, IsActiveColor);
        }

        private static bool HasConfirmFill(PixelFrame frame, GardenEncounterPopup popup, Func<int, int, int, bool> matches)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || popup.Title.IsEmpty || popup.Confirm.IsEmpty) return false;
            Rectangle layout = Region(new Size(frame.Width, frame.Height), 0, 0, 1, 1);
            int centerX = popup.Confirm.Left + popup.Confirm.Width / 2, centerY = popup.Confirm.Top + popup.Confirm.Height / 2;
            int outer = layout.Width * 8 / 100, inner = layout.Width * 4 / 100;
            int halfHeight = layout.Height / 45;
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

        private static bool IsActiveColor(int red, int green, int blue)
        {
            return green >= 165 && green - red >= 17 && green - blue >= 35 ||
                red >= 190 && green >= 120 && red - green >= 14 && green - blue >= 29;
        }

        private static bool IsNeutralGray(int red, int green, int blue)
        {
            int maximum = Math.Max(red, Math.Max(green, blue)), minimum = Math.Min(red, Math.Min(green, blue));
            return minimum >= 120 && maximum <= 210 && maximum - minimum <= 14;
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
