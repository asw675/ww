using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace TideCombat
{
    internal sealed class BattleReport
    {
        internal string State;
        internal string LastAction;
        internal HudSnapshot Hud;
        internal Size Size;
    }

    internal static class BattleLoop
    {
        internal static void Run(CombatSettings settings, bool preview, IProgress<BattleReport> progress,
            CancellationToken cancellation, ConcertoMeter concerto)
        {
            var window = new GameWindow(settings.Source);
            var input = new WindowInput(window);
            var vision = new HudVision(settings, concerto);
            bool cloudStream = settings.Source == GameSource.Cloud || settings.Source == GameSource.ManagedCloud;
            var planner = new BattlePlanner(settings, cloudStream);
            var reviveInput = new FarmInput(window);
            var reviveVision = new FarmVision();
            long nextReviveClick = 0;
            long nextPopupClick = 0;
            var browser = settings.Source == GameSource.ManagedCloud ? new CloudBrowser() : null;
            Func<bool> release = browser == null ? (Func<bool>)input.ReleaseAll : browser.ReleaseAll;
            Func<BattleAction, CancellationToken, bool> send = browser == null ?
                (Func<BattleAction, CancellationToken, bool>)input.Send : browser.Send;
            var timer = Stopwatch.StartNew();
            string lastAction = "—";
            long lastReport = -500;
            IntPtr lastWindow = IntPtr.Zero;
            uint lastOwner = 0;
            try
            {
              using (var capture = new WindowFrames(window))
              {
                while (!cancellation.IsCancellationRequested)
                {
                    long started = timer.ElapsedMilliseconds;
                    Rectangle bounds = Rectangle.Empty;
                    string state;
                    HudSnapshot hud = null;
                    bool waitingFrame = false;
                    PixelFrame currentFrame = default(PixelFrame);
                    bool haveFrame = false;
                    if (browser != null)
                    {
                        state = "等待已打开的官网云游戏窗口";
                        PixelFrame frame;
                        Size size;
                        if (browser.TryRead(cancellation, out frame, out size))
                        {
                            bounds = new Rectangle(Point.Empty, size);
                            currentFrame = frame;
                            haveFrame = true;
                            hud = vision.Read(frame, planner.Fighting, true, planner.LucillaTransformSlot,
                                planner.ShouldScanExecution(started));
                        }
                    }
                    else if (window.TryLocate(out bounds, out state))
                    {
                        if (!preview && window.InputBlockedByIntegrity)
                            throw new InputElevationRequiredException();
                        if (window.Handle != lastWindow || window.ProcessId != lastOwner)
                        {
                            release();
                            planner = new BattlePlanner(settings, cloudStream);
                            vision = new HudVision(settings, concerto);
                            lastWindow = window.Handle;
                            lastOwner = window.ProcessId;
                        }
                        try
                        {
                            PixelFrame frame;
                            if (!capture.Read(bounds, out frame))
                            {
                                state = "等待新的游戏画面";
                                waitingFrame = true;
                            }
                            else if (window.Available())
                            {
                                currentFrame = frame;
                                haveFrame = true;
                                hud = vision.Read(frame, planner.Fighting, cloudStream, planner.LucillaTransformSlot,
                                    planner.ShouldScanExecution(started));
                            }
                        }
                        catch (Win32Exception)
                        {
                            state = "游戏截图暂不可用";
                        }
                    }

                    long now = timer.ElapsedMilliseconds;
                    if (hud == null)
                    {
                        if (waitingFrame && window.Available())
                        {
                            if (!preview) planner.Observe(new HudSnapshot { Detail = state }, now);
                            if (release()) planner.HoldsCanceled(now);
                        }
                        else
                        {
                            planner.Invalidate(now);
                            release();
                        }
                    }
                    else if (now - started > (browser == null ? 250 : 550) || browser == null && !window.Available())
                    {
                        planner.Invalidate(now);
                        release();
                        state = "等待最新游戏画面";
                    }
                    else if (preview)
                        state = hud.Valid ? "仅检测" : hud.Detail;
                    else
                    {
                        if (settings.AutoRevive && hud.Dead && browser == null && haveFrame && now >= nextReviveClick)
                        {
                            nextReviveClick = now + 2600;
                            TryRevive(currentFrame, hud, bounds, reviveInput, reviveVision, cancellation, ref state);
                        }
                        if (!hud.Valid && haveFrame && now >= nextPopupClick)
                        {
                            nextPopupClick = now + 1800;
                            Rectangle popup;
                            if (reviveVision.FindLabel(currentFrame, "monthly_card", .84, out popup))
                            {
                                try
                                {
                                    if (reviveInput.Click(new Point((int)(currentFrame.Width * .5),
                                        (int)(currentFrame.Height * .89)), new Size(currentFrame.Width, currentFrame.Height),
                                        bounds, FarmMouse.Left, 60, cancellation))
                                        state = "关闭月卡弹窗";
                                }
                                catch (System.ComponentModel.Win32Exception) { }
                            }
                        }
                        BattleAction action = planner.Observe(hud, now);
                        state = planner.Status;
                        if (planner.TakeReleaseRequest())
                            release();
                        if (!hud.Valid && !planner.KeepAnimationHold(hud, now) && release())
                            planner.HoldsCanceled(now);
                        if (action.AttackBefore)
                            send(BattleAction.Attack(), cancellation);
                        long sentAt = timer.ElapsedMilliseconds;
                        if (action.Kind != ActionKind.Wait && send(action, cancellation))
                        {
                            planner.Sent(action, sentAt);
                            lastAction = action.Label;
                            if (action.FollowWithAttack)
                                send(BattleAction.Attack(), cancellation);
                        }
                    }

                    if (now - lastReport >= 175)
                    {
                        progress.Report(new BattleReport { State = state, LastAction = lastAction, Hud = hud, Size = bounds.Size });
                        lastReport = now;
                    }
                    int interval = planner.Fighting && !preview ? 50 : 100;
                    int remaining = (int)Math.Max(8, interval - (timer.ElapsedMilliseconds - started));
                    cancellation.WaitHandle.WaitOne(remaining);
                }
              }
            }
            finally
            {
                if (browser != null) browser.Dispose();
                else input.ReleaseAll();
                concerto.Save();
            }
        }

        internal sealed class WindowFrames : IDisposable
        {
            private readonly GameWindow window;
            private readonly GameCapture desktop;
            private readonly WindowCapture fallback = new WindowCapture();
            private GraphicsCapture graphics;
            private IntPtr capturedWindow;
            private uint capturedProcess;
            private int emptyFrames;

            internal WindowFrames(GameWindow window)
            {
                this.window = window;
                desktop = new GameCapture(window.Mode == GameSource.Cloud);
            }

            internal bool Read(Rectangle bounds, out PixelFrame frame)
            {
                if (window.Mode != GameSource.Background)
                {
                    frame = desktop.Read(bounds);
                    return true;
                }
                if (capturedWindow != window.Handle || capturedProcess != window.ProcessId)
                {
                    if (graphics != null) graphics.Dispose();
                    graphics = null;
                    capturedWindow = window.Handle;
                    capturedProcess = window.ProcessId;
                    emptyFrames = 0;
                    try { graphics = new GraphicsCapture(window.Handle); }
                    catch (Exception error) { if (!IsCaptureUnavailable(error)) throw; }
                }
                if (graphics != null)
                {
                    try
                    {
                        if (graphics.TryRead(window.Handle, bounds, out frame))
                        {
                            emptyFrames = 0;
                            return true;
                        }
                        if (++emptyFrames <= 3) return false;
                    }
                    catch (Exception error)
                    {
                        if (!IsCaptureUnavailable(error)) throw;
                        graphics.Dispose();
                        graphics = null;
                    }
                }
                return fallback.TryRead(window.Handle, bounds.Size, out frame);
            }

            private static bool IsCaptureUnavailable(Exception error)
            {
                return error is TargetInvocationException || error is COMException || error is TypeLoadException ||
                    error is PlatformNotSupportedException || error is EntryPointNotFoundException;
            }

            public void Dispose()
            {
                if (graphics != null) graphics.Dispose();
                desktop.Dispose();
                fallback.Dispose();
            }
        }

        private static void TryRevive(PixelFrame frame, HudSnapshot hud, Rectangle bounds, FarmInput input,
            FarmVision vision, CancellationToken cancellation, ref string state)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || bounds.Width <= 0)
                return;
            var size = new Size(frame.Width, frame.Height);
            Rectangle box;
            Point point;
            if (vision.FindLabel(frame, "revive_confirm_hcenter_vcenter", .90, out box))
                point = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            else
                point = new Point((int)(frame.Width * .78), (int)(frame.Height * .72));
            try
            {
                if (input.Click(point, size, bounds, FarmMouse.Left, 80, cancellation))
                    state = "尝试自动复活";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                state = "等待手动恢复";
            }
        }

        internal static byte[] EnrollPortrait(int slot, CancellationToken cancellation)
        {
            var window = new GameWindow();
            var watch = Stopwatch.StartNew();
            byte[] first = null;
            using (var capture = new GameCapture())
            {
                while (watch.ElapsedMilliseconds < 15000)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Rectangle bounds;
                    string reason;
                    if (window.TryLocate(out bounds, out reason))
                    {
                        PixelFrame frame = capture.Read(bounds);
                        var hud = new HudSnapshot();
                        if (window.StillForeground() && PartyHud.Read(frame, hud) && hud.Health[slot] > 0.08)
                        {
                            byte[] sample = PortraitReader.Sample(frame, slot);
                            if (first != null && PortraitReader.Distance(first, sample) <= 5.0)
                                return sample;
                            first = sample;
                        }
                        else
                            first = null;
                    }
                    else
                        first = null;
                    cancellation.WaitHandle.WaitOne(150);
                }
            }
            throw new TimeoutException("15 秒内未捕获可用头像。回到前台游戏，确认所选槽位有存活角色。");
        }
    }
}
