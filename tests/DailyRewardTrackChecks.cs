using System;
using System.Drawing;

namespace TideCombat
{
    internal static class DailyRewardTrackChecks
    {
        internal static void DistinguishesClaimedFromPendingAndLocked()
        {
            using (var image = new Bitmap(1600, 900))
            using (Graphics paint = Graphics.FromImage(image))
            using (var reader = new FrameReader())
            using (var gold = new SolidBrush(Color.FromArgb(239, 205, 115)))
            using (var red = new SolidBrush(Color.FromArgb(217, 58, 84)))
            {
                paint.Clear(Color.FromArgb(43, 57, 72));
                var score = new[] { Line("60", 312, 754, 49, 37) };
                var thresholds = new OcrLine[DailyRewardTrack.ChestCount];
                for (int slot = 0; slot < thresholds.Length; slot++)
                    thresholds[slot] = Line(((slot + 1) * 20).ToString(), 565 + 213 * slot, 807, 38, 24);

                DrawCheck(paint, 628, Brushes.White);
                paint.FillRectangle(gold, 821, 786, 40, 30);
                paint.FillRectangle(red, 866, 754, 14, 15);
                paint.FillRectangle(Brushes.White, 1034, 789, 40, 25);
                paint.FillRectangle(Brushes.White, 1247, 789, 40, 25);
                DrawCheck(paint, 1480, Brushes.DimGray);

                DailyRewardTrack track;
                Require(DailyRewardTrack.TryRead(reader.Read(image), score, thresholds, out track) &&
                    track.Eligible == 7 && track.Observed == 11 && track.Pending == 2 && track.FirstPending == 1,
                    "Only the claimed check, glowing pending chest and verified ineligible chest should be observed.");
                Require(track.IsCleared(0) && !track.IsCleared(1) && !track.IsCleared(2) &&
                    !track.IsCleared(3) && !track.AllEligibleObserved,
                    "A white chest silhouette or dim check cannot clear an eligible tier.");

                paint.Clear(Color.FromArgb(43, 57, 72));
                for (int slot = 0; slot < 3; slot++) DrawCheck(paint, 628 + 213 * slot, Brushes.White);
                Require(DailyRewardTrack.TryRead(reader.Read(image), score, thresholds, out track) &&
                    track.AllEligibleObserved && track.Pending == 0 && track.FirstPending == -1 &&
                    track.IsCleared(0) && track.IsCleared(1) && track.IsCleared(2),
                    "The full eligible track is observed only after all three claimed checks are present.");
            }
        }

        private static void DrawCheck(Graphics paint, int center, Brush color)
        {
            for (int row = 0; row < 32; row++)
            {
                int x = center + 22 - (int)Math.Round(24 * row / 31.0);
                paint.FillRectangle(color, x - 1, 782 + row, 3, 1);
            }
        }

        private static OcrLine Line(string text, int x, int y, int width, int height)
        {
            return new OcrLine { Text = text, Score = 1, Box = new Rectangle(x, y, width, height) };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
