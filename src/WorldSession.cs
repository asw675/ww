using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace TideCombat
{
    // Synchronous world helpers shared by the task ports: capture, vision, OCR,
    // clicks, keys and waits. Each helper ticks the frame when needed.
    internal sealed class WorldSession : IDisposable
    {
        private const int BackgroundWarmupMs = 1000;
        private const int BackgroundFrameWaitMs = 250;
        internal readonly CombatSettings Settings;
        internal readonly CancellationToken Token;
        internal readonly GameWindow Window;
        internal readonly FarmInput Input;
        internal readonly WindowInput BattleInput;
        internal readonly FarmVision Vision;
        internal readonly OcrReader Ocr;
        internal readonly ChallengeStartObservation ChallengeObservation = new ChallengeStartObservation();
        internal PixelFrame Frame;
        internal Rectangle Bounds;
        internal HudSnapshot Hud;
        internal FarmUi Ui;
        internal bool Captured;
        internal bool AllowShortEnemyBars { get; set; }
        internal bool RetainEchoFrames { get; set; }
        internal int InputRouteVersion { get; private set; }

        private readonly BattleLoop.WindowFrames capture;
        private readonly bool ownsCapture, ownsOcr;
        private readonly HudVision hudVision;
        private readonly MonthlyCardSchedule monthly;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private IntPtr routeWindow;
        private uint routeProcess;
        private GameSource routeMode;
        private long lastTick;
        private bool detailedFrame;
        private bool? terminal;
        private bool captureError, hudError, uiError, ocrError, labelError;

        internal WorldSession(CombatSettings settings, CancellationToken token, OcrReader ocr = null,
            GameWindow window = null, BattleLoop.WindowFrames frames = null, MonthlyCardSchedule cardSchedule = null)
        {
            Settings = settings;
            monthly = cardSchedule ?? new MonthlyCardSchedule(settings.WorldAssist);
            Token = token;
            Window = window ?? GameWindow.ForTask(settings.Source);
            Input = new FarmInput(Window);
            BattleInput = new WindowInput(Window);
            Vision = new FarmVision();
            ownsCapture = frames == null;
            ownsOcr = ocr == null;
            capture = frames ?? new BattleLoop.WindowFrames(Window);
            hudVision = new HudVision(settings);
            Ocr = ocr ?? new OcrReader(OcrReader.Directory);
        }

        internal bool Tick(bool detailed = true, BattlePlanner planner = null, long combatTime = 0, bool nativePixels = false)
        {
            if (Token.IsCancellationRequested)
                return false;
            Rectangle bounds;
            string reason;
            if (!Window.TryLocate(out bounds, out reason))
            {
                ResetFrame();
                ClearInputRoute();
                return false;
            }
            if (Window.InputBlockedByIntegrity)
                throw new InputElevationRequiredException();
            bool routeChanged = UpdateInputRoute();
            bool retainSource = RetainEchoFrames || nativePixels;
            if (!routeChanged && Captured && (!retainSource || Frame.HasSourcePixels) && bounds == Bounds && clock.ElapsedMilliseconds - lastTick < 40 &&
                Window.Available())
            {
                if (detailed && !detailedFrame)
                    ReadUi(true);
                return true;
            }
            ResetFrame();
            PixelFrame frame;
            try
            {
                if (!ReadFrame(ref bounds, routeChanged, retainSource, out frame))
                    return false;
                captureError = false;
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                LogReadFailure(ref captureError, "读取任务画面", error);
                return false;
            }
            Frame = frame;
            Bounds = bounds;
            bool cloud = Settings.Source == GameSource.Cloud || Settings.Source == GameSource.ManagedCloud;
            try
            {
                Hud = hudVision.Read(frame, AllowShortEnemyBars || planner != null && planner.Fighting, cloud,
                    planner == null ? -1 : planner.LucillaTransformSlot,
                    planner != null && planner.ShouldScanExecution(combatTime));
                hudError = false;
            }
            catch (Exception error)
            {
                Hud = null;
                LogReadFailure(ref hudError, "识别任务 HUD", error);
            }
            ReadUi(detailed);
            lastTick = clock.ElapsedMilliseconds;
            Captured = true;
            if (planner == null) ChallengeObservation.Observe(Hud, Now, consume: false);
            return true;
        }

        private bool ReadFrame(ref Rectangle bounds, bool routeChanged, bool retainSource, out PixelFrame frame)
        {
            long deadline = clock.ElapsedMilliseconds + (routeChanged ? BackgroundWarmupMs : BackgroundFrameWaitMs);
            do
            {
                if (Window.Available() && capture.Read(bounds, out frame, retainSource) && Window.Available())
                    return true;
                if ((Window.Mode != GameSource.Background && Settings.Source != GameSource.AutoLocal) ||
                    clock.ElapsedMilliseconds >= deadline || Token.WaitHandle.WaitOne(25))
                    break;
                string reason;
                if (!Window.TryLocate(out bounds, out reason))
                {
                    ClearInputRoute();
                    break;
                }
                if (Window.InputBlockedByIntegrity)
                    throw new InputElevationRequiredException();
                UpdateInputRoute();
            }
            while (!Token.IsCancellationRequested);
            frame = default(PixelFrame);
            return false;
        }

        private bool UpdateInputRoute()
        {
            if (routeWindow == Window.Handle && routeProcess == Window.ProcessId && routeMode == Window.Mode)
                return false;
            if (routeWindow != IntPtr.Zero)
                ReleaseHeldInput();
            routeWindow = Window.Handle;
            routeProcess = Window.ProcessId;
            routeMode = Window.Mode;
            ChallengeObservation.Clear();
            InputRouteVersion++;
            return true;
        }

        private void ClearInputRoute()
        {
            if (routeWindow == IntPtr.Zero)
                return;
            ReleaseHeldInput();
            routeWindow = IntPtr.Zero;
            routeProcess = 0;
            ChallengeObservation.Clear();
            InputRouteVersion++;
        }

        private void ResetFrame()
        {
            Captured = false;
            Frame = default(PixelFrame);
            Bounds = Rectangle.Empty;
            Hud = null;
            Ui = null;
            detailedFrame = false;
            terminal = null;
        }

        private void ReadUi(bool detailed)
        {
            try
            {
                Ui = Vision.Read(Frame, detailed);
                detailedFrame = detailed;
                uiError = false;
            }
            catch (Exception error)
            {
                Ui = null;
                LogReadFailure(ref uiError, "识别任务界面", error);
            }
        }

        private bool dismissingMonthlyCard;
        private long ElapsedMilliseconds { get { return clock.ElapsedMilliseconds; } }
        internal long Now { get { return ElapsedMilliseconds; } }

        internal bool WaitStable(Func<bool> condition, int timeoutMs, int stableMs)
        {
            long since = -1;
            return WaitUntil(() =>
            {
                if (!condition()) { since = -1; return false; }
                long now = ElapsedMilliseconds;
                if (since < 0) since = now;
                return now - since > stableMs;
            }, timeoutMs);
        }

        internal void Sleep(int milliseconds)
        {
            long started = clock.ElapsedMilliseconds;
            CheckScheduledCard();
            int remaining = (int)Math.Max(0, milliseconds - (clock.ElapsedMilliseconds - started));
            Token.WaitHandle.WaitOne(remaining);
            Tick();
        }

        internal bool DismissMonthlyCard(bool requireTeam = false)
        {
            if (!Tick()) return false;
            if (Ui == null || !Ui.MonthlyCard) return Vision.HasTeamNumbers(Frame);
            int route = InputRouteVersion;
            var dismiss = new MonthlyCardDismissal(() => ElapsedMilliseconds);
            dismissingMonthlyCard = true;
            try
            {
                while (Tick() && InputRouteVersion == route)
                {
                    MonthlyCardResult result = dismiss.Observe(Vision.HasTeamNumbers(Frame), () =>
                        ClickOnRoute(MonthlyCardDismissal.Button(Frame), route, 20));
                    if (result == MonthlyCardResult.InputLost) return false;
                    if (result != MonthlyCardResult.Waiting)
                    {
                        monthly.Handled(DateTime.Now);
                        return result == MonthlyCardResult.ReturnedToTeam || !requireTeam;
                    }
                    Sleep(dismiss.Delay);
                }
                return false;
            }
            finally { dismissingMonthlyCard = false; }
        }

        internal void CheckScheduledCard()
        {
            if (dismissingMonthlyCard || !monthly.Due(DateTime.Now) || !Tick() || Hud == null ||
                Hud.Locked || Hud.EnemyHealth || Hud.Dead || !Vision.HasTeamNumbers(Frame)) return;
            int route = InputRouteVersion;
            long end = clock.ElapsedMilliseconds + 120000;
            while (clock.ElapsedMilliseconds < end && Tick() && InputRouteVersion == route)
            {
                if (Ui != null && Ui.MonthlyCard)
                {
                    DismissMonthlyCard();
                    return;
                }
                if (Token.WaitHandle.WaitOne(250)) return;
            }
        }

        internal bool ClickCentered(double x, double y)
        {
            if (!Tick()) return false;
            double contentWidth = Math.Min(Frame.Width, Frame.Height * (16.0 / 9));
            return Click(.5 + (x - .5) * contentWidth / Frame.Width, y);
        }

        internal bool TypeText(string text)
        {
            return Tick() && Input.TypeText(text, Token);
        }

        internal bool Click(double x, double y, int hold = 60)
        {
            if (!Tick())
                return false;
            var size = new Size(Frame.Width, Frame.Height);
            return Input.Click(new Point((int)(x * Frame.Width), (int)(y * Frame.Height)), size, Bounds,
                FarmMouse.Left, hold, Token);
        }

        internal bool Click(Point point, int hold = 60)
        {
            if (!Tick())
                return false;
            return Input.Click(point, new Size(Frame.Width, Frame.Height), Bounds, FarmMouse.Left, hold, Token);
        }

        internal void WaitForOptionalDialog(int? expectedRoute = null)
        {
            Rectangle check = Rectangle.Empty, button = Rectangle.Empty;
            if (!WaitUntil(() => (expectedRoute.HasValue && expectedRoute.Value != InputRouteVersion) ||
                Vision.FindOptionalCheckbox(Frame, out check) && Vision.FindOptionalButton(Frame, .78, out button), 3000) ||
                expectedRoute.HasValue && expectedRoute.Value != InputRouteVersion)
                return;
            int route = InputRouteVersion;
            Rectangle original = button;
            Sleep(500);
            if (Token.IsCancellationRequested || !Captured || !Vision.FindOptionalButton(Frame, .78, out button) ||
                !original.IntersectsWith(button) || !Vision.FindOptionalCheckbox(Frame, out check) || !ClickOnRoute(check, route)) return;
            Sleep(500);
            if (!Token.IsCancellationRequested && Captured && Vision.FindOptionalButton(Frame, .78, out button) &&
                original.IntersectsWith(button) && ClickOnRoute(button, route)) Sleep(200);
        }

        internal bool ClickOnRoute(Rectangle control, int route, int hold = 60)
        {
            return !control.IsEmpty && ClickOnRoute(new Point(control.Left + control.Width / 2,
                control.Top + control.Height / 2), route, hold);
        }

        internal bool ClickOnRoute(Point point, int route, int hold = 60)
        {
            return Tick() && InputRouteVersion == route && Input.Click(point, new Size(Frame.Width, Frame.Height),
                Bounds, FarmMouse.Left, hold, Token);
        }

        internal bool ClickMenuButton(Point point, bool retry = false, bool requireForeground = false)
        {
            return Tick() && (!requireForeground || Window.Mode == GameSource.Foreground) &&
                Input.Click(point, new Size(Frame.Width, Frame.Height), Bounds,
                    FarmMouse.Left, 100, Token, settleMs: 150, useWindowMessage: !retry);
        }

        internal bool ClickMenuButton(Rectangle box)
        {
            return !box.IsEmpty && ClickMenuButton(new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
        }

        internal bool ClickMenuButton(double x, double y)
        {
            return Tick() && ClickMenuButton(new Point((int)(x * Frame.Width), (int)(y * Frame.Height)));
        }

        internal bool Click(Rectangle box, int hold = 60)
        {
            if (box.IsEmpty || !Tick())
                return false;
            return Input.Click(new Point(box.X + box.Width / 2, box.Y + box.Height / 2),
                new Size(Frame.Width, Frame.Height), Bounds, FarmMouse.Left, hold, Token);
        }

        internal bool ClickWithAlt(double x, double y, int settleMs)
        {
            const char alt = '\u0012';
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!KeyDown(alt))
                    return false;
                int route = InputRouteVersion;
                try
                {
                    if (Token.WaitHandle.WaitOne(settleMs) || !Tick())
                        return false;
                    // Reapply Alt on the new route before sending the click.
                    if (route == InputRouteVersion)
                        return Input.ClickRelative(x, y, new Size(Frame.Width, Frame.Height), Bounds,
                            FarmMouse.Left, 60, Token);
                }
                finally { KeyUp(alt); }
            }
            return false;
        }

        internal bool Key(char key, int hold = 60, bool requireForeground = false, int? route = null)
        {
            if (!Tick() || requireForeground && Window.Mode != GameSource.Foreground ||
                route.HasValue && route.Value != InputRouteVersion)
                return false;
            return Input.Key(key, hold, Token);
        }

        internal bool Interact(bool retry)
        {
            return Tick() && Input.KeyCode('F', 90, Token, useWindowMessage: retry);
        }

        internal bool ScrollInteraction()
        {
            return Tick() && Input.Scroll(1, new Point(Frame.Width / 2, Frame.Height / 2),
                new Size(Frame.Width, Frame.Height), Bounds, Token);
        }

        internal bool CenterEchoCamera(int route, int hold = 200, int settle = 1000)
        {
            if (!Tick() || route != InputRouteVersion) return false;
            if (!Input.ClickRelative(.5, .5, new Size(Frame.Width, Frame.Height), Bounds, FarmMouse.Middle, hold, Token))
                return false;
            Sleep(settle);
            return !Token.IsCancellationRequested && Captured && route == InputRouteVersion;
        }

        internal bool KeyDown(char key, int? route = null)
        {
            return !Token.IsCancellationRequested && Tick() && (!route.HasValue || route.Value == InputRouteVersion) &&
                Input.KeyDown(key);
        }

        internal bool KeyUp(char key)
        {
            return Input.KeyUp(key);
        }

        internal bool GuideKey(bool requireForeground = false)
        {
            return Tick() && (!requireForeground || Window.Mode == GameSource.Foreground) &&
                Input.KeyCode(Settings.GuidebookVirtualKey(), 60, Token);
        }

        internal bool TryRecoverForegroundCapture()
        {
            if (Token.IsCancellationRequested)
                return false;
            IntPtr handle = Window.Handle;
            uint process = Window.ProcessId;
            if (!Window.TryActivateForAutoTask())
                return false;
            bool captured = WaitUntil(() => Window.Handle == handle && Window.ProcessId == process &&
                Window.Mode == GameSource.Foreground && Window.StillForeground(), 1500);
            RunLog.Info("任务识别", "前台重试画面已确认=" + captured);
            return captured;
        }

        internal bool Send(BattleAction action)
        {
            if (!Tick())
                return false;
            return BattleInput.Send(action, Token);
        }

        internal List<OcrLine> Read(double left, double top, double right, double bottom, bool centered = false)
        {
            if (!Tick(nativePixels: true))
                return new List<OcrLine>();
            var area = new Rectangle((int)(Frame.Width * left), (int)(Frame.Height * top),
                Math.Max(1, (int)(Frame.Width * (right - left))),
                Math.Max(1, (int)(Frame.Height * (bottom - top))));
            try
            {
                List<OcrLine> result = Ocr.ReadRelative(Frame, left, top, right, bottom, centered);
                ocrError = false;
                return result;
            }
            catch (Exception error)
            {
                LogReadFailure(ref ocrError, "识别文字，区域=" + area, error);
                return new List<OcrLine>();
            }
        }

        internal bool FindNear(string label, double threshold, out Rectangle box)
        {
            box = Rectangle.Empty;
            if (!Tick())
                return false;
            return Vision.FindLabel(Frame, label, threshold, out box);
        }

        internal bool FindIn(string label, double left, double top, double right, double bottom, int step,
            double threshold, double light, out Rectangle box)
        {
            box = Rectangle.Empty;
            if (!Tick())
                return false;
            var area = new Rectangle((int)(Frame.Width * left), (int)(Frame.Height * top),
                Math.Max(1, (int)(Frame.Width * (right - left))),
                Math.Max(1, (int)(Frame.Height * (bottom - top))));
            return Vision.FindFirst(Frame, label, area, step, threshold, light, out box);
        }

        internal List<Rectangle> FindAll(string label, double left, double top, double right, double bottom,
            int step, double threshold, double light)
        {
            if (!Tick())
                return new List<Rectangle>();
            var area = new Rectangle((int)(Frame.Width * left), (int)(Frame.Height * top),
                Math.Max(1, (int)(Frame.Width * (right - left))),
                Math.Max(1, (int)(Frame.Height * (bottom - top))));
            try
            {
                List<Rectangle> result = Vision.FindAll(Frame, label, area, step, threshold, light);
                labelError = false;
                return result;
            }
            catch (Exception error)
            {
                LogReadFailure(ref labelError, "匹配界面标记 " + label, error);
                return new List<Rectangle>();
            }
        }

        private static void LogReadFailure(ref bool reported, string operation, Exception error)
        {
            if (reported)
                return;
            reported = true;
            RunLog.Error("任务识别", operation + "失败", error);
        }

        internal bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (Token.IsCancellationRequested)
                    return false;
                if (Tick() && condition())
                    return true;
                Token.WaitHandle.WaitOne(60);
            }
            return false;
        }

        internal bool WaitFind(string label, int timeoutMs, double threshold = .86)
        {
            bool found = WaitUntil(delegate
            {
                Rectangle box;
                return FindNear(label, threshold, out box);
            }, timeoutMs);
            if (!Token.IsCancellationRequested)
                RunLog.Info("任务识别", found ? "已识别标记 " + label : "等待标记 " + label + " 超时，限时 " + timeoutMs + " ms");
            return found;
        }

        internal bool WaitClick(string label, int timeoutMs, double threshold = .86)
        {
            var watch = Stopwatch.StartNew();
            while (!Token.IsCancellationRequested && watch.ElapsedMilliseconds < timeoutMs)
            {
                Rectangle box;
                if (FindNear(label, threshold, out box))
                {
                    Sleep(150);
                    bool clicked = FindNear(label, threshold, out box) && Click(box);
                    RunLog.Info("任务识别", "标记 " + label + " 已识别，点击" + (clicked ? "已发送" : "未发送"));
                    if (clicked)
                        return true;
                }
                Token.WaitHandle.WaitOne(80);
            }
            if (!Token.IsCancellationRequested)
                RunLog.Info("任务识别", "等待可点击标记 " + label + " 超时，限时 " + timeoutMs + " ms");
            return false;
        }

        internal bool InRealm { get { return Captured && Ui != null && Ui.InRealm; } }
        internal bool TeamHud { get { return Captured && Hud != null && Hud.Valid; } }

        internal bool Terminal
        {
            get
            {
                if (!Captured) return false;
                if (!terminal.HasValue) terminal = Vision.IsTerminal(Frame);
                return terminal.Value;
            }
        }

        internal bool VerifiedWorld
        {
            get
            {
                if (!Captured || !detailedFrame || Ui == null || Ui.Book || Ui.BookMonsters || Ui.Teleport ||
                    Ui.Challenge || Ui.Claim || Ui.Death || Ui.MonthlyCard || Ui.SkipDialog ||
                    !Ui.ConfirmBox.IsEmpty)
                    return false;

                // Navigation can resume before the skill bar is fully visible.
                return (TeamHud || ((Ui.OpenWorld || Ui.InRealm) && PartyHud.Read(Frame, new HudSnapshot()))) && !Terminal;
            }
        }

        private void ReleaseHeldInput()
        {
            try { BattleInput.ReleaseAll(); }
            catch (Exception error) { RunLog.Error("任务输入", "释放战斗按键失败", error); }
            try { Input.ReleaseAll(); }
            catch (Exception error) { RunLog.Error("任务输入", "释放任务按键失败", error); }
        }

        public void Dispose()
        {
            ReleaseHeldInput();
            if (ownsCapture) capture.Dispose();
            if (ownsOcr) Ocr.Dispose();
        }
    }
}
