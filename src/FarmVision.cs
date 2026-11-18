using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal sealed class FarmUi
    {
        internal bool FPrompt;
        internal Rectangle FBox;
        internal bool Absorb;
        internal bool Claim;
        internal bool Challenge;
        internal bool TeamStart;
        internal bool SkipDialog;
        internal bool Death;
        internal bool Treasure { get; set; }
        internal Rectangle TreasureBox { get; set; }
        internal bool Book;
        internal Rectangle BookBox;
        internal List<Rectangle> BossRows = new List<Rectangle>();
        internal bool InRealm;
        internal bool OpenWorld;
        internal bool Teleport;
        internal Rectangle TeleportBox;
        internal bool MonthlyCard;
        internal Rectangle MonthlyCardBox;
        internal bool ClaimReward;
        internal bool HasMapMarker;
        internal Rectangle MapMarker;
        internal Rectangle ClaimBox;
        internal double ScrollTop = .1963;
        internal int Evidence;
        internal string Detail = "等待画面";
    }

    internal static class FarmIndicators
    {
        // Signatures that the 4C farm layer matches. Keep this list in sync with the
        // rows added to data/Signatures.tsv; tools/BuildSignatures.cs regenerates them.
        internal static readonly string[] All =
        {
            "pick_up_f_hcenter_vcenter", "absorb_zh_CN", "absorb_en_US",
            "claim_cancel_button_hcenter_vcenter", "cancel_button_hcenter_vcenter",
            "confirm_btn_hcenter_vcenter", "confirm_btn_highlight_hcenter_vcenter",
            "team_start_challenge", "gray_button_challenge", "gray_start_battle",
            "skip_dialog_check", "skip_dialog_confirm", "revive_confirm_hcenter_vcenter",
            "treasure_icon", "gray_book_boss", "boss_proceed", "gray_teleport",
            "illusive_realm_exit", "world_earth_icon", "monthly_card", "claim_stamina_sign",
            "boss_check_mark_minimap", "boss_check_mark"
        };
    }

    // Whole-frame integral index so several template searches can share one pass.
    internal sealed class FrameIndex
    {
        private int width, height;
        private int[] luminance, blueRed, greenMagenta;
        private readonly int[] values = new int[49 * 3];

        internal void Load(PixelFrame frame)
        {
            if (width != frame.Width || height != frame.Height)
            {
                width = frame.Width;
                height = frame.Height;
                int length = (width + 1) * (height + 1);
                luminance = new int[length];
                blueRed = new int[length];
                greenMagenta = new int[length];
            }
            int stride = width + 1;
            for (int y = 1; y <= height; y++)
            {
                int sumLight = 0, sumBlueRed = 0, sumGreen = 0;
                for (int x = 1; x <= width; x++)
                {
                    int r, g, b;
                    frame.Rgb(x - 1, y - 1, out r, out g, out b);
                    sumLight += (r * 3 + g * 6 + b) / 10;
                    sumBlueRed += b - r;
                    sumGreen += g - (r + b) / 2;
                    int index = y * stride + x;
                    luminance[index] = sumLight + luminance[index - stride];
                    blueRed[index] = sumBlueRed + blueRed[index - stride];
                    greenMagenta[index] = sumGreen + greenMagenta[index - stride];
                }
            }
        }

        internal int[] Sample(Rectangle area)
        {
            int stride = width + 1;
            for (int gy = 0; gy < 7; gy++)
            {
                int top = area.Top + gy * area.Height / 7;
                int bottom = area.Top + (gy + 1) * area.Height / 7;
                int heightCell = Math.Max(1, bottom - top);
                int y0 = Math.Max(0, Math.Min(height - 1, top));
                heightCell = Math.Min(heightCell, height - y0);
                for (int gx = 0; gx < 7; gx++)
                {
                    int left = area.Left + gx * area.Width / 7;
                    int right = area.Left + (gx + 1) * area.Width / 7;
                    int widthCell = Math.Max(1, right - left);
                    int x0 = Math.Max(0, Math.Min(width - 1, left));
                    widthCell = Math.Min(widthCell, width - x0);
                    int a = y0 * stride + x0;
                    int b = a + widthCell;
                    int c = a + heightCell * stride;
                    int d = c + widthCell;
                    int size = widthCell * heightCell;
                    values[gy * 7 + gx] = Div(luminance[d] + luminance[a] - luminance[b] - luminance[c], size);
                    values[49 + gy * 7 + gx] = Div(blueRed[d] + blueRed[a] - blueRed[b] - blueRed[c], size);
                    values[98 + gy * 7 + gx] = Div(greenMagenta[d] + greenMagenta[a] - greenMagenta[b] - greenMagenta[c], size);
                }
            }
            return values;
        }

        private static int Div(int sum, int size)
        {
            return sum / Math.Max(1, size);
        }
    }

    internal sealed class FarmVision
    {
        private struct Candidate
        {
            internal double Score;
            internal Rectangle Rect;
            internal Candidate(double score, Rectangle rect) { Score = score; Rect = rect; }
        }

        private static readonly int[] Small = { 0, -2, 2, -4, 4, -6, 6, -8, 8, -10, 10, -12, 12 };
        private readonly FrameIndex index = new FrameIndex();

        internal FarmUi Read(PixelFrame frame, bool detailed)
        {
            var ui = new FarmUi();
            index.Load(frame);
            ui.FPrompt = FindNear(frame, "pick_up_f_hcenter_vcenter", 20, .86, .70, out ui.FBox);
            ui.Absorb = FindNear(frame, "absorb_zh_CN", 30, .86, .70, out Rectangle absorb) ||
                FindNear(frame, "absorb_en_US", 30, .86, .70, out absorb);
            ui.Claim = FindNear(frame, "claim_cancel_button_hcenter_vcenter", 8, .88, .72, out Rectangle claim) ||
                FindNear(frame, "cancel_button_hcenter_vcenter", 8, .88, .72, out claim);
            if (ui.Claim)
                ui.Evidence |= 2;
            ui.TeamStart = FindNear(frame, "team_start_challenge", 8, .88, .72, out Rectangle start);
            bool grayChallenge = FindNear(frame, "gray_button_challenge", 8, .88, .72, out start);
            bool grayStart = FindNear(frame, "gray_start_battle", 8, .88, .72, out start);
            ui.Challenge = ui.TeamStart || grayChallenge || grayStart;
            if (ui.Challenge)
                ui.Evidence |= 4;
            bool check = FindNear(frame, "skip_dialog_check", 8, .86, .70, out Rectangle box);
            bool confirm = FindNear(frame, "skip_dialog_confirm", 8, .86, .70, out Rectangle confirmBox) ||
                FindNear(frame, "confirm_btn_hcenter_vcenter", 8, .86, .70, out confirmBox);
            ui.SkipDialog = check && confirm;
            if (!detailed)
            {
                ui.Detail = ui.FPrompt ? "可交互" : "探索中";
                return ui;
            }
            ui.Death = FindNear(frame, "revive_confirm_hcenter_vcenter", 8, .92, .78, out Rectangle death);
            if (ui.Death)
                ui.Evidence |= 1;
            ui.InRealm = FindNear(frame, "illusive_realm_exit", 8, .86, .72, out Rectangle realm);
            ui.OpenWorld = !ui.InRealm && FindNear(frame, "world_earth_icon", 8, .86, .72, out Rectangle world);
            ui.Book = FindNear(frame, "gray_book_boss", 8, .86, .72, out ui.BookBox);
            if (ui.Book)
                ui.Evidence |= 16;
            ui.Teleport = FindNear(frame, "gray_teleport", 8, .86, .72, out ui.TeleportBox);
            if (ui.Teleport)
                ui.Evidence |= 32;
            List<Rectangle> markers = FindAll(frame, "boss_check_mark", Ratio(frame, .3, .3, .7, .7), 4, .82, .74);
            ui.HasMapMarker = markers.Count > 0;
            if (ui.HasMapMarker)
                ui.MapMarker = markers[0];
            ui.MonthlyCard = FindNear(frame, "monthly_card", 12, .86, .74, out ui.MonthlyCardBox);
            ui.ClaimReward = FindNear(frame, "claim_stamina_sign", 10, .86, .74, out ui.ClaimBox);
            if (ui.Book)
            {
                ui.BossRows = FindAll(frame, "boss_proceed", Ratio(frame, .86, .18, 1, .90), 4, .90, .76);
                ui.ScrollTop = ScrollBarTop(frame);
            }
            ui.Detail = ui.Death ? "死亡" : ui.Claim ? "领奖弹窗" : ui.Challenge ? "挑战界面" :
                ui.Book ? "图鉴" : ui.Teleport ? "传送按钮" : ui.FPrompt ? "可交互" :
                ui.InRealm ? "副本内" : ui.OpenWorld ? "大世界" : "探索中";
            return ui;
        }

        internal bool FindLabel(PixelFrame frame, string label, double threshold, out Rectangle found)
        {
            index.Load(frame);
            return FindNear(frame, label, 20, threshold, .70, out found);
        }

        internal bool FindFirst(PixelFrame frame, string label, Rectangle area, int step, double threshold,
            double lightMinimum, out Rectangle found)
        {
            List<Rectangle> all = FindAll(frame, label, area, step, threshold, lightMinimum);
            found = all.Count > 0 ? all[0] : Rectangle.Empty;
            return all.Count > 0;
        }

        internal List<Rectangle> FindAll(PixelFrame frame, string label, Rectangle area, int step, double threshold,
            double lightMinimum)
        {
            index.Load(frame);
            var results = new List<Rectangle>();
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                int width = pattern.Area.Width, height = pattern.Area.Height;
                Rectangle limit = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
                if (limit.Width <= width || limit.Height <= height)
                    continue;
                var candidates = new List<Candidate>(8);
                for (int y = limit.Top; y <= limit.Bottom - height; y += step)
                    for (int x = limit.Left; x <= limit.Right - width; x += step)
                    {
                        var trial = new Rectangle(x, y, width, height);
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score >= threshold)
                            Add(candidates, score, trial);
                    }
                candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
                foreach (Candidate candidate in candidates)
                {
                    if (!PassesLight(frame, pattern, candidate.Rect, lightMinimum))
                        continue;
                    if (Overlaps(results, candidate.Rect))
                        continue;
                    results.Add(candidate.Rect);
                }
            }
            results.Sort((left, right) => left.Y.CompareTo(right.Y));
            return results;
        }

        private bool FindNear(PixelFrame frame, string label, int radius, double threshold, double lightMinimum,
            out Rectangle found)
        {
            found = Rectangle.Empty;
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle expected = pattern.Map(frame.Width, 0);
                var candidates = new List<Candidate>(8);
                foreach (int dy in Small)
                {
                    if (Math.Abs(dy) > radius) continue;
                    foreach (int dx in Small)
                    {
                        if (Math.Abs(dx) > radius) continue;
                        var trial = new Rectangle(expected.X + dx, expected.Y + dy, expected.Width, expected.Height);
                        if (!Inside(frame, trial)) continue;
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score >= threshold)
                            Add(candidates, score, trial);
                    }
                }
                candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
                foreach (Candidate candidate in candidates)
                {
                    if (!PassesLight(frame, pattern, candidate.Rect, lightMinimum))
                        continue;
                    found = candidate.Rect;
                    return true;
                }
            }
            return false;
        }

        private static bool PassesLight(PixelFrame frame, VisualPattern pattern, Rectangle rect, double minimum)
        {
            return !pattern.HasLight || pattern.LightSimilarity(frame, rect) >= minimum;
        }

        private static bool Overlaps(List<Rectangle> existing, Rectangle rect)
        {
            foreach (Rectangle item in existing)
                if (Math.Abs(item.X - rect.X) < rect.Width && Math.Abs(item.Y - rect.Y) < rect.Height)
                    return true;
            return false;
        }

        private static void Add(List<Candidate> candidates, double score, Rectangle rect)
        {
            if (candidates.Count < 24)
            {
                candidates.Add(new Candidate(score, rect));
                return;
            }
            int worst = 0;
            for (int index = 1; index < candidates.Count; index++)
                if (candidates[index].Score < candidates[worst].Score)
                    worst = index;
            if (score > candidates[worst].Score)
                candidates[worst] = new Candidate(score, rect);
        }

        private double ScrollBarTop(PixelFrame frame)
        {
            Rectangle area = Ratio(frame, .969, .191, .978, .271);
            for (int y = area.Top; y <= area.Bottom; y++)
            {
                int white = 0;
                for (int x = area.Left; x <= area.Right; x++)
                    if (frame.Pale(x, y, 190, 25))
                        white++;
                if (white >= 3)
                    return (double)y / frame.Height;
            }
            return .1963;
        }

        private static Rectangle Ratio(PixelFrame frame, double left, double top, double right, double bottom)
        {
            return new Rectangle((int)(frame.Width * left), (int)(frame.Height * top),
                Math.Max(1, (int)(frame.Width * (right - left))), Math.Max(1, (int)(frame.Height * (bottom - top))));
        }

        private static bool Inside(PixelFrame frame, Rectangle rect)
        {
            return rect.Left >= 0 && rect.Top >= 0 && rect.Right <= frame.Width && rect.Bottom <= frame.Height;
        }
    }
}
