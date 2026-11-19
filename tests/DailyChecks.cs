using System;

namespace TideCombat
{
    internal static class DailyChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void Plan()
        {
            Require(DailyPlan.RewardReady(100), "100 积分应视为已领取");
            Require(!DailyPlan.RewardReady(99), "99 积分不应视为已领取");
            Require(DailyPlan.NeedsStamina(false, 0), "未满且未用完体力应继续刷");
            Require(!DailyPlan.NeedsStamina(true, 0), "积分已满不应刷体力");
            Require(!DailyPlan.NeedsStamina(false, 180), "体力已用满不应继续");
            Require(DailyPlan.NeedsNightmare(true, false, true, DomainKind.Tacet), "勾选全刷时始终刷梦魇");
            Require(!DailyPlan.NeedsNightmare(false, true, true, DomainKind.Forgery), "积分已满不必补梦魇");
            Require(DailyPlan.NeedsNightmare(false, true, false, DomainKind.Forgery), "缺日常时应补梦魇");
            Require(!DailyPlan.NeedsNightmare(false, true, false, DomainKind.Tacet), "刷无音区时参考不补梦魇");
        }

        internal static void Options()
        {
            DailyOptions options = DailyOptions.Defaults();
            options.Validate();
            bool threw = false;
            try
            {
                options.TacetIndex = 0;
                options.Validate();
            }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "越界序号应拒绝");
        }
    }
}
