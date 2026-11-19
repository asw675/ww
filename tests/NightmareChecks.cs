using System;

namespace TideCombat
{
    internal static class NightmareChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void Counts()
        {
            int denominator;
            Require(NestMath.TryIncomplete("0/24", out denominator) && denominator == 24, "0/24 应判定未完成");
            Require(NestMath.TryIncomplete("0/41", out denominator) && denominator == 41, "0/41 应判定未完成");
            Require(!NestMath.TryIncomplete("5/24", out denominator), "非零分子应判定已完成");
            Require(!NestMath.TryIncomplete("0/30", out denominator), "未知分母不应判定");
            Require(!NestMath.TryIncomplete("hello", out denominator), "无计数文本不应判定");
        }

        internal static void Keys()
        {
            string first = NestMath.CacheKey(NestTab.TacetNest, 24, 400);
            string near = NestMath.CacheKey(NestTab.TacetNest, 24, 404);
            string otherTab = NestMath.CacheKey(NestTab.Purification, 24, 400);
            Require(first == near, "轻微抖动应落同一行槽位");
            Require(first != otherTab, "不同页签应有不同键");
        }

        internal static void Options()
        {
            NightmareOptions options = NightmareOptions.Defaults();
            options.Validate();
            bool threw = false;
            try
            {
                options.TacetNest = false;
                options.Purification = false;
                options.Validate();
            }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "两种巢穴都不选时应拒绝");
        }
    }
}
