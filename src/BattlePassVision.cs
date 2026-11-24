using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal enum BattlePassPage { Unknown, Tasks, Rewards }

    internal sealed class BattlePassReadout
    {
        internal BattlePassPage Page;
        internal Rectangle ClaimAll, Receipt, ReceiptDismiss;
        internal bool NoPendingRewards;
    }

    internal static class BattlePassVision
    {
        private static readonly string[] EntryLabels = { "先约电台", "先約電台", "PioneerPodcast" };
        private static readonly string[] TaskGroups =
        {
            "晨间放送", "晨間放送", "每日放送", "MorningBroadcasts",
            "本周热播", "本週熱播", "每周热播", "每週熱播", "WeeklyHighlights",
            "当季专题", "當季專題", "当季放送", "當季放送", "SeasonalSpecials"
        };
        private static readonly string[] PublicTier = { "大众频道", "大眾頻道", "PublicChannel" };
        private static readonly string[] InsiderTier = { "寰宇频道", "寰宇頻道", "InsiderChannel" };
        private static readonly string[] ClaimLabels =
        {
            "一键领取", "一鍵領取", "全部领取", "全部領取", "一键领取奖励", "一鍵領取獎勵",
            "一键领奖", "一鍵領獎", "ClaimAll", "ClaimAllRewards"
        };
        private static readonly string[] FooterClaimLabels = { "领取奖励", "領取獎勵", "ClaimRewards" };
        private static readonly string[] EmptyLabels =
        {
            "暂无可领取奖励", "暫無可領取獎勵", "暂无可领取", "暫無可領取",
            "没有可领取的奖励", "沒有可領取的獎勵", "无可领取奖励", "無可領取獎勵",
            "全部已领取", "全部已領取", "已全部领取", "已全部領取", "奖励已全部领取", "獎勵已全部領取",
            "暂无可领取的任务奖励", "暫無可領取的任務獎勵", "NoRewardsToClaim", "AllRewardsClaimed"
        };
        private static readonly string[] ReceiptLabels =
        {
            "获得奖励", "獲得獎勵", "奖励领取成功", "獎勵領取成功", "领取成功", "領取成功",
            "获得物品", "獲得物品", "奖励已领取", "獎勵已領取",
            "RewardsObtained", "RewardsClaimed", "ItemsObtained"
        };
        private static readonly string[] DismissLabels =
        {
            "点击任意位置关闭", "點擊任意位置關閉", "点击空白处关闭", "點擊空白處關閉",
            "点击屏幕关闭", "點擊螢幕關閉", "ClickAnywhereToClose"
        };
        private static readonly Regex LevelProgress = new Regex(@"(?:^|[^0-9])\d{1,5}/(?:1000|10000|12000)(?:$|[^0-9])");

        internal static Rectangle FindTerminalEntry(IEnumerable<OcrLine> lines, Size size)
        {
            return Unique(lines, EntryLabels, Relative(size, .30, .13, .95, .80));
        }

        internal static BattlePassReadout Inspect(IEnumerable<OcrLine> lines, Size size)
        {
            var state = new BattlePassReadout();
            Rectangle top = Relative(size, .00, .00, .60, .24);
            Rectangle main = Relative(size, .07, .20, .95, .87);
            bool title = !Unique(lines, EntryLabels, top).IsEmpty;
            bool progress = HasLevelProgress(lines, top);
            bool taskGroups = CountMatches(lines, TaskGroups, Relative(size, .09, .18, .49, .83)) >= 2;
            bool rewardTiers = !Unique(lines, PublicTier, main).IsEmpty && !Unique(lines, InsiderTier, main).IsEmpty;
            if ((title || progress) && taskGroups != rewardTiers)
                state.Page = taskGroups ? BattlePassPage.Tasks : BattlePassPage.Rewards;

            state.Receipt = Unique(lines, ReceiptLabels, Relative(size, .24, .10, .76, .58));
            if (!state.Receipt.IsEmpty)
                state.ReceiptDismiss = Unique(lines, DismissLabels, Relative(size, .20, .53, .80, .98));
            if (state.Page == BattlePassPage.Unknown)
                return state;

            state.ClaimAll = Unique(lines, ClaimLabels, Relative(size, .39, .75, .94, .98));
            if (state.ClaimAll.IsEmpty)
                state.ClaimAll = Unique(lines, FooterClaimLabels, Relative(size, .53, .85, .81, .97));
            state.NoPendingRewards = !Unique(lines, EmptyLabels, Relative(size, .12, .27, .89, .98)).IsEmpty;
            return state;
        }

        internal static int[] ButtonPatch(PixelFrame frame, Rectangle button)
        {
            if (button.IsEmpty || !new Rectangle(0, 0, frame.Width, frame.Height).Contains(button))
                return new int[0];
            var samples = new int[45];
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 9; x++)
                    samples[y * 9 + x] = frame.Light(button.Left + ((2 * x + 1) * button.Width) / 18,
                        button.Top + ((2 * y + 1) * button.Height) / 10);
            return samples;
        }

        internal static bool ButtonChanged(PixelFrame frame, Rectangle button, int[] before)
        {
            int[] after = ButtonPatch(frame, button);
            if (after.Length == 0 || after.Length != before.Length)
                return false;
            int changed = 0;
            for (int index = 0; index < after.Length; index++)
                if (Math.Abs(after[index] - before[index]) >= 24)
                    changed++;
            return changed >= after.Length / 3;
        }

        internal static bool FindTab(PixelFrame frame, BattlePassPage target, out Point point)
        {
            point = Point.Empty;
            var size = new Size(frame.Width, frame.Height);
            Point reward, tasks;
            if (!TryRailIcon(frame, Relative(size, .023, .142, .062, .207), out reward) ||
                !TryRailIcon(frame, Relative(size, .023, .264, .062, .329), out tasks))
                return false;
            point = target == BattlePassPage.Tasks ? tasks : reward;
            return true;
        }

        private static bool TryRailIcon(PixelFrame frame, Rectangle area, out Point center)
        {
            center = Point.Empty;
            Rectangle highlights = Rectangle.Empty;
            int pixels = 0;
            for (int y = area.Top; y < area.Bottom; y += 2)
                for (int x = area.Left; x < area.Right; x += 2)
                {
                    if (frame.Light(x, y) < 152)
                        continue;
                    var pixel = new Rectangle(x, y, 2, 2);
                    highlights = highlights.IsEmpty ? pixel : Rectangle.Union(highlights, pixel);
                    pixels++;
                }
            if (pixels < 12 || highlights.Width < area.Width * .23 || highlights.Height < area.Height * .20)
                return false;
            center = new Point(highlights.X + highlights.Width / 2, highlights.Y + highlights.Height / 2);
            return true;
        }

        private static int CountMatches(IEnumerable<OcrLine> lines, string[] labels, Rectangle area)
        {
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OcrLine line in lines)
            {
                string label = TextAt(line, area);
                if (label != null && Matches(label, labels))
                    matched.Add(label);
            }
            return matched.Count;
        }

        private static Rectangle Unique(IEnumerable<OcrLine> lines, string[] labels, Rectangle area)
        {
            Rectangle found = Rectangle.Empty;
            foreach (OcrLine line in lines)
            {
                string label = TextAt(line, area);
                if (label == null || !Matches(label, labels))
                    continue;
                if (!found.IsEmpty)
                    return Rectangle.Empty;
                found = line.Box;
            }
            return found;
        }

        private static bool HasLevelProgress(IEnumerable<OcrLine> lines, Rectangle area)
        {
            foreach (OcrLine line in lines)
            {
                string label = TextAt(line, area);
                if (label != null && LevelProgress.IsMatch(label))
                    return true;
            }
            return false;
        }

        private static string TextAt(OcrLine line, Rectangle area)
        {
            var center = new Point(line.Box.X + line.Box.Width / 2, line.Box.Y + line.Box.Height / 2);
            return line.Score >= .70f && !string.IsNullOrWhiteSpace(line.Text) && area.Contains(center) ?
                Regex.Replace(line.Text.Normalize(NormalizationForm.FormKC).Replace('臺', '台'), @"\s+", "")
                    .TrimEnd('。', '!', '.', ':') : null;
        }

        private static bool Matches(string text, string[] choices)
        {
            foreach (string choice in choices)
                if (string.Equals(text, choice, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static Rectangle Relative(Size size, double left, double top, double right, double bottom)
        {
            return Rectangle.FromLTRB((int)(size.Width * left), (int)(size.Height * top),
                (int)(size.Width * right), (int)(size.Height * bottom));
        }
    }
}
