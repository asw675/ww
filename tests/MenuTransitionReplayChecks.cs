using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TideCombat
{
    internal static class MenuTransitionReplayChecks
    {
        private static readonly Rectangle Original = new Rectangle(90, 70, 140, 40);
        private static readonly Rectangle Shifted = new Rectangle(96, 72, 140, 40);
        private static readonly Rectangle Other = new Rectangle(480, 70, 140, 40);

        private enum Screen { Missing, Loading, Original, Shifted, Other, Destination }

        private sealed class Replay
        {
            private readonly Func<long, Screen> screen;
            private Screen frame;
            private long now;
            private bool captured;
            internal readonly List<Tuple<long, Rectangle, bool>> Clicks = new List<Tuple<long, Rectangle, bool>>();
            internal readonly List<int> Settles = new List<int>();
            internal Func<long, bool> SameRoute = time => true;
            internal Func<long, bool> Cancelled = time => false;
            internal Action OnFind;
            internal Action OnDestination;
            internal Func<int, bool> Delivered = attempt => true;

            internal Replay(Func<long, Screen> screen)
            {
                this.screen = screen;
            }

            internal bool Run(int timeout = 180, int settle = 0)
            {
                return WorldSession.RunMenuTransition(Capture, () => captured,
                    () => SameRoute(now) && !Cancelled(now), () => now, pause => now += pause,
                    delay =>
                    {
                        Settles.Add(delay);
                        now += delay;
                    }, FindControl, AtDestination, Dispatch, timeout, settle);
            }

            private bool Capture()
            {
                frame = screen(now);
                captured = frame != Screen.Missing;
                return captured;
            }

            private bool AtDestination()
            {
                if (OnDestination != null) OnDestination();
                return captured && frame == Screen.Destination;
            }

            private Rectangle FindControl()
            {
                if (OnFind != null) OnFind();
                if (frame == Screen.Original) return Original;
                if (frame == Screen.Shifted) return Shifted;
                return frame == Screen.Other ? Other : Rectangle.Empty;
            }

            private bool Dispatch(Rectangle control, bool retry)
            {
                Clicks.Add(Tuple.Create(now, control, retry));
                return Delivered(Clicks.Count);
            }
        }

        internal static void ReplaysInterruptedTransitions()
        {
            var recovered = new Replay(time =>
            {
                if (time == 0 || time == 120 || time == 300) return Screen.Missing;
                if (time < 180) return Screen.Original;
                if (time < 240) return Screen.Loading;
                if (time < 420) return Screen.Original;
                return time < 480 ? Screen.Shifted : Screen.Destination;
            })
            {
                Delivered = attempt => attempt == 2
            };
            Require(recovered.Run(settle: 60), "missing captures or first-click transport failure prevented recovery");
            Require(recovered.Clicks.SequenceEqual(new[]
            {
                Tuple.Create(60L, Original, false), Tuple.Create(420L, Shifted, true)
            }), "retry fired before two consecutive matching controls or used stale geometry");
            Require(recovered.Settles.SequenceEqual(new[] { 60 }), "settle should only follow a delivered click");

            var late = new Replay(time =>
            {
                if (time == 0 || time == 180) return Screen.Original;
                if (time < 180) return Screen.Loading;
                return Screen.Destination;
            })
            {
                Delivered = attempt => false
            };
            Require(late.Run() && late.Clicks.Count == 1,
                "a destination arriving between retry samples must win even if the first send reported failure");

            var different = new Replay(time => time == 0 ? Screen.Original : Screen.Other);
            Require(!different.Run() && different.Clicks.Count == 1,
                "another control on the same input route must not be used for replay");

            var changed = new Replay(time => time < 120 ? Screen.Original : Screen.Destination)
            {
                SameRoute = time => time < 120
            };
            Require(!changed.Run() && changed.Clicks.Count == 1,
                "a destination on a replacement route must not complete the operation");

            bool routeChangedDuringDestinationRead = false;
            var staleDestination = new Replay(time => Screen.Destination)
            {
                SameRoute = time => !routeChangedDuringDestinationRead,
                OnDestination = () => routeChangedDuringDestinationRead = true
            };
            Require(!staleDestination.Run() && staleDestination.Clicks.Count == 0,
                "a route change during a destination check must invalidate its result");

            bool canceledDuringControlRead = false;
            var canceled = new Replay(time => Screen.Original)
            {
                Cancelled = time => canceledDuringControlRead,
                OnFind = () => canceledDuringControlRead = true
            };
            Require(!canceled.Run() && canceled.Clicks.Count == 0,
                "cancellation during screen recognition must block input");

            var immediate = new Replay(time => Screen.Destination);
            Require(immediate.Run() && immediate.Clicks.Count == 0,
                "the already-visible destination should complete without input");

            var unchanged = new Replay(time => Screen.Original);
            Require(!unchanged.Run() && unchanged.Clicks.Count == 2 &&
                unchanged.Clicks.Select(click => click.Item3).SequenceEqual(new[] { false, true }),
                "a permanently unchanged page must exhaust precisely one fallback");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
