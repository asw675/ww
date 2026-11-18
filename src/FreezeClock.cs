using System.Collections.Generic;

namespace TideCombat
{
    internal sealed class FreezeClock
    {
        private struct Interval
        {
            internal long Start, Duration;
            internal bool Intro;
        }

        private readonly List<Interval> intervals = new List<Interval>();

        internal void Record(long start, long duration, long observedAt, bool intro = false, int baseAllowance = 100)
        {
            long allowance = intro ? 0 : baseAllowance;
            if (start < 0 || duration <= allowance) return;
            intervals.RemoveAll(interval => interval.Start <= observedAt - 60000);
            intervals.Add(new Interval { Start = start, Duration = duration - allowance, Intro = intro });
        }

        internal long Elapsed(long since, long now, bool includeIntro = false)
        {
            if (since < 0) return 10000000;
            long elapsed = now - since;
            foreach (Interval interval in intervals)
                if (interval.Start > since && (!interval.Intro || includeIntro))
                    elapsed -= interval.Duration;
            return elapsed;
        }

        internal void Clear() { intervals.Clear(); }

    }
}
