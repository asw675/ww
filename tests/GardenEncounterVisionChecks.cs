using System;
using System.Drawing;

namespace TideCombat
{
    internal static class GardenEncounterVisionChecks
    {
        internal static void DistinguishesDeclineFromPaidChoices()
        {
            var size = new Size(1600, 900);
            OcrLine title = Line("奇缘异遇", 907, 147, 162, 42);
            OcrLine first = Line("当前所拥有的乐园券数量减少50%", 665, 257, 370, 34);
            OcrLine second = Line("获得10次商店免费刷新次数", 665, 372, 346, 33);
            OcrLine decline = Line("你对此不感兴趣", 666, 489, 167, 29);
            OcrLine confirm = Line("确认", 1171, 550, 52, 27);
            OcrLine hide = Line("隐藏当前界面", 759, 699, 137, 27);
            GardenEncounterPopup popup;
            Require(GardenEncounterVision.TryRead(new[] { title, first, second, decline, confirm, hide }, size, out popup) &&
                popup.Title == title.Box && popup.Decline == decline.Box && popup.Confirm == confirm.Box && popup.Hide == hide.Box,
                "only the opt-out text is eligible among the encounter's options");
            Require(!GardenEncounterVision.TryRead(new[] { title, first, second, confirm, hide }, size, out popup) &&
                !popup.Title.IsEmpty && popup.Decline.IsEmpty && popup.Confirm == confirm.Box,
                "a recognized encounter remains visible if the opt-out text cannot be read");
            Require(!GardenEncounterVision.TryRead(new[] { first, second, decline, confirm, hide }, size, out popup) &&
                popup.Title.IsEmpty, "an unrelated confirmation controls no encounter");
            Require(!GardenEncounterVision.TryRead(new[] { title, first, decline,
                Line("对此不感兴趣", 670, 369, 166, 31), confirm, hide }, size, out popup) && popup.Decline.IsEmpty,
                "ambiguous opt-out text cannot authorize a choice");
        }

        internal static void RequiresPositiveConfirmationStateAndDeclineSelection()
        {
            using (var bitmap = new Bitmap(1600, 900))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var reader = new FrameReader())
            using (var disabled = new SolidBrush(Color.FromArgb(176, 176, 177)))
            using (var active = new SolidBrush(Color.FromArgb(236, 167, 79)))
            {
                graphics.Clear(Color.FromArgb(250, 250, 228));
                Rectangle button = new Rectangle(1041, 531, 312, 64);
                var popup = new GardenEncounterPopup
                {
                    Title = new Rectangle(907, 147, 162, 42),
                    Decline = new Rectangle(666, 489, 167, 29),
                    Confirm = new Rectangle(1171, 550, 52, 27)
                };
                Require(!GardenEncounterVision.IsConfirmDisabled(reader.Read(bitmap), popup) &&
                    !GardenEncounterVision.IsConfirmEnabled(reader.Read(bitmap), popup), "an unrecognized fill is not a button state");
                graphics.FillRectangle(disabled, button);
                Require(GardenEncounterVision.IsConfirmDisabled(reader.Read(bitmap), popup) &&
                    !GardenEncounterVision.IsConfirmEnabled(reader.Read(bitmap), popup), "both gray button flanks prove disabled");
                graphics.FillRectangle(active, button.Left, button.Top, button.Width / 2, button.Height);
                Require(!GardenEncounterVision.IsConfirmDisabled(reader.Read(bitmap), popup) &&
                    !GardenEncounterVision.IsConfirmEnabled(reader.Read(bitmap), popup), "mixed button states are ambiguous");
                graphics.FillRectangle(active, button);
                Require(GardenEncounterVision.IsConfirmEnabled(reader.Read(bitmap), popup) &&
                    !GardenEncounterVision.CanConfirmDecline(reader.Read(bitmap), popup),
                    "a bright confirmation may apply to a different option");
                graphics.FillRectangle(active, 642, 479, 690, 39);
                Require(GardenEncounterVision.CanConfirmDecline(reader.Read(bitmap), popup),
                    "a highlighted, explicitly recognized declination and an active confirmation allow completing it");
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
