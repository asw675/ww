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

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
