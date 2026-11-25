using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal static class GardenChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void Progress()
        {
            Require(GardenMath.IsCompleted("本周积分 6000/6000"), "应识别已满积分");
            Require(!GardenMath.IsCompleted("本周积分 5200"), "未满不应结束");
            Require(!GardenMath.IsFinished(new[] { "挑战成功 积分为 6000" }), "单次 6000 视为未结束");
            Require(GardenMath.IsFinished(new[] { "6000", "6000" }), "出现两次 6000 视为结束");
            Require(GardenMath.IsFinished(new List<string>()), "两个结算按钮均出现时，零个 6000 与原版一样返回主页");
            Require(!GardenMath.IsCompleted("本周积分 5200/6000"), "分母 6000 不等于当前积分");
            Require(GardenMath.IsCompleted("已達到上限"), "实际乐园入口显示达到上限");
            Require(!GardenMath.HasWeeklyLimit(new[]
            {
                new OcrLine { Text = "5200/6000", Score = .96f },
                new OcrLine { Text = "6000/6000", Score = .45f }
            }), "分母和低置信度读数不能确认周上限");
            Require(GardenMath.HasWeeklyLimit(new[] { new OcrLine { Text = "6000/6000", Score = .95f } }),
                "确认当前积分和上限相等");
            Require(!GardenMath.HasWeeklyLimit(new List<OcrLine>()), "缺失积分读数不能证明结算完成");
            Require(Array.IndexOf(GardenRunner.EventLabels, "garden_get_confirm_gray") < 0 &&
                Array.IndexOf(GardenRunner.EventLabels, "garden_not_interested_confirm") < 0 &&
                Array.IndexOf(GardenRunner.EventLabels, "garden_not_interested") < 0 &&
                Array.IndexOf(GardenRunner.EventLabels, "garden_get_gold") < 0 &&
                Array.IndexOf(GardenRunner.EventLabels, "garden_next_day") < 0,
                "奖励选择和下一日需要独立识别，依赖型确认仅随发起动作处理");
            Require(GardenMath.PreferredPoint(new Size(1000, 1000), new Rectangle(900, 870, 60, 50), .927, .893) ==
                new Point(927, 893) &&
                GardenMath.PreferredPoint(new Size(1000, 1000), new Rectangle(800, 840, 60, 50), .927, .893) ==
                new Point(830, 865), "原版固定位置落在已核实控件内时使用；移位控件不盲点固定位置");
            Require(GardenMath.WeeklyClaimSettleMs(true, 0) == 3000 && GardenMath.WeeklyClaimSettleMs(true, 1) == 2000 &&
                GardenMath.WeeklyClaimSettleMs(false, 0) == 2000 && GardenMath.WeeklyClaimSettleMs(false, 1) == 1000,
                "首次进入与结算返回的两次奖励领取节奏均应匹配原版");
        }

        internal static void NextDayRequiresTextAndAStableTransition()
        {
            var nextDay = new OcrLine { Text = "下一日", Score = .95f, Box = new Rectangle(1330, 808, 66, 26) };
            Require(GardenMath.NextDayButton(new[] { nextDay }) == nextDay.Box &&
                GardenMath.NextDayButton(new[] { new OcrLine { Text = "跳过", Score = 1, Box = nextDay.Box } }).IsEmpty,
                "a patterned strip or a different button must not qualify as next day");
            Require(GardenMath.ReadDay(new[] { new OcrLine { Text = "第 2 日", Score = .96f } }) == 2,
                "a new explicit day can verify advancement with the same button");
            Require(!GardenMath.HasGardenResult(new[] { new OcrLine { Text = "挑战成功", Score = .98f } }),
                "unrelated domain settlement does not qualify as a Garden result");

            var check = new GardenEventProgress();
            Require(!check.Observe(1, "day-2") && !check.Observe(1, "day-2") &&
                !check.Observe(2, null) && !check.Observe(3, "day-2") && check.Observe(4, "day-2"),
                "a repeated capture or a transient new day must not count twice");

            foreach (bool responds in new[] { false, true })
            {
                long now = 0;
                long frame = 0;
                int clicks = 0;
                var replay = new GardenEventProgress();
                bool advanced = WorldSession.RunMenuTransition(() => { frame++; return true; }, () => true,
                    () => true, () => now, ms => now += ms, ms => now += ms, () => nextDay.Box,
                    () => clicks != 0 && replay.Observe(frame, responds && now >= 120 ? "day-2" : null),
                    (button, retry) => { clicks++; return true; }, 1500, 0);
                Require(advanced == responds && clicks == (responds ? 1 : 2),
                    "unchanged frames get one retry and zero progress, a real transition gets one click and counts once");
            }
        }

        internal static void RewardBoardAndOfferProgress()
        {
            var weekly = new OcrLine { Text = "本周游园", Score = .96f, Box = new Rectangle(60, 40, 96, 23) };
            var stage = new OcrLine { Text = "阶段：2/7", Score = .96f, Box = new Rectangle(94, 87, 101, 23) };
            var day = new OcrLine { Text = "第1天", Score = .96f, Box = new Rectangle(95, 125, 59, 24) };
            GardenBoardPosition board;
            Require(GardenMath.ReadBoard(new[] { weekly, stage, day }, out board) &&
                board.Stage == 2 && board.Day == 1 && board.IsAfter(new GardenBoardPosition { Stage = 1, Day = 5 }),
                "the board can advance to a new stage while the day counter resets");
            Require(!GardenMath.ReadBoard(new[] { stage, day }, out board),
                "a day number alone cannot verify the Garden board");
            Require(!new GardenBoardPosition { Stage = 1, Day = 5 }.IsAfter(new GardenBoardPosition { Stage = 2, Day = 1 }),
                "a backwards stage cannot pass as an advancing day");
            var first = new OcrLine { Text = "湮灭棱镜", Score = .98f, Box = new Rectangle(293, 167, 90, 24) };
            var second = new OcrLine { Text = "先锋幼岩", Score = .98f, Box = new Rectangle(670, 167, 90, 24) };
            var third = new OcrLine { Text = "咕咕河豚", Score = .98f, Box = new Rectangle(1048, 167, 90, 24) };
            var shifted = new OcrLine { Text = first.Text, Score = .95f, Box = new Rectangle(290, 163, 89, 24) };
            var changed = new OcrLine { Text = "鸣钟之龟", Score = .98f, Box = third.Box };
            string original = GardenMath.RewardOfferKey(new[] { first, second, third }, 1600);
            string nextOffer = GardenMath.RewardOfferKey(new[] { first, second, changed }, 1600);
            Require(original != null && nextOffer != null && original != nextOffer &&
                GardenMath.RewardOfferKey(new[] { shifted, second, third }, 1600) == original &&
                GardenMath.RewardOfferKey(new[] { first, third }, 1600) == null,
                "selection geometry may shift, but a distinct offer requires readable current card titles");
        }

        internal static void Signatures()
        {
            foreach (string label in TaskIndicators.All)
                Require(PatternBank.For(label).Length > 0, "缺少签名: " + label);
        }
    }
}
