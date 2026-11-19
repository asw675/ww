using System;

namespace TideCombat
{
    internal static class MergeChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void FullBatch()
        {
            Require(MergeMath.IsFullBatch("100/100"), "100/100 应判定满批");
            Require(MergeMath.IsFullBatch("100 / 100"), "带空格应判定满批");
            Require(!MergeMath.IsFullBatch("99/100"), "未满不应判定");
            Require(!MergeMath.IsFullBatch("1000/1000"), "1000/1000 不应误判");
            Require(!MergeMath.IsFullBatch("100/1000"), "100/1000 不应误判");
            Require(!MergeMath.IsFullBatch(null), "空文本不应判定");
        }
    }
}
