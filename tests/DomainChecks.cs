using System;

namespace TideCombat
{
    internal static class DomainChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void Arithmetic()
        {
            int current, backup, total;
            DomainMath.ParseStamina(new[] { "160/240" }, out current, out backup, out total);
            Require(current == 160 && total == 240 && backup == 80, "成对体力解析错误");

            DomainMath.ParseStamina(new[] { "120" }, out current, out backup, out total);
            Require(current == 0 && backup == 120 && total == 120, "备用体力解析错误");

            double previous = 0;
            for (int serial = 1; serial <= 20; serial++)
            {
                double y = DomainMath.ScrollOffset(.1963, serial, 21, 4);
                Require(y > previous && y <= .8806, "滚动位置应递增且不超过底部");
                previous = y;
            }
            double fallback = DomainMath.ScrollOffset(0, 5, 0, 4);
            Require(Math.Abs(fallback - .1963) < 1e-9, "无总数量时应回退顶部");
        }

        internal static void Options()
        {
            DomainOptions options = DomainOptions.Defaults();
            options.Validate();
            Require(options.Kind == DomainKind.Tacet && options.Index == 1, "默认体力本配置错误");
            bool threw = false;
            try
            {
                options.Index = 0;
                options.Validate();
            }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "越界序号应拒绝");
        }
    }
}
