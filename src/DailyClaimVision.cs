using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal static class DailyClaimVision
    {
        private const float MinimumTextConfidence = .70f;
        internal static readonly string[] DailyHeadings =
        {
            "活跃度", "活躍度", "日常", "每日任务", "每日任務", "日常任务", "日常任務", "每日活跃", "每日活躍",
            "今日活跃", "今日活躍", "今日活跃度", "今日活躍度", "每日活跃度", "每日活躍度", "日常奖励", "日常獎勵",
            "Activity", "Daily Activity", "Daily Tasks"
        };
        internal static readonly string[] MailHeadings = { "邮件", "郵件", "信箱", "邮箱", "郵箱", "Mail", "Mailbox" };
        internal static readonly string[] DailyClaimButtons =
        {
            "一键领取", "一鍵領取", "全部领取", "全部領取", "领取奖励", "領取獎勵", "领取", "領取",
            "Claim All", "Claim Rewards", "Claim"
        };
        internal static readonly string[] MailClaimButtons =
        {
            "一键领取", "一鍵領取", "全部领取", "全部領取", "领取全部", "領取全部", "Claim All"
        };
        internal static readonly string[] DepletedClaims =
        {
            "已全部领取", "已全部領取", "奖励已全部领取", "獎勵已全部領取", "已领取奖励", "已領取獎勵",
            "已领取", "已領取", "暂无可领取奖励", "暫無可領取獎勵", "无可领取奖励", "無可領取獎勵",
            "没有可领取的奖励", "沒有可領取的獎勵", "不可领取", "不可領取", "进度未达成", "進度未達成",
            "All Claimed", "Already Claimed", "No Rewards to Claim", "No Claimable Rewards"
        };
        internal static readonly string[] EmptyMail =
        {
            "暂无邮件", "暫無郵件", "没有邮件", "沒有郵件", "暂无任何邮件", "暫無任何郵件", "没有任何邮件",
            "沒有任何郵件", "无邮件", "無郵件", "收件箱为空", "收件匣為空", "没有可领取的附件",
            "沒有可領取的附件", "暂无可领取附件", "暫無可領取附件", "No Mail", "Mailbox is Empty",
            "No Attachments to Claim"
        };
        private static readonly string[] MailClaimedNotices =
        {
            "全部邮件的附件已经领取完毕", "全部郵件的附件已經領取完畢"
        };
        internal static readonly string[] Receipts =
        {
            "获得道具", "獲得道具", "获得物品", "獲得物品", "获得奖励", "獲得獎勵",
            "获得以下道具", "獲得以下道具", "获得以下物品", "獲得以下物品", "Items Obtained", "Rewards Obtained"
        };
        internal static readonly string[] ReceiptCloseButtons =
        {
            "点击空白处关闭", "點擊空白處關閉", "点击任意处关闭", "點擊任意處關閉", "点击任意位置关闭",
            "點擊任意位置關閉", "点击空白区域关闭", "點擊空白區域關閉", "Click anywhere to close"
        };
        private static readonly string[] ObtainedReceiptTitle = { "获得", "獲得", "Obtained" };
        private static readonly string[] ContinueReceiptButtons =
        {
            "点击空白处继续", "點擊空白處繼續", "Click a blank area to continue", "Click anywhere to continue"
        };

        internal static bool Contains(IEnumerable<OcrLine> lines, params string[] accepted)
        {
            foreach (OcrLine line in lines)
                if (Matches(line, accepted))
                    return true;
            return false;
        }

        internal static Rectangle ReadObtainedReceiptDismissal(PixelFrame frame, OcrReader ocr)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || ocr == null)
                return Rectangle.Empty;
            var heading = ocr.ReadRelative(frame, .39, .19, .61, .34, centered: true);
            if (UniqueButton(heading, ObtainedReceiptTitle).IsEmpty)
                return Rectangle.Empty;
            return ObtainedReceiptDismissal(heading, ocr.ReadRelative(frame, .36, .83, .64, .95, centered: true));
        }

        internal static Rectangle ObtainedReceiptDismissal(IEnumerable<OcrLine> heading, IEnumerable<OcrLine> footer)
        {
            return UniqueButton(heading, ObtainedReceiptTitle).IsEmpty ? Rectangle.Empty :
                UniqueButton(footer, ContinueReceiptButtons);
        }

        private static Rectangle MailNoticeArea(PixelFrame frame)
        {
            return Rectangle.FromLTRB(frame.Width * 36 / 100, frame.Height * 17 / 100,
                frame.Width * 64 / 100, frame.Height * 222 / 1000);
        }

        internal static PixelFrame CaptureMailNoticeBackground(PixelFrame frame)
        {
            Rectangle area = MailNoticeArea(frame);
            var pixels = new byte[area.Width * area.Height * 4];
            frame.CopyBgra(area, pixels);
            return new PixelFrame(pixels, area.Width, area.Height);
        }

        internal static bool HasMailClaimedNotice(PixelFrame frame, PixelFrame before, OcrReader ocr)
        {
            Rectangle area = MailNoticeArea(frame);
            if (area.IsEmpty || ocr == null)
                return false;
            if (Contains(ocr.Read(frame, area), MailClaimedNotices))
                return true;
            if (area.Width != before.Width || area.Height != before.Height)
                return false;

            // The translucent notice fades over the mail list, which otherwise remains visible.
            var pixels = new byte[area.Width * area.Height * 4];
            int offset = 0;
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < area.Width; x++)
                {
                    int delta = frame.Light(area.Left + x, area.Top + y) - before.Light(x, y);
                    byte shade = (byte)Math.Max(0, Math.Min(255, 110 + 12 * delta));
                    pixels[offset++] = shade;
                    pixels[offset++] = shade;
                    pixels[offset++] = shade;
                    pixels[offset++] = 255;
                }
            }
            var difference = new PixelFrame(pixels, area.Width, area.Height);
            return Contains(ocr.Read(difference, new Rectangle(0, 0, area.Width, area.Height)), MailClaimedNotices);
        }

        internal static Rectangle UniqueButton(IEnumerable<OcrLine> lines, params string[] accepted)
        {
            Rectangle match = Rectangle.Empty;
            foreach (OcrLine line in lines)
            {
                if (!Matches(line, accepted)) continue;
                if (!match.IsEmpty && !Rectangle.Inflate(match, 8, 8).IntersectsWith(line.Box))
                    return Rectangle.Empty;
                match = line.Box;
            }
            return match;
        }

        internal static Rectangle FirstClaimRow(IEnumerable<OcrLine> lines)
        {
            Rectangle first = Rectangle.Empty;
            foreach (OcrLine line in lines)
                if (Matches(line, DailyClaimButtons) && (first.IsEmpty || line.Box.Top < first.Top))
                    first = line.Box;
            return first;
        }

        private static bool Matches(OcrLine line, string[] accepted)
        {
            if (line.Box.IsEmpty || line.Score < MinimumTextConfidence || string.IsNullOrWhiteSpace(line.Text))
                return false;
            string observed = Normalize(line.Text);
            foreach (string value in accepted)
                if (string.Equals(observed, Normalize(value), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string Normalize(string text)
        {
            return string.Concat(System.Linq.Enumerable.Where(text, character =>
                !char.IsWhiteSpace(character) && character != '。' && character != '!' && character != '！' &&
                character != '·' && character != ':'));
        }
    }
}
