using System;
using System.Collections.Generic;

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
            Require(GardenMath.IsFinished(new List<string>()), "没有 6000 视为结束");
        }

        internal static void Signatures()
        {
            foreach (string label in TaskIndicators.All)
                Require(PatternBank.For(label).Length > 0, "缺少签名: " + label);
        }
    }
}
