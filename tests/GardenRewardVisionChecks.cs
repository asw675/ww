using System;
using System.Drawing;

namespace TideCombat
{
    internal static class GardenRewardVisionChecks
    {
        internal static void RequiresRecognizedRewardPopupControls()
        {
            var size = new Size(1920, 1080);
            OcrLine skip = Line("跳过", 1110, 850, 55, 29);
            OcrLine confirm = Line("确认", 1455, 851, 55, 29);
            OcrLine hide = Line("隐藏当前界面", 859, 977, 168, 28);
            OcrLine nextDay = Line("下一日", 1582, 955, 69, 32);
            GardenRewardPopup popup;
            Require(GardenRewardVision.TryRead(new[] { skip, confirm, hide, nextDay }, size, out popup) &&
                popup.Skip == skip.Box && popup.Confirm == confirm.Box && popup.Hide == hide.Box,
                "the reward overlay must use its own controls even when the next-day control is still displayed");
            Require(!GardenRewardVision.TryRead(new[] { skip, confirm, nextDay }, size, out popup) &&
                popup.Skip == skip.Box && popup.Confirm == confirm.Box && popup.Hide.IsEmpty,
                "a general confirmation plus skip cannot identify the reward overlay, but the identified controls are retained");
            Require(!GardenRewardVision.TryRead(new[] { skip, confirm, hide,
                Line("确认", 1513, 897, 55, 22) }, size, out popup) &&
                popup.Skip == skip.Box && popup.Confirm.IsEmpty && popup.Hide == hide.Box,
                "separate controls of the same role make the reward overlay ambiguous");
            Require(!GardenRewardVision.TryRead(new[] { skip, confirm, Line("隐藏当前界面", 859, 701, 168, 28) },
                size, out popup), "a hide instruction outside the footer cannot identify the overlay");
            Require(GardenRewardVision.TryRead(new[] { Line("跳過", 1110, 850, 55, 29),
                Line("確認", 1455, 851, 55, 29), Line("隱藏當前介面", 859, 977, 168, 28) }, size, out popup),
                "the traditional Chinese controls use the same geometry");
        }

        internal static void RequiresEnabledConfirmationAndFullSelectedCard()
        {
            using (var bitmap = new Bitmap(1600, 900))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var reader = new FrameReader())
            using (var hover = new SolidBrush(Color.FromArgb(246, 153, 66)))
            using (var disabled = new SolidBrush(Color.FromArgb(117, 141, 84)))
            using (var enabled = new SolidBrush(Color.FromArgb(170, 205, 122)))
            {
                graphics.Clear(Color.FromArgb(33, 44, 30));
                graphics.FillRectangle(hover, 240, 110, 37, 6);
                graphics.FillRectangle(hover, 240, 110, 7, 34);
                graphics.FillRectangle(hover, 592, 110, 37, 6);
                graphics.FillRectangle(hover, 622, 110, 7, 34);
                var popup = new GardenRewardPopup
                {
                    Skip = new Rectangle(909, 701, 63, 37),
                    Confirm = new Rectangle(1202, 701, 60, 37),
                    Hide = new Rectangle(759, 812, 135, 26)
                };
                graphics.FillRectangle(enabled, 1080, 690, 308, 61);
                Require(!GardenRewardVision.HasSelectedCard(reader.Read(bitmap), popup),
                    "orange mouse focus cannot authorize confirmation even if the button region is bright");

                graphics.FillRectangle(enabled, 258, 630, 347, 8);
                graphics.FillRectangle(enabled, 258, 131, 7, 504);
                Require(!GardenRewardVision.HasSelectedCard(reader.Read(bitmap), popup),
                    "a bright button and one green rail do not establish a complete selected card");
                graphics.FillRectangle(enabled, 598, 131, 7, 504);
                graphics.FillRectangle(disabled, 1080, 690, 308, 61);
                PixelFrame disabledFrame = reader.Read(bitmap);
                Require(GardenRewardVision.IsConfirmDisabled(disabledFrame, popup) &&
                    !GardenRewardVision.HasSelectedCard(disabledFrame, popup),
                    "a disabled confirmation control cannot authorize any click");

                graphics.FillRectangle(enabled, 1080, 690, 308, 61);
                Rectangle card;
                Require(GardenRewardVision.TryFindSelectedCard(reader.Read(bitmap), popup, out card) &&
                    card.Contains(new Point(435, 400)) && !card.Contains(new Point(800, 400)),
                    "a full green selection outline together with the enabled confirmation identifies the selected card");
                graphics.FillRectangle(enabled, 644, 630, 347, 8);
                graphics.FillRectangle(enabled, 644, 131, 7, 504);
                graphics.FillRectangle(enabled, 984, 131, 7, 504);
                Require(!GardenRewardVision.TryFindSelectedCard(reader.Read(bitmap), popup, out card) && card.IsEmpty,
                    "two disjoint apparent selections are ambiguous");
            }
        }

        internal static void RarityUsesColorAndEqualCardsPreferCenter()
        {
            using (var bitmap = new Bitmap(1600, 900))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var reader = new FrameReader())
            using (var gray = new SolidBrush(Color.FromArgb(217, 216, 232)))
            using (var blue = new SolidBrush(Color.FromArgb(196, 240, 255)))
            using (var gold = new SolidBrush(Color.FromArgb(253, 211, 120)))
            using (var purple = new SolidBrush(Color.FromArgb(224, 183, 247)))
            {
                var left = new GardenRewardCard { Title = "left", Header = new Rectangle(270, 145, 300, 64) };
                var middle = new GardenRewardCard { Title = "middle", Header = new Rectangle(645, 145, 300, 64) };
                var right = new GardenRewardCard { Title = "right", Header = new Rectangle(1020, 145, 300, 64) };
                left.Bounds = left.Header;
                middle.Bounds = middle.Header;
                right.Bounds = right.Header;
                var cards = new[] { left, middle, right };
                graphics.FillRectangle(blue, left.Header);
                graphics.FillRectangle(gray, middle.Header);
                graphics.FillRectangle(gray, right.Header);
                var ordered = GardenRunner.OrderRewardCards(reader.Read(bitmap), cards);
                Require(ordered[0].Title == "middle", "neutral and blue titles cannot be mistaken for rare offers");
                graphics.FillRectangle(purple, left.Header);
                graphics.FillRectangle(gold, right.Header);
                ordered = GardenRunner.OrderRewardCards(reader.Read(bitmap), cards);
                Require(ordered[0].Title == "right" && ordered[1].Title == "left" && ordered[2].Title == "middle",
                    "verified gold takes priority over purple, then standard offers");
            }
        }

        private static OcrLine Line(string text, int x, int y, int width, int height)
        {
            return new OcrLine { Text = text, Score = .99f, Box = new Rectangle(x, y, width, height) };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
