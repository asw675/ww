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
            Require(Array.IndexOf(GardenRunner.EventLabels, "garden_get_gold") >= 0 &&
                Array.IndexOf(GardenRunner.EventLabels, "garden_get_purple") >= 0,
                "即使跳过按钮漏识别也要参与普通金色、紫色奖励匹配");
            Require(Array.IndexOf(GardenRunner.EventLabels, "garden_get_confirm_gray") < 0 &&
                Array.IndexOf(GardenRunner.EventLabels, "garden_not_interested_confirm") < 0,
                "两种确认模板仅应随相应发起动作处理，与原版普通扫描一致");
            Require(GardenMath.PreferredPoint(new Size(1000, 1000), new Rectangle(900, 870, 60, 50), .927, .893) ==
                new Point(927, 893) &&
                GardenMath.PreferredPoint(new Size(1000, 1000), new Rectangle(800, 840, 60, 50), .927, .893) ==
                new Point(830, 865), "原版固定位置落在已核实控件内时使用；移位控件不盲点固定位置");
            Require(GardenMath.WeeklyClaimSettleMs(true, 0) == 3000 && GardenMath.WeeklyClaimSettleMs(true, 1) == 2000 &&
                GardenMath.WeeklyClaimSettleMs(false, 0) == 2000 && GardenMath.WeeklyClaimSettleMs(false, 1) == 1000,
                "首次进入与结算返回的两次奖励领取节奏均应匹配原版");
        }

        internal static void Signatures()
        {
            foreach (string label in TaskIndicators.All)
                Require(PatternBank.For(label).Length > 0, "缺少签名: " + label);
        }
    }
}
