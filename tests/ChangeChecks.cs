using System;
using System.Drawing;

namespace TideCombat
{
    internal static class ChangeChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void TextFixes()
        {
            Require(ChangeMath.Fix("凝夜自霜") == "凝夜白霜", "凝夜自霜应纠正");
            Require(ChangeMath.Fix("主属性灭伤害加成") == "主属性湮灭伤害加成", "湮灭主属性应纠正");
            Require(ChangeMath.Fix("灭伤害加成") == "主属性湮灭伤害加成", "湮灭简称应纠正");
            Require(ChangeMath.Fix("主属性行射伤害加成") == "主属性衍射伤害加成", "衍射误识别应纠正");
            const string havoc = "湮灭伤害加成";
            var havocOption = new OcrLine { Text = havoc, Score = .99f, Box = new Rectangle(450, 150, 110, 24) };
            Require(ChangeMath.FindTarget(new[] { havocOption }, havoc) == havocOption &&
                ChangeMath.IsTarget(havoc, "主属性湮灭伤害加成") &&
                ChangeMath.IsTarget(havoc, "主属性：湮灭伤害加成 +30.0％"),
                "应保留并匹配正确识别的湮灭伤害加成");
            Require(ChangeMath.IsTarget(havoc, "主音属性：灭伤害加成 +30.0％"),
                "解析属性名和数值后仍应纠正湮灭误读");
            Require(ChangeMath.IsTarget("攻击", "主属性攻击"), "目标匹配应成功");
            Require(!ChangeMath.IsTarget("攻击", "主属性暴击"), "不匹配应失败");
            Require(!ChangeMath.IsTarget("暴击", "主属性暴击伤害") &&
                ChangeMath.IsTarget("暴击", "主属性：暴击 + 22.0％") &&
                ChangeMath.IsTarget("暴击", "主属性暴击率 +22.0%") &&
                ChangeMath.IsTarget("攻击", "主属性攻击力百分比"),
                "完整属性名要区分暴击与暴击伤害，同时允许数值");
            var damage = new OcrLine { Text = "暴击伤害", Score = .99f, Box = new Rectangle(450, 190, 80, 24) };
            var chance = new OcrLine { Text = "暴击", Score = .99f, Box = new Rectangle(450, 230, 65, 24) };
            Require(ChangeMath.FindTarget(new[] { damage, chance }, "暴击") == chance,
                "暴击伤害先出现时不能错选");
            var duplicate = new OcrLine { Text = "暴击", Score = .99f, Box = new Rectangle(450, 270, 65, 24) };
            Require(ChangeMath.FindTarget(new[] { chance, duplicate }, "暴击") == null,
                "多个不重叠的目标选项不能随意取第一个");
            Require(ChangeMath.ReadCurrentStat(new[] { damage }) == "暴击伤害" &&
                !ChangeMath.IsTarget("暴击", ChangeMath.ReadCurrentStat(new[] { damage })),
                "生产入口的当前主属性读取应保留完整名称，不能把暴击伤害当成暴击");
            Require(ChangeMath.ReadCurrentStat(new[] { damage, chance }) == null,
                "当前属性存在冲突时不得继续消耗修改资源");

            var title = new OcrLine { Text = "声骸技能", Score = .90f };
            var zero = new OcrLine { Text = "＋０", Score = .98f };
            var upgraded = new OcrLine { Text = "+25", Score = .98f };
            Require(ChangeMath.ReadBagLevel(new[] { upgraded, title }) == 25,
                "当前等级为正数时不能仅凭技能标题当作零级");
            Require(ChangeMath.ReadBagLevel(new[] { zero }) == 0,
                "明确识别 +0 时不依赖技能标题");
            Require(ChangeMath.ReadBagLevel(new[] { zero, upgraded, title }) == null,
                "冲突等级即使有技能标题也不能修改或声称耗尽");
            Require(ChangeMath.ReadBagLevel(new OcrLine[0]) == null,
                "OCR 漏读不能判定背包零级声骸耗尽");
            Require(ChangeMath.ReadBagLevel(new[] { title }) == null,
                "仅技能标题无法确认当前声骸的真实等级，不能自动消耗改主属性资源");
        }

        internal static void Options()
        {
            ChangeOptions options = ChangeOptions.Defaults();
            options.Validate();
            bool threw = false;
            try
            {
                options.Target = "不存在";
                options.Validate();
            }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "非法目标属性应拒绝");
        }
    }
}
