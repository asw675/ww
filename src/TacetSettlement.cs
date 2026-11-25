using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal sealed class TacetDebitCheck
    {
        private int consecutive;
        internal int? Remaining { get; private set; }

        internal bool Observe(TacetSettlement settlement, int availableBefore, int charged)
        {
            if (!settlement.ConfirmsSpend(availableBefore, charged))
            {
                Reset();
                return false;
            }
            consecutive = Remaining == settlement.Remaining ? consecutive + 1 : 1;
            Remaining = settlement.Remaining;
            return consecutive >= 2;
        }

        internal void Reset()
        {
            consecutive = 0;
            Remaining = null;
        }
    }

    internal struct TacetSettlement
    {
        private static readonly Regex RemainingText = new Regex(@"^(?:剩[余餘]|Remaining)[:：]?([0-9]{1,4})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal Rectangle Exit, Restart;
        internal int? Remaining;
        internal bool BalanceAmbiguous;

        internal static bool TryRead(PixelFrame frame, OcrReader ocr, out TacetSettlement settlement)
        {
            settlement = default(TacetSettlement);
            if (frame.Width <= 0 || frame.Height <= 0 || ocr == null) return false;
            try
            {
                var heading = ocr.ReadRelative(frame, .33, .23, .67, .345, centered: true);
                return HasSuccessHeading(heading) &&
                    TryParseActions(ocr.ReadRelative(frame, .25, .805, .75, .95, centered: true), out settlement);
            }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
            {
                return false;
            }
        }

        internal static bool HasSuccessHeading(IEnumerable<OcrLine> lines)
        {
            return DailyClaimVision.Contains(lines, "挑战成功", "挑戰成功", "Challenge Complete", "Challenge Completed",
                "Challenge Successful");
        }

        internal static bool TryParseActions(IList<OcrLine> lines, out TacetSettlement settlement)
        {
            settlement = default(TacetSettlement);
            Rectangle exit = DailyClaimVision.UniqueButton(lines, "退出副本", "离开副本", "離開副本", "Exit Domain", "Leave Domain");
            Rectangle restart = DomainMath.RechallengeButton(lines);
            if (exit.IsEmpty || restart.IsEmpty || exit.Right >= restart.Left ||
                Math.Abs(exit.Top - restart.Top) > Math.Max(exit.Height, restart.Height)) return false;

            settlement.Exit = exit;
            settlement.Restart = restart;
            foreach (OcrLine line in lines)
            {
                if (line.Box.IsEmpty || line.Score < .72f || line.Box.Top <= restart.Top ||
                    line.Box.Right < restart.Left || line.Box.Left > restart.Right) continue;
                Match text = RemainingText.Match(OcrText.Compact(line.Text));
                int remaining;
                if (!text.Success || !int.TryParse(text.Groups[1].Value, out remaining)) continue;
                if (settlement.Remaining.HasValue && settlement.Remaining.Value != remaining)
                    settlement.BalanceAmbiguous = true;
                settlement.Remaining = remaining;
            }
            return true;
        }

        internal bool ConfirmsSpend(int availableBefore, int charged)
        {
            if (Exit.IsEmpty || Restart.IsEmpty || BalanceAmbiguous || !Remaining.HasValue ||
                charged <= 0 || availableBefore < charged)
                return false;
            int debit = availableBefore - Remaining.Value;
            return debit == charged || debit == charged - 1;
        }
    }
}
