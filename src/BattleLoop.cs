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
        internal GameSource? DetectedMode;
        internal string Notice;
    }

    internal static class BattleLoop
    {
        internal static void Run(CombatSettings settings, bool preview, IProgress<BattleReport> progress,
            CancellationToken cancellation, ConcertoMeter concerto, bool assistOnly = false)
        {
            RunLog.Info("Battle", string.Format("开始识别和战斗循环｜来源={0} 仅检测={1}", settings.Source, preview));
            var window = new GameWindow(settings.Source);
            var input = new WindowInput(window);
            var vision = new HudVision(settings, concerto);
            bool cloudStream = settings.Source == GameSource.Cloud || settings.Source == GameSource.ManagedCloud;
            var planner = new BattlePlanner(settings, cloudStream, attackVisibleEnemies: settings.AutoTarget);
            var reviveInput = new FarmInput(window);
            var reviveVision = new FarmVision();
            long nextReviveClick = 0;
            long nextPopupClick = 0;
            var monthly = new MonthlyCardSchedule(settings.WorldAssist);
            MonthlyCardDismissal dismissingCard = null;
            long scheduledWaitEnd = 0;
            int browserRoute = 0;
            var browser = settings.Source == GameSource.ManagedCloud ? new CloudBrowser() : null;
            WorldAssist assist = !preview && settings.WorldAssist != null &&
                (settings.WorldAssist.NeedsVision || settings.WorldAssist.RestoreCursor)
                ? new WorldAssist(settings.WorldAssist, reviveVision, updates: new GameUpdateRecovery(window, cancellation, cloudStream,
                    message => progress.Report(new BattleReport { State = message, Notice = message }))) : null;
            Func<bool> release = browser == null ? (Func<bool>)input.ReleaseAll : browser.ReleaseAll;
            Func<BattleAction, CancellationToken, bool> send = browser == null ?
                (Func<BattleAction, CancellationToken, bool>)input.Send : (action, token) =>
                {
                    bool sent = browser.Send(action, token);
                    if (sent)
                        RunLog.Info("Input", "云游戏浏览器｜" + action.Label + "｜" + action.Kind);
                    return sent;
                };
            var timer = Stopwatch.StartNew();
            string lastAction = "—";
            string lastObservation = null;
            long lastReport = -500;
            IntPtr lastWindow = IntPtr.Zero;
            uint lastOwner = 0;
            GameSource lastMode = window.Mode;
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
                    bool nativePixels = assist != null && !planner.Fighting && assist.NeedsNativeFrame(started);
                    if (browser != null)
                    {
                        state = "等待已打开的官网云游戏窗口";
                        PixelFrame frame;
                        Size size;
                        if (browser.TryRead(cancellation, out frame, out size, nativePixels))
                        {
                            if (browserRoute != browser.RouteVersion)
                            {
                                dismissingCard = null;
                                scheduledWaitEnd = 0;
                                if (assist != null) assist.ResetWindow();
                                browserRoute = browser.RouteVersion;
                            }
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
                        if (window.Handle != lastWindow || window.ProcessId != lastOwner || window.Mode != lastMode)
                        {
                            dismissingCard = null;
                            scheduledWaitEnd = 0;
                        }
                        if (window.Handle != lastWindow || window.ProcessId != lastOwner)
                        {
                            release();
                            planner = new BattlePlanner(settings, cloudStream, attackVisibleEnemies: settings.AutoTarget);
                            vision = new HudVision(settings, concerto);
                            if (assist != null) assist.ResetWindow();
                            lastWindow = window.Handle;
                            lastOwner = window.ProcessId;
                        }
                        else if (window.Mode != lastMode && release())
                        {
                            planner.HoldsCanceled(started);
                        }
                        lastMode = window.Mode;
                        try
                        {
                            PixelFrame frame;
                            if (!capture.Read(bounds, out frame, nativePixels))
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
                            else
                                state = "等待最新游戏画面";
                        }
                        catch (Win32Exception)
                        {
                            state = "游戏截图暂不可用";
                        }
                    }

                    long now = timer.ElapsedMilliseconds;
                    if (assist != null && browser == null && !bounds.IsEmpty)
                        assist.UpdateCursor(window, bounds, now);
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
                        var frameSize = new Size(currentFrame.Width, currentFrame.Height);
                        Func<Point, bool> click = point => browser == null
                            ? reviveInput.Click(point, frameSize, bounds, FarmMouse.Left, 60, cancellation)
                            : browser.Click(point, frameSize, cancellation);
                        string frameAction = null;
                        if (dismissingCard == null && !assistOnly && settings.AutoRevive && hud.Dead && haveFrame && now >= nextReviveClick)
                        {
                            nextReviveClick = now + 2600;
                            if (TryRevive(currentFrame, reviveVision, click, allowFallback: browser == null))
                                frameAction = "尝试自动复活";
                        }
                        bool canWait = !planner.Fighting && hud.Valid && !hud.Locked && !hud.EnemyHealth && !hud.Dead;
                        if (dismissingCard == null && canWait && scheduledWaitEnd == 0 && monthly.Due(DateTime.Now) &&
                            reviveVision.HasTeamNumbers(currentFrame))
                            scheduledWaitEnd = now + 120000;
                        if (scheduledWaitEnd != 0 && now >= scheduledWaitEnd)
                            scheduledWaitEnd = 0;
                        if (frameAction == null && haveFrame)
                        {
                            try
                            {
                                if (dismissingCard == null && (!hud.Valid || scheduledWaitEnd != 0) && now >= nextPopupClick)
                                {
                                    nextPopupClick = now + 500;
                                    Rectangle card;
                                    if (reviveVision.FindMonthlyCard(currentFrame, out card))
                                        dismissingCard = new MonthlyCardDismissal(() => timer.ElapsedMilliseconds);
                                }
                                if (dismissingCard != null)
                                {
                                    Point button = MonthlyCardDismissal.Button(currentFrame);
                                    MonthlyCardResult result = dismissingCard.Observe(reviveVision.HasTeamNumbers(currentFrame),
                                        () => browser == null ? reviveInput.Click(button, frameSize, bounds, FarmMouse.Left, 20, cancellation) :
                                            browser.Click(button, frameSize, cancellation, 20));
                                    if (result != MonthlyCardResult.Waiting)
                                    {
                                        if (result != MonthlyCardResult.InputLost) monthly.Handled(DateTime.Now);
                                        dismissingCard = null;
                                        scheduledWaitEnd = 0;
                                    }
                                    frameAction = result == MonthlyCardResult.ReturnedToTeam ? "领取月卡后已返回队伍" :
                                        result == MonthlyCardResult.TimedOut ? "关闭月卡后等待队伍画面超时" :
                                        result == MonthlyCardResult.InputLost ? "月卡输入中断" : "正在关闭月卡弹窗";
                                }
                            }
                            catch (Win32Exception error)
                            {
                                dismissingCard = null;
                                RunLog.Error("Battle", "关闭月卡弹窗时输入失败", error);
                            }
                        }
                        bool waitForCard = scheduledWaitEnd != 0;
                        if (!waitForCard && frameAction == null && assist != null && haveFrame)
                        {
                            try
                            {
                                frameAction = assist.Observe(currentFrame, hud, planner.Fighting, now, click,
                                    () => {
                                        bool pressed = browser == null ? reviveInput.Key('F', 70, cancellation)
                                            : browser.Send(new BattleAction { Kind = ActionKind.Cast, Key = 'F', DownMilliseconds = 70 }, cancellation);
                                        if (pressed) planner.CancelChallengePrompt();
                                        return pressed;
                                    },
                                    cloud: cloudStream, capturedAt: started);
                            }
                            catch (Win32Exception error) { RunLog.Error("战斗辅助", "辅助操作输入失败", error); }
                        }
                        if (frameAction != null)
                        {
                            if (!hud.Valid && release())
                                planner.HoldsCanceled(now);
                            if (dismissingCard == null || frameAction != lastAction) RunLog.Info("Battle", frameAction);
                            state = lastAction = frameAction;
                        }
                        else if (waitForCard)
                            state = "等待月卡弹窗";
                        else if (assistOnly)
                            state = "后台辅助运行中";
                        else
                        {
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
                    }

                    if (now - lastReport >= 175)
                    {
                        string observation = DescribeHud(state, hud);
                        if (observation != lastObservation)
                        {
                            RunLog.Info("Battle", observation);
                            lastObservation = observation;
                        }
                        progress.Report(new BattleReport
                        {
                            State = state, LastAction = lastAction, Hud = hud, Size = bounds.Size,
                            DetectedMode = settings.Source == GameSource.AutoLocal && !bounds.IsEmpty ?
                                (GameSource?)window.Mode : null
                        });
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
                try
                {
                    if (browser != null) browser.Dispose();
                    else input.ReleaseAll();
                }
                finally
                {
                    planner.Invalidate(timer.ElapsedMilliseconds);
                    if (assist != null) assist.Dispose();
                    concerto.Save();
                    RunLog.Info("Battle", "识别和战斗循环结束｜取消=" + cancellation.IsCancellationRequested);
                }
            }
        }

        private static string DescribeHud(string state, HudSnapshot hud)
        {
            if (hud == null)
                return state + "｜无可用识别结果";
            string fighter = hud.Current >= 0 && hud.Current < hud.Fighters.Length ? hud.Fighters[hud.Current] : null;
            return string.Format("{0}｜{1}｜HUD={2} 槽位={3} 角色={4} 锁敌={5} 敌人血条={6} 倒下={7} 技能={8}/{9}/{10}",
                state, hud.Detail, hud.Valid, hud.Current + 1, fighter ?? "未识别", hud.Locked, hud.EnemyHealth, hud.Dead,
                hud.Skills[0], hud.Skills[1], hud.Skills[2]);
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
            private string lastPath;
            private int? lastCaptureError;

            internal WindowFrames(GameWindow window)
            {
                this.window = window;
                desktop = new GameCapture(window.Mode == GameSource.Cloud);
            }

            internal bool Read(Rectangle bounds, out PixelFrame frame, bool retainEcho = false)
            {
                try
                {
                    bool captured = ReadCore(bounds, out frame, retainEcho);
                    if (captured && lastCaptureError.HasValue)
                    {
                        RunLog.Info("Capture", "已恢复读取游戏画面");
                        lastCaptureError = null;
                    }
                    return captured;
                }
                catch (Win32Exception error)
                {
                    if (lastCaptureError != error.NativeErrorCode)
                    {
                        RunLog.Error("Capture", "读取游戏画面失败｜系统错误=" + error.NativeErrorCode, error);
                        lastCaptureError = error.NativeErrorCode;
                    }
                    throw;
                }
            }

            private bool ReadCore(Rectangle bounds, out PixelFrame frame, bool retainEcho)
            {
                if (window.Mode != GameSource.Background)
                {
                    if (capturedWindow != IntPtr.Zero)
                    {
                        if (graphics != null) graphics.Dispose();
                        graphics = null;
                        capturedWindow = IntPtr.Zero;
                    }
                    if (!window.Available())
                    {
                        frame = default(PixelFrame);
                        return false;
                    }
                    frame = desktop.Read(bounds, retainEcho);
                    ReportPath("屏幕截图", bounds.Size);
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
                    catch (Exception error)
                    {
                        if (!IsCaptureUnavailable(error)) throw;
                        RunLog.Error("Capture", "无法初始化 Windows 图形捕获，改用 PrintWindow", error);
                    }
                }
                if (graphics != null)
                {
                    try
                    {
                        if (graphics.TryRead(window.Handle, bounds, out frame, retainEcho))
                        {
                            emptyFrames = 0;
                            ReportPath("Windows 图形捕获", bounds.Size);
                            return true;
                        }
                        if (++emptyFrames <= 3) return false;
                    }
                    catch (Exception error)
                    {
                        if (!IsCaptureUnavailable(error)) throw;
                        RunLog.Error("Capture", "Windows 图形捕获中断，改用 PrintWindow", error);
                        graphics.Dispose();
                        graphics = null;
                    }
                }
                bool captured = fallback.TryRead(window.Handle, bounds.Size, out frame, retainEcho);
                ReportPath(captured ? "PrintWindow" : "PrintWindow 未返回可用画面", bounds.Size);
                return captured;
            }

            private void ReportPath(string path, Size size)
            {
                if (path == lastPath) return;
                lastPath = path;
                RunLog.Info("Capture", string.Format("{0}｜PID={1} 尺寸={2}×{3}", path, window.ProcessId, size.Width, size.Height));
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

        private static bool TryRevive(PixelFrame frame, FarmVision vision, Func<Point, bool> click, bool allowFallback)
        {
            Rectangle box;
            Point point;
            if (vision.FindLabel(frame, "revive_confirm_hcenter_vcenter", .90, out box))
                point = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            else if (allowFallback)
                point = new Point((int)(frame.Width * .78), (int)(frame.Height * .72));
            else
                return false;
            try
            {
                return click(point);
            }
            catch (Win32Exception error)
            {
                RunLog.Error("Battle", "自动复活输入失败", error);
                return false;
            }
        }

        internal static byte[] EnrollPortrait(int slot, GameSource source, CancellationToken cancellation)
        {
            var window = GameWindow.ForTask(source);
            var watch = Stopwatch.StartNew();
            byte[] first = null;
            IntPtr previousHandle = IntPtr.Zero;
            RunLog.Info("Portrait", "开始捕获头像｜来源=" + source + " 槽位=" + (slot + 1));
            using (var browser = source == GameSource.ManagedCloud ? new CloudBrowser() : null)
            using (var capture = browser == null ? new WindowFrames(window) : null)
            {
                while (watch.ElapsedMilliseconds < 15000)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Rectangle bounds;
                    string reason;
                    Size size;
                    PixelFrame frame = default(PixelFrame);
                    bool captured = browser != null ? browser.TryRead(cancellation, out frame, out size) :
                        window.TryLocate(out bounds, out reason) && capture.Read(bounds, out frame) && window.Available();
                    if (previousHandle != window.Handle)
                    {
                        previousHandle = window.Handle;
                        first = null;
                    }
                    if (captured)
                    {
                        var hud = new HudSnapshot();
                        if (PartyHud.Read(frame, hud) && hud.Health[slot] > 0.08)
                        {
                            byte[] sample = PortraitReader.Sample(frame, slot);
                            if (first != null && PortraitReader.Distance(first, sample) <= 5.0)
                            {
                                RunLog.Info("Portrait", "已确认稳定头像｜槽位=" + (slot + 1));
                                return sample;
                            }
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
            RunLog.Warn("Portrait", "15 秒内未捕获可用头像｜来源=" + source + " 槽位=" + (slot + 1));
            throw new TimeoutException("15 秒内未捕获可用头像。确认游戏窗口已打开、所选槽位有存活角色。");
        }
    }
}
