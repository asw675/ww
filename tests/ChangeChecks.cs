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
