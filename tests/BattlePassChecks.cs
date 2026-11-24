using System;
using System.Drawing;
using System.IO;

namespace TideCombat
{
    internal static class BattlePassChecks
    {
        internal static void ConfirmAnchorsAndClaimControl()
        {
            var size = new Size(1600, 900);
            var title = At("先约电台", 80, 72);
            var daily = At("晨间放送", 280, 285);
            var weekly = At("本周热播", 275, 385);
            var claimAll = At("一键领取", 1090, 819);
            var unlock = At("解锁寰宇频道", 1385, 819);
            Rectangle entry = BattlePassVision.FindTerminalEntry(new[]
            {
                At("先约随心频道", 1040, 275), At("先約電臺", 1193, 265)
            }, size);
            Require(entry.X == 1193, "an entry must match the actual Pioneer Podcast title");

            var unrelated = BattlePassVision.Inspect(new[] { At("51", 375, 149), claimAll }, size);
            Require(unrelated.Page == BattlePassPage.Unknown && unrelated.ClaimAll.IsEmpty,
                "digits and a claim label must not identify a battle pass");

            var tasks = BattlePassVision.Inspect(new[] { title, daily, weekly, claimAll, unlock }, size);
            Require(tasks.Page == BattlePassPage.Tasks && tasks.ClaimAll == claimAll.Box,
                "a verified task page must expose only its exact claim-all button");

            var free = At("大众频道", 350, 420);
            var premium = At("寰宇频道", 350, 560);
            var nothingRecognized = BattlePassVision.Inspect(new[] { title, free, premium, unlock }, size);
            Require(nothingRecognized.Page == BattlePassPage.Rewards && !nothingRecognized.NoPendingRewards &&
                nothingRecognized.ClaimAll.IsEmpty, "an invisible claim control is not evidence of an empty queue");
            var empty = BattlePassVision.Inspect(new[] { title, free, premium, At("暂无可领取奖励", 700, 570) }, size);
            Require(empty.Page == BattlePassPage.Rewards && empty.NoPendingRewards, "explicit empty status was lost");
            Require(BattlePassVision.IsSeasonUnavailable(new[] { At("本期先约电台已结束", 680, 420) }, size) &&
                BattlePassVision.IsSeasonUnavailable(new[] { At("Pioneer Podcast is not currently available", 650, 400) }, size) &&
                BattlePassVision.IsSeasonUnavailable(new[] { title, At("本期已结束", 680, 430) }, size),
                "only explicit unavailable-season evidence may skip a battle pass");
            Require(!BattlePassVision.IsSeasonUnavailable(new OcrLine[0], size) &&
                !BattlePassVision.IsSeasonUnavailable(new[] { At("活动已结束", 680, 420) }, size) &&
                !BattlePassVision.IsSeasonUnavailable(new[] { title }, size) &&
                !BattlePassVision.IsSeasonUnavailable(new[] { title, daily, weekly, claimAll, At("本期先约电台已结束", 680, 420) }, size),
                "missing OCR, unrelated activity or active claimable panel is not a verified season closure");
            OcrLine uncertainSeason = At("先约电台暂未开放", 680, 420); uncertainSeason.Score = .5f;
            Require(!BattlePassVision.IsSeasonUnavailable(new[] { uncertainSeason }, size), "low confidence season text is not explicit evidence");
        }

        internal static void VerifyTerminalEntryReference(string root)
        {
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            using (var bitmap = new Bitmap(Path.Combine(root, "ok_templates", "14.png")))
            {
                PixelFrame frame = reader.Read(bitmap);
                Require(new FarmVision().IsTerminal(frame), "the game fixture must be a verified Terminal");
                var box = new Rectangle((int)(frame.Width * .30), (int)(frame.Height * .13),
                    (int)(frame.Width * .65), (int)(frame.Height * .67));
                Rectangle entry = BattlePassVision.FindTerminalEntry(ocr.Read(frame, box), new Size(frame.Width, frame.Height));
                Require(!entry.IsEmpty && entry.X > frame.Width * .69 && entry.X < frame.Width * .79,
                    "localized Terminal text must resolve to the Pioneer Podcast tile");
            }
        }

        internal static void CompareClaimButtonPixels()
        {
            var before = new PixelFrame(new byte[160 * 90 * 4], 160, 90);
            var changedPixels = new byte[160 * 90 * 4];
            var button = new Rectangle(95, 65, 45, 15);
            for (int y = button.Top; y < button.Bottom; y++)
                for (int x = button.Left; x < button.Right; x++)
                {
                    int start = (y * 160 + x) * 4;
                    changedPixels[start] = changedPixels[start + 1] = changedPixels[start + 2] = 180;
                }
            var after = new PixelFrame(changedPixels, 160, 90);
            int[] original = BattlePassVision.ButtonPatch(before, button);
            Require(!BattlePassVision.ButtonChanged(before, button, original),
                "an OCR miss on unchanged pixels cannot confirm a claim");
            Require(BattlePassVision.ButtonChanged(after, button, original),
                "a substantive change in the previously matched button must be detected");
        }

        private static OcrLine At(string text, int x, int y)
        {
            return new OcrLine { Text = text, Score = .95f, Box = new Rectangle(x, y, 72, 22) };
        }

        private static void Require(bool valid, string error)
        {
            if (!valid)
                throw new InvalidOperationException(error);
        }
    }
}
