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
            int count;
            Require(MergeMath.TryReadBatch("0 / 100", out count) && count == 0, "须区分未读到批量与确实归零");
            Require(!MergeMath.TryReadBatch("移除选择", out count), "按钮其他文字无法确认剩余批量");
            Require(MergeMath.ExplicitlyInsufficient("弃置声骸数量未达到 1000"), "明确不足提醒可用");
            Require(!MergeMath.ExplicitlyInsufficient("取消") && !MergeMath.ExplicitlyInsufficient("声骸数量不足1000"),
                "不能从通用弹窗或其他计数推断弃置声骸不足");
            var empty = new OcrLine { Text = "0/100", Score = .99f };
            var full = new OcrLine { Text = "100/100", Score = .99f };
            Require(MergeMath.TryReadBatchLines(new[] { empty }, out count) && count == 0,
                "已确认的不足满批状态才能正常结束");
            Require(!MergeMath.TryReadBatchLines(new OcrLine[0], out count) && count == -1,
                "无 OCR 读数与剩余 0/100 不等价");
            Require(!MergeMath.TryReadBatchLines(new[] { empty, full }, out count) && count == -1,
                "冲突数量不能当作满批已经结束或可以提交");
            Require(!MergeMath.TryReadBatch("0/100 100/100", out count) && count == -1,
                "同一 OCR 行中的多个冲突数量也不能用于提交或结束");
        }
    }
}
