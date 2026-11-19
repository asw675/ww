using System;

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
