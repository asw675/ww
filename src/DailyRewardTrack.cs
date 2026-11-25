using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal struct DailyRewardTrack
    {
        internal const int ChestCount = 5;
        internal int Points, Eligible, Observed, Pending;

        internal bool AllEligibleObserved { get { return (Observed & Eligible) == Eligible; } }

        internal int FirstPending
        {
            get
            {
                for (int slot = 0; slot < ChestCount; slot++)
                    if ((Pending & 1 << slot) != 0) return slot;
                return -1;
            }
        }

        internal bool IsCleared(int slot)
        {
            int bit = 1 << slot;
            return ((Observed & Eligible) & bit) != 0 && (Pending & bit) == 0;
        }

        internal static Rectangle Control(PixelFrame frame, int slot)
        {
            int center = 628 + 213 * slot;
            return Scale(frame, center - 24, 777, center + 24, 823);
        }

        internal static bool TryRead(PixelFrame frame, IEnumerable<OcrLine> score, IEnumerable<OcrLine> milestones,
            out DailyRewardTrack track)
        {
            track = new DailyRewardTrack();
            if (frame.Width < 800 || Math.Abs(frame.Width / (double)frame.Height - 16.0 / 9) > .015)
                return false;
            Rectangle counter = Scale(frame, 288, 745, 480, 801);
            var counterLines = new List<OcrLine>();
            foreach (OcrLine line in score)
                if (line.Box.Height >= frame.Height / 36 && counter.Contains(new Point(
                    line.Box.Left + line.Box.Width / 2, line.Box.Top + line.Box.Height / 2)))
                    counterLines.Add(line);
            int points;
            bool uncertain;
            if (!DailyPlan.TryReadPoints(counterLines, out points, out uncertain) || uncertain) return false;
            track.Points = points;
            track.Eligible = (1 << Math.Min(ChestCount, points / 20)) - 1;
            foreach (OcrLine line in milestones)
            {
                int threshold;
                if (line.Box.IsEmpty || line.Score < .70f || !int.TryParse(OcrText.Compact(line.Text), out threshold) ||
                    threshold < 20 || threshold > 100 || threshold % 20 != 0)
                    continue;
                int slot = threshold / 20 - 1;
                int center = 628 + 213 * slot;
                Rectangle label = Scale(frame, center - 79, 803, center - 20, 843);
                if (!label.Contains(new Point(line.Box.Left + line.Box.Width / 2, line.Box.Top + line.Box.Height / 2)))
                    continue;

                Rectangle icon = Scale(frame, center - 20, 786, center + 20, 816);
                Rectangle dot = Scale(frame, center + 20, 748, center + 40, 780);
                int gold = Count(frame, icon, IsGold);
                int red = Count(frame, dot, IsRed);
                bool glow = gold >= icon.Width * icon.Height / 4;
                int bit = 1 << slot;
                bool eligible = (track.Eligible & bit) != 0;
                if (red >= dot.Width * dot.Height / 9)
                {
                    if (!glow || !eligible) continue;
                    track.Pending |= bit;
                }
                else if (glow || red > dot.Width * dot.Height / 40)
                    continue;
                else if (eligible ? !HasClaimedCheck(frame, center) :
                    Count(frame, icon, IsWhite) < icon.Width * icon.Height / 3)
                    continue;
                track.Observed |= bit;
            }
            return track.Observed != 0;
        }

        private static bool HasClaimedCheck(PixelFrame frame, int center)
        {
            // The overlay check extends above the white chest silhouette.
            Rectangle stroke = Scale(frame, center - 2, 782, center + 22, 814);
            int tolerance = Math.Max(1, (int)Math.Round(2 * frame.Width / 1600.0));
            int upperRows = stroke.Height / 4;
            int whiteRows = 0, upperWhiteRows = 0;
            for (int row = 0; row < stroke.Height; row++)
            {
                int x = stroke.Right - (int)Math.Round(stroke.Width * row / (double)(stroke.Height - 1));
                for (int offset = -tolerance; offset <= tolerance; offset++)
                {
                    int red, green, blue;
                    frame.Rgb(x + offset, stroke.Top + row, out red, out green, out blue);
                    if (!IsWhite(red, green, blue)) continue;
                    whiteRows++;
                    if (row < upperRows) upperWhiteRows++;
                    break;
                }
            }
            return whiteRows * 4 >= stroke.Height * 3 && upperWhiteRows * 4 >= upperRows * 3;
        }

        private static Rectangle Scale(PixelFrame frame, int left, int top, int right, int bottom)
        {
            return Rectangle.FromLTRB((int)Math.Round(frame.Width * left / 1600.0),
                (int)Math.Round(frame.Height * top / 900.0), (int)Math.Round(frame.Width * right / 1600.0),
                (int)Math.Round(frame.Height * bottom / 900.0));
        }

        private static int Count(PixelFrame frame, Rectangle area, Func<int, int, int, bool> predicate)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int red, green, blue;
                    frame.Rgb(x, y, out red, out green, out blue);
                    if (predicate(red, green, blue)) count++;
                }
            return count;
        }

        private static bool IsGold(int red, int green, int blue)
        {
            return red >= 174 && green >= 139 && green - blue >= 35 && red >= green - 5;
        }

        private static bool IsRed(int red, int green, int blue)
        {
            return red >= 140 && red - green >= 50 && red - blue >= 40 && green <= 125;
        }

        private static bool IsWhite(int red, int green, int blue)
        {
            int minimum = Math.Min(red, Math.Min(green, blue));
            return minimum >= 180 && Math.Max(red, Math.Max(green, blue)) - minimum <= 35;
        }
    }
}
