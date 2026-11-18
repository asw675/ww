using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal sealed class FarmUi
    {
        internal bool FPrompt;
        internal Rectangle FBox;
        internal bool Claim;
        internal bool Challenge;
        internal bool TeamStart;
        internal bool SkipDialog;
        internal bool Death;
        internal bool Treasure;
        internal Rectangle TreasureBox;
        internal bool Book;
        internal Rectangle BookBox;
        internal List<Rectangle> BossRows = new List<Rectangle>();
        internal bool InRealm;
        internal bool OpenWorld;
        internal bool Teleport;
        internal Rectangle TeleportBox;
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
            "illusive_realm_exit", "world_earth_icon"
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
        private static readonly int[] Small = { 0, -3, 3, -6, 6, -9, 9, -12, 12 };
        private readonly FrameIndex index = new FrameIndex();

        internal FarmUi Read(PixelFrame frame, bool detailed)
        {
            var ui = new FarmUi();
            index.Load(frame);
            ui.FPrompt = FindNear(frame, "pick_up_f_hcenter_vcenter", 26, 2, .80, out ui.FBox);
            ui.InRealm = FindNear(frame, "illusive_realm_exit", 8, 2, .70, out Rectangle realm);
            ui.OpenWorld = !ui.InRealm && FindNear(frame, "world_earth_icon", 8, 2, .70, out Rectangle world);
            ui.Death = FindNear(frame, "revive_confirm_hcenter_vcenter", 60, 4, .84, out Rectangle death);
            if (ui.Death)
                ui.Evidence |= 1;
            ui.Claim = FindIn(frame, "claim_cancel_button_hcenter_vcenter",
                Ratio(frame, .12, .38, .88, .78), 6, .80, out Rectangle claim) ||
                FindIn(frame, "cancel_button_hcenter_vcenter", Ratio(frame, .12, .38, .88, .78), 6, .80, out claim);
            if (ui.Claim)
                ui.Evidence |= 2;
            ui.TeamStart = FindIn(frame, "team_start_challenge", Ratio(frame, .78, .74, 1, .97), 6, .80, out Rectangle start);
            ui.Challenge = ui.TeamStart ||
                FindIn(frame, "gray_button_challenge", Ratio(frame, .5, .70, .85, .92), 6, .78, out start) ||
                FindIn(frame, "gray_start_battle", Ratio(frame, .78, .74, 1, .97), 6, .78, out start);
            if (ui.Challenge)
                ui.Evidence |= 4;
            bool check = FindIn(frame, "skip_dialog_check", Ratio(frame, .28, .40, .72, .68), 5, .76, out Rectangle box);
            bool confirm = FindIn(frame, "skip_dialog_confirm", Ratio(frame, .40, .45, .75, .72), 5, .76, out Rectangle confirmBox) ||
                FindIn(frame, "confirm_btn_hcenter_vcenter", Ratio(frame, .40, .42, .80, .74), 5, .76, out confirmBox);
            ui.SkipDialog = check && confirm;
            if (!detailed && !ui.SkipDialog)
            {
                ui.Detail = ui.FPrompt ? "可交互" : "等待";
                return ui;
            }
            ui.Treasure = FindIn(frame, "treasure_icon", Ratio(frame, .02, .10, .98, .82), 8, .80, out ui.TreasureBox);
            if (ui.Treasure)
                ui.Evidence |= 8;
            ui.Book = FindIn(frame, "gray_book_boss", Ratio(frame, 0, .08, .16, .92), 6, .30, out ui.BookBox);
            if (ui.Book)
            {
                ui.Evidence |= 16;
                ui.BossRows = FindAll(frame, "boss_proceed", Ratio(frame, .86, .18, 1, .90), 4, .76);
            }
            ui.Teleport = FindIn(frame, "gray_teleport", Ratio(frame, .55, .72, 1, 1), 6, .72, out ui.TeleportBox);
            if (ui.Teleport)
                ui.Evidence |= 32;
            ui.Detail = ui.Death ? "死亡" : ui.Claim ? "领奖弹窗" : ui.Challenge ? "挑战界面" :
                ui.Treasure ? "宝箱" : ui.Book ? "图鉴" : ui.Teleport ? "传送按钮" : ui.FPrompt ? "可交互" : "探索中";
            return ui;
        }

        internal bool FindLabel(PixelFrame frame, string label, Rectangle area, int step, double threshold,
            out Rectangle found)
        {
            index.Load(frame);
            return FindIn(frame, label, area, step, threshold, out found);
        }

        internal List<Rectangle> FindAll(PixelFrame frame, string label, Rectangle area, int step, double threshold)
        {
            index.Load(frame);
            var results = new List<Rectangle>();
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                for (int y = area.Top; y <= area.Bottom - pattern.Area.Height; y += step)
                {
                    for (int x = area.Left; x <= area.Right - pattern.Area.Width; x += step)
                    {
                        var trial = new Rectangle(x, y, pattern.Area.Width, pattern.Area.Height);
                        if (pattern.Signature.Similarity(index.Sample(trial)) < threshold)
                            continue;
                        var hit = new Rectangle(trial.X + trial.Width / 2, trial.Y + trial.Height / 2, trial.Width, trial.Height);
                        bool overlaps = false;
                        foreach (Rectangle existing in results)
                            if (Math.Abs(existing.X - hit.X) < hit.Width && Math.Abs(existing.Y - hit.Y) < hit.Height)
                                overlaps = true;
                        if (!overlaps)
                            results.Add(hit);
                    }
                }
            }
            results.Sort((left, right) => left.Y.CompareTo(right.Y));
            return results;
        }

        private bool FindNear(PixelFrame frame, string label, int radius, int step, double threshold, out Rectangle found)
        {
            found = Rectangle.Empty;
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle expected = pattern.Map(frame.Width, 0);
                double best = 0;
                foreach (int dy in Small)
                {
                    if (Math.Abs(dy) > radius) continue;
                    foreach (int dx in Small)
                    {
                        if (Math.Abs(dx) > radius) continue;
                        var trial = new Rectangle(expected.X + dx, expected.Y + dy, expected.Width, expected.Height);
                        if (!Inside(frame, trial)) continue;
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score <= best) continue;
                        best = score;
                        found = trial;
                        if (best >= .99) return true;
                    }
                }
                if (best >= threshold)
                    return true;
            }
            found = Rectangle.Empty;
            return false;
        }

        private bool FindIn(PixelFrame frame, string label, Rectangle area, int step, double threshold, out Rectangle found)
        {
            found = Rectangle.Empty;
            double best = 0;
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                int width = pattern.Area.Width, height = pattern.Area.Height;
                Rectangle limit = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
                if (limit.Width <= width || limit.Height <= height)
                    continue;
                for (int y = limit.Top; y <= limit.Bottom - height; y += step)
                {
                    for (int x = limit.Left; x <= limit.Right - width; x += step)
                    {
                        var trial = new Rectangle(x, y, width, height);
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score <= best) continue;
                        best = score;
                        found = trial;
                        if (best >= .995)
                            return true;
                    }
                }
            }
            if (best >= threshold)
                return true;
            found = Rectangle.Empty;
            return false;
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
