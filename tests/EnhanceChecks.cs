using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal static class EnhanceChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        private static EchoStat Stat(string name, double value)
        {
            return new EchoStat { Name = name, Value = value, Y = 0 };
        }

        private static bool Pass(EnhanceOptions options, params EchoStat[] stats)
        {
            string reason;
            return EchoRules.Evaluate(new List<EchoStat>(stats), options, out reason);
        }

        internal static void Rules()
        {
            var options = EnhanceOptions.Defaults();

            Require(Pass(options, Stat("暴击", 7.5), Stat("暴击伤害", 14)), "双爆达标应通过");
            Require(!Pass(options, Stat("暴击", 5.0), Stat("暴击伤害", 21)), "首条暴击不足应丢弃");
            Require(!Pass(options, Stat("暴击伤害", 13.0), Stat("暴击", 7.0)), "首条爆伤按一半计算不足应丢弃");
            Require(!Pass(options, Stat("暴击", 6.0), Stat("暴击伤害", 12)), "双爆总分不足应丢弃");
            Require(!Pass(options, Stat("暴击", 7.0), Stat("攻击百分比", 10), Stat("防御", 10),
                Stat("生命", 10), Stat("防御百分比", 10)), "剩余孔位不足双爆应丢弃");
            Require(!Pass(options, Stat("防御", 10), Stat("暴击", 7.0)), "双爆前出现无效词条应丢弃");
            Require(Pass(options, Stat("暴击", 7.0), Stat("防御", 10), Stat("攻击百分比", 9)), "有效词条数量达标应通过");
            Require(!Pass(options, Stat("防御", 10)), "首条无效应丢弃");

            var relaxed = EnhanceOptions.Defaults();
            relaxed.RequireBothCrit = false;
            relaxed.RequireValidBeforeCrit = false;
            relaxed.ValidCountMinimum = 1;
            relaxed.FirstMustBeValid = false;
            Require(Pass(relaxed, Stat("防御", 10)), "放宽条件后单条防御应通过");
        }

        internal static void Parsing()
        {
            var lines = new List<OcrLine>
            {
                new OcrLine { Text = "暴击", Box = new Rectangle(100, 300, 40, 20) },
                new OcrLine { Text = "7.5%", Box = new Rectangle(200, 302, 40, 20) },
                new OcrLine { Text = "攻击", Box = new Rectangle(100, 340, 40, 20) },
                new OcrLine { Text = "30", Box = new Rectangle(200, 341, 30, 20) },
                new OcrLine { Text = "辅音", Box = new Rectangle(100, 380, 40, 20) }
            };
            List<EchoStat> stats = EchoRules.Parse(lines);
            Require(stats.Count == 2, "应解析出两条词条");
            Require(stats[0].Name == "暴击" && Math.Abs(stats[0].Value - 7.5) < 1e-6, "暴击数值解析错误");
            Require(stats[1].Name == "攻击" && Math.Abs(stats[1].Value - 30) < 1e-6, "攻击数值解析错误");
        }

        internal static void Flow()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            long now = 1000;
            var frame = new EnhanceFrame { Width = 1600, Height = 900 };
            task.Observe(frame, now);
            frame.HasCultivate = true;
            frame.CultivateBox = new Rectangle(1300, 800, 80, 40);
            task.Observe(frame, now += 100);
            frame.HasCultivate = false;
            EnhanceDecision click = task.Observe(frame, now += 100);
            Require(click.Action == EnhanceAction.Click && click.Point.X == 208 && click.Point.Y == 189,
                "应点击第一格声骸");
            frame.LevelZero = true;
            task.Observe(frame, now += 600);
            task.Observe(frame, now += 100);
            Require(task.Status.Contains("培养"), "确认 0 级后应进入强化");
        }
    }
}
