using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class MenuInputChecks
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct QueuedMessage
        {
            internal IntPtr Window;
            internal uint Code;
            internal IntPtr WParam, LParam;
            internal uint At;
            internal Native.Point Point;
            internal uint Reserved;
        }

        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        private static extern bool PeekMessage(out QueuedMessage message, IntPtr window,
            uint first, uint last, uint remove);

        private sealed class Sink : NativeWindow, IDisposable
        {
            internal readonly List<Message> Messages = new List<Message>();

            internal Sink(IntPtr parent, Rectangle area)
            {
                bool child = parent != IntPtr.Zero;
                CreateHandle(new CreateParams
                {
                    Parent = parent,
                    X = area.X, Y = area.Y, Width = area.Width, Height = area.Height,
                    Style = child ? 0x50000000 : unchecked((int)0x90000000),
                    ExStyle = child ? 0 : 0x08000080
                });
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0006 || message.Msg >= 0x0100 && message.Msg <= 0x0105 ||
                    message.Msg >= 0x0200 && message.Msg <= 0x020A)
                {
                    Messages.Add(message);
                    return;
                }
                base.WndProc(ref message);
            }

            public void Dispose() { DestroyHandle(); }
        }

        internal static void BackgroundDispatch()
        {
            using (var root = new Sink(IntPtr.Zero, new Rectangle(-21100, -21200, 300, 200)))
            using (var child = new Sink(root.Handle, new Rectangle(40, 50, 100, 100)))
            {
                Application.DoEvents();
                root.Messages.Clear();
                child.Messages.Clear();
                uint owner;
                Native.GetWindowThreadProcessId(root.Handle, out owner);
                var point = new Point(60, 75);
                var token = CancellationToken.None;
                IntPtr window = root.Handle;
                Require(!WindowMessages.ClickBackground(window, owner + 1, point, FarmMouse.Right, 1, 0,
                    token, () => true), "background click accepted an incorrect process");
                Require(WindowMessages.ClickBackground(window, owner, point, FarmMouse.Right, 1, 0,
                    token, () => true, 4), "failed to send right-click to game child");
                Require(WindowMessages.ScrollBackground(window, owner, point, -2, 4), "failed to send wheel to game child");
                Application.DoEvents();

                Message[] mouse = child.Messages.ToArray();
                Require(mouse.Select(m => m.Msg).SequenceEqual(new[] { 0x0200, 0x0204, 0x0205, 0x020A }) &&
                    !root.Messages.Any(), "click/wheel reached a parent or sent activation. Child=" +
                    string.Join(",", mouse.Select(m => m.Msg.ToString("X"))) + " Root=" +
                    string.Join(",", root.Messages.Select(m => m.Msg.ToString("X"))));
                int localPoint = 20 | 25 << 16;
                Require(mouse.Take(3).All(m => m.LParam.ToInt32() == localPoint) &&
                    mouse[0].WParam.ToInt32() == 4 && mouse[1].WParam.ToInt32() == 6 &&
                    mouse[2].WParam.ToInt32() == 4, "right button or modifier flags were incorrect");
                var screen = new Native.Point { X = point.X, Y = point.Y };
                Require(Native.ClientToScreen(window, ref screen), "could not resolve test screen point");
                int wheelFlags = mouse[3].WParam.ToInt32();
                int wheelPoint = mouse[3].LParam.ToInt32();
                Require((short)(wheelFlags >> 16) == -240 && (wheelFlags & 0xffff) == 4 &&
                    (short)wheelPoint == screen.X && (short)(wheelPoint >> 16) == screen.Y,
                    "wheel delivery lost the signed delta, modifier, or signed screen coordinates");

                Func<bool> release;
                bool ready = true;
                Require(WindowMessages.TryHoldKey(window, owner, 0x12, out release, () => ready),
                    "failed to send background Alt down");
                ready = false;
                Require(release(), "Alt release did not use its original window after mode changed");
                Application.DoEvents();
                Message[] keys = root.Messages.ToArray();
                Require(keys.Select(m => m.Msg).SequenceEqual(new[] { 0x0104, 0x0105 }) &&
                    keys.All(m => m.WParam.ToInt32() == 0x12) &&
                    (keys[0].LParam.ToInt64() & 0xe0000000L) == 0x20000000L &&
                    (keys[1].LParam.ToInt64() & 0xe0000000L) == 0xe0000000L,
                    "Alt system messages were missing or had incorrect state bits");

                const string characters = "声骸\U0001F30A";
                Require(!WindowMessages.TypeText(window, owner + 1, characters, token) &&
                    WindowMessages.TypeText(window, owner, characters, token), "background text accepted the wrong target");
                var postedText = new List<char>();
                QueuedMessage queued;
                // WinForms pre-processes text dispatched to inactive HWNDs. Inspect the actual input queue.
                while (PeekMessage(out queued, window, 0x0102, 0x0102, 1))
                    postedText.Add((char)queued.WParam.ToInt32());
                Require(postedText.SequenceEqual(characters), "background text delivery changed UTF-16 code units");
            }
        }

        internal static void BackgroundTargetDispatch()
        {
            using (var root = new Sink(IntPtr.Zero, new Rectangle(-21100, -21200, 320, 200)))
            {
                uint owner;
                Native.GetWindowThreadProcessId(root.Handle, out owner);
                var input = new WindowMessages();
                var target = BattleAction.Target();
                var token = CancellationToken.None;
                using (var container = new Sink(root.Handle, new Rectangle(40, 35, 250, 135)))
                using (var viewport = new Sink(container.Handle, new Rectangle(90, 42, 64, 45)))
                {
                    Application.DoEvents();
                    root.Messages.Clear();
                    container.Messages.Clear();
                    viewport.Messages.Clear();
                    Require(!input.Send(target, root.Handle, owner + 1, token),
                        "background target accepted an incorrect process");
                    Require(!input.Send(target, root.Handle, owner, new CancellationToken(true)),
                        "cancelled background target was sent");
                    int readinessChecks = 0;
                    Require(!input.Send(target, root.Handle, owner, token, () => ++readinessChecks == 1),
                        "background target was sent after its input route changed during the mouse move");
                    Application.DoEvents();
                    Require(!root.Messages.Any() && !container.Messages.Any() &&
                        viewport.Messages.Select(message => message.Msg).SequenceEqual(new[] { 0x0200 }),
                        "a button or activation message escaped the rejected background target");
                    viewport.Messages.Clear();

                    Require(input.Send(BattleAction.Press(BattleButton.Attack, 0, true), root.Handle, owner, token),
                        "failed to hold left mouse before background target");
                    Application.DoEvents();
                    root.Messages.Clear();
                    container.Messages.Clear();
                    viewport.Messages.Clear();
                    Require(input.Send(target, root.Handle, owner, token), "background target was not sent");
                    Application.DoEvents();

                    int viewportCenter = 30 | 23 << 16;
                    Require(!root.Messages.Any() && !container.Messages.Any(),
                        "background target reached a parent or sent activation");
                    Require(viewport.Messages.Select(message => message.Msg).SequenceEqual(new[] { 0x0200, 0x0207, 0x0208 }) &&
                        viewport.Messages.All(message => message.LParam.ToInt32() == viewportCenter) &&
                        viewport.Messages.Select(message => message.WParam.ToInt32()).SequenceEqual(new[] { 1, 0x11, 1 }),
                        "background target must move, press and release middle mouse at the verified child point without releasing left mouse");
                    Require(input.ReleaseAll() && !input.ReleaseAll(), "middle click must leave only the original left hold outstanding");

                    Application.DoEvents();
                    viewport.Messages.Clear();
                    Require(input.Send(BattleAction.Press(BattleButton.MiddleClick, 0x04, false), root.Handle, owner, token),
                        "a rotation's middle-click target was not sent");
                    Application.DoEvents();
                    Require(viewport.Messages.Select(message => message.Msg).SequenceEqual(new[] { 0x0200, 0x0207, 0x0208 }) &&
                        viewport.Messages.All(message => message.LParam.ToInt32() == viewportCenter) &&
                        viewport.Messages.Select(message => message.WParam.ToInt32()).SequenceEqual(new[] { 0, 0x10, 0 }),
                        "rotation middle-click must use the same verified viewport as automatic targeting");
                }
                Application.DoEvents();
                root.Messages.Clear();
                Require(input.Send(target, root.Handle, owner, token), "background target without children was not sent");
                Application.DoEvents();
                int rootCenter = 160 | 100 << 16;
                Require(root.Messages.Select(message => message.Msg).SequenceEqual(new[] { 0x0200, 0x0207, 0x0208 }) &&
                    root.Messages.All(message => message.LParam.ToInt32() == rootCenter) &&
                    root.Messages.Select(message => message.WParam.ToInt32()).SequenceEqual(new[] { 0, 0x10, 0 }),
                    "without a child, the same middle-click sequence must reach the game window itself");
            }
        }

        internal static void ReplayTransitionAgainstWindow()
        {
            using (var root = new Sink(IntPtr.Zero, new Rectangle(-21100, -21200, 300, 200)))
            using (var child = new Sink(root.Handle, new Rectangle(40, 50, 100, 100)))
            {
                Application.DoEvents();
                root.Messages.Clear();
                child.Messages.Clear();
                uint owner;
                Native.GetWindowThreadProcessId(root.Handle, out owner);
                var control = new Rectangle(55, 70, 12, 10);
                int dispatched = 0;
                long now = 0, destinationAt = long.MaxValue;
                bool arrived = WorldSession.RunMenuTransition(() => true, () => true,
                    () =>
                    {
                        uint current;
                        return Native.GetWindowThreadProcessId(root.Handle, out current) != 0 && current == owner;
                    },
                    () => now, delay => now += delay, delay => now += delay,
                    () => control, () => now >= destinationAt, (button, retry) =>
                    {
                        dispatched++;
                        var click = new Point(button.Left + button.Width / 2, button.Top + button.Height / 2);
                        Require(WindowMessages.ClickBackground(root.Handle, owner, click, FarmMouse.Left, 1, 0,
                            CancellationToken.None, () => true), "click did not reach the offline menu window");
                        Application.DoEvents();
                        // The window receives the first click but models a game
                        // that only responds after the verified retry.
                        if (retry) destinationAt = now + 120;
                        return true;
                    }, 240, 0);
                Require(arrived && dispatched == 2 &&
                    child.Messages.Count(message => message.Msg == 0x0201) == 2 &&
                    child.Messages.Count(message => message.Msg == 0x0202) == 2 &&
                    !root.Messages.Any(), "the original click and one retry must target only the offline child window");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
