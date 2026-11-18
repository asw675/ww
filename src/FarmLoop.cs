using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace TideCombat
{
    internal sealed class FarmReport
    {
        internal string State;
        internal string LastAction;
        internal int Kills;
        internal int Echoes;
        internal double Rate;
        internal Size Size;
        internal HudSnapshot Hud;
        internal bool Finished;
        internal string Failure;
    }

    internal static class FarmLoop
    {
        internal static void Run(CombatSettings settings, FarmOptions options, IProgress<FarmReport> progress,
            CancellationToken cancellation, ConcertoMeter concerto)
        {
            var window = new GameWindow(settings.Source);
            var input = new WindowInput(window);
            var farmInput = new FarmInput(window);
            var vision = new HudVision(settings, concerto);
            var farmVision = new FarmVision();
            settings.SupportOpener = options.SwitchHealer;
            bool cloudStream = settings.Source == GameSource.Cloud || settings.Source == GameSource.ManagedCloud;
            var farm = new Farm4C(settings, options, cloudStream);
            EchoDetector detector = null;
            try
            {
                string model = EchoModel.ModelPath;
                if (File.Exists(model)) detector = new EchoDetector(model);
            }
            catch (Exception)
            {
                detector = null;
            }
            long lastDetect = -1000;
            List<EchoBox> echoes = null;
            OcrReader ocr = null;
            try
            {
                string dir = OcrReader.Directory;
                if (File.Exists(Path.Combine(dir, "det.onnx"))) ocr = new OcrReader(dir);
            }
            catch (Exception) { ocr = null; }
            long lastOcr = -1000;
            List<OcrLine> levelLines = null;
            bool absorbText = false;
            string dialogText = null;
            string bossName = null;
            long lastBossOcr = -1000;
            long lastDialogOcr = -1000;
            BossMarkerFinder markerFinder = null;
            try
            {
                string markerPath = BossMarkerFinder.DataPath;
                if (File.Exists(markerPath)) markerFinder = new BossMarkerFinder(markerPath);
            }
            catch (Exception) { markerFinder = null; }
            long lastMarker = -1000;
            MinimapNavigator navigator = null;
            double navTurn = double.NaN;
            long lastNav = -1000;
            try
            {
                string arrow = MinimapNavigator.ArrowPath;
                if (File.Exists(arrow)) navigator = new MinimapNavigator(arrow);
            }
            catch (Exception) { navigator = null; }
            var timer = Stopwatch.StartNew();
            string lastAction = "—";
            long lastReport = -500;
            bool forward = false;
            long lostSince = 0;
            try
            {
                using (var capture = new BattleLoop.WindowFrames(window))
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        long started = timer.ElapsedMilliseconds;
                        Rectangle bounds = Rectangle.Empty;
                        string state = "等待《鸣潮》客户端";
                        FarmUi ui = null;
                        HudSnapshot hud = null;
                        PixelFrame frame = default(PixelFrame);
                        bool captured = false;
                        if (window.TryLocate(out bounds, out state))
                        {
                            if (window.InputBlockedByIntegrity)
                                throw new InputElevationRequiredException();
                            try
                            {
                                captured = capture.Read(bounds, out frame) && window.Available();
                            }
                            catch (System.ComponentModel.Win32Exception)
                            {
                                state = "游戏截图暂不可用";
                            }
                            if (captured)
                            {
                                try
                                {
                                    hud = vision.Read(frame, farm.Planner.Fighting, cloudStream, -1, false);
                                }
                                catch (Exception error)
                                {
                                    if (error is OutOfMemoryException || error is StackOverflowException)
                                        throw;
                                    hud = null;
                                }
                                ui = farmVision.Read(frame, !(hud != null && (hud.EnemyHealth || hud.Locked)));
                            }
                        }

                        long now = timer.ElapsedMilliseconds;
                        if (!captured)
                        {
                            farmInput.ReleaseAll();
                            if (forward) { farmInput.KeyUp('W'); forward = false; }
                            if (lostSince == 0) lostSince = now;
                            if (now - lostSince > 5000)
                                farm.ResetToApproach(now);
                            state = window.Available() ? "等待画面" : "等待切回游戏";
                        }
                        else
                        {
                            lostSince = 0;
                            bool wantEchoes = detector != null && (farm.Planner.Fighting ||
                                options.Pickup == FarmPickupMode.Yolo);
                            if (wantEchoes && now - lastDetect >= 300)
                            {
                                lastDetect = now;
                                try
                                {
                                    float threshold = ui != null && ui.InRealm ? .25f : .65f;
                                    echoes = detector.Detect(frame, threshold);
                                }
                                catch (Exception)
                                {
                                    echoes = null;
                                    detector.Dispose();
                                    detector = null;
                                }
                            }
                            else if (!wantEchoes)
                                echoes = null;
                            if (markerFinder != null && farm.NeedsMapMarker && ui != null && !ui.HasMapMarker &&
                                now - lastMarker >= 500)
                            {
                                lastMarker = now;
                                try
                                {
                                    Rectangle marker;
                                    if (markerFinder.TryFind(frame, out marker))
                                    {
                                        ui.HasMapMarker = true;
                                        ui.MapMarker = marker;
                                    }
                                }
                                catch (Exception) { }
                            }
                            if (navigator != null && now - lastNav >= 150 && !farm.Planner.Fighting)
                            {
                                lastNav = now;
                                try
                                {
                                    if (!navigator.TryTurn(frame, out navTurn))
                                        navTurn = double.NaN;
                                }
                                catch (Exception) { navTurn = double.NaN; }
                            }
                            if (ocr != null && now - lastOcr >= 400)
                            {
                                if (farm.WantsLevelScan)
                                {
                                    lastOcr = now;
                                    try
                                    {
                                        levelLines = ocr.Read(frame, new Rectangle((int)(frame.Width * .03),
                                            (int)(frame.Height * .113), (int)(frame.Width * .293),
                                            (int)(frame.Height * .547)));
                                    }
                                    catch (Exception) { levelLines = null; }
                                }
                                else if (farm.WantsPickupScan && ui != null && ui.FPrompt)
                                {
                                    lastOcr = now;
                                    try
                                    {
                                        Rectangle f = ui.FBox;
                                        var lines = ocr.Read(frame, new Rectangle(f.X + f.Width, f.Y - f.Height,
                                            Math.Max(80, f.Width * 8), Math.Max(30, f.Height * 4)));
                                        absorbText = lines.Any(line => line.Text != null &&
                                            (line.Text.Contains("吸收") || line.Text.IndexOf("Absorb",
                                                StringComparison.OrdinalIgnoreCase) >= 0));
                                    }
                                    catch (Exception) { absorbText = false; }
                                }
                            }
                            if (ocr != null && (hud == null || !hud.Valid) && now - lastDialogOcr >= 1000 &&
                                !farm.WantsLevelScan)
                            {
                                lastDialogOcr = now;
                                try
                                {
                                    var lines = ocr.Read(frame, new Rectangle((int)(frame.Width * .2),
                                        (int)(frame.Height * .25), (int)(frame.Width * .6),
                                        (int)(frame.Height * .5)));
                                    dialogText = string.Join(" ", lines.Select(line => line.Text));
                                }
                                catch (Exception) { dialogText = null; }
                            }
                            if (ocr != null && !farm.Planner.Fighting && now - lastBossOcr >= 3000)
                            {
                                lastBossOcr = now;
                                try
                                {
                                    var lines = ocr.Read(frame, new Rectangle((int)(frame.Width * .3305),
                                        (int)(frame.Height * .0046), (int)(frame.Width * .3291),
                                        (int)(frame.Height * .0602)));
                                    bossName = string.Join(" ", lines.Select(line => line.Text));
                                }
                                catch (Exception) { bossName = null; }
                            }
                            var farmFrame = new FarmFrame(hud, hud != null && hud.Valid, ui,
                                new Size(frame.Width, frame.Height), bounds, echoes, navTurn, levelLines, absorbText,
                                dialogText, bossName);
                            FarmDecision decision = farm.Observe(farmFrame, now);
                            state = farm.Status;
                            if (farm.Finished)
                            {
                                progress.Report(new FarmReport
                                {
                                    State = farm.Failure ?? "刷取完成", Failure = farm.Failure, Finished = true,
                                    Kills = farm.Kills, Echoes = farm.Echoes,
                                    Rate = (farm.Echoes > 0 ? farm.Echoes : farm.Kills) / farm.Hours,
                                    Size = bounds.Size, Hud = hud
                                });
                                break;
                            }
                            string action = Execute(decision, frame, bounds, farmInput, input, cancellation, ref forward);
                            if (decision.Request == FarmRequest.Battle && decision.Battle.Kind != ActionKind.Wait)
                            {
                                farm.Sent(decision, now);
                                action = decision.Battle.Label;
                            }
                            if (action != null)
                                lastAction = action;
                            if (!farm.ForwardHeld && forward)
                            {
                                farmInput.KeyUp('W');
                                forward = false;
                            }
                        }

                        if (now - lastReport >= 200)
                        {
                            progress.Report(new FarmReport
                            {
                                State = state, LastAction = lastAction, Kills = farm.Kills,
                                Echoes = farm.Echoes,
                                Rate = (farm.Echoes > 0 ? farm.Echoes : farm.Kills) / farm.Hours,
                                Size = bounds.Size, Hud = hud
                            });
                            lastReport = now;
                        }
                        int remaining = (int)Math.Max(40, 90 - (timer.ElapsedMilliseconds - started));
                        cancellation.WaitHandle.WaitOne(remaining);
                    }
                }
            }
            finally
            {
                if (detector != null) detector.Dispose();
                if (ocr != null) ocr.Dispose();
                try { farmInput.ReleaseAll(); } catch (Exception) { }
                try { input.ReleaseAll(); } catch (Exception) { }
                concerto.Save();
            }
        }

        private static string Execute(FarmDecision decision, PixelFrame frame, Rectangle bounds, FarmInput farmInput,
            WindowInput input, CancellationToken token, ref bool forward)
        {
            Size size = new Size(frame.Width, frame.Height);
            switch (decision.Request)
            {
                case FarmRequest.Battle:
                    if (decision.Battle.Kind == ActionKind.Wait)
                        return null;
                    return input.Send(decision.Battle, token) ? decision.Battle.Label : null;
                case FarmRequest.Click:
                    return farmInput.Click(decision.Point, size, bounds, FarmMouse.Left, 60, token) ? "点击" : null;
                case FarmRequest.Middle:
                    return farmInput.Click(new Point(size.Width / 2, size.Height / 2), size, bounds,
                        FarmMouse.Middle, 40, token) ? "中键" : null;
                case FarmRequest.Key:
                    if (decision.VirtualKey != 0)
                        return farmInput.KeyCode(decision.VirtualKey, 60, token) ? "功能键" : null;
                    return farmInput.Key(decision.Key, 60, token) ? decision.Key.ToString() : null;
                case FarmRequest.KeyDown:
                    if (decision.Key == 'W')
                    {
                        forward = true;
                        return farmInput.KeyDown('W') ? "前进" : null;
                    }
                    return farmInput.KeyDown(decision.Key) ? decision.Key.ToString() : null;
                case FarmRequest.KeyUp:
                    if (decision.Key == 'W') forward = false;
                    farmInput.KeyUp(decision.Key);
                    return null;
                case FarmRequest.Text:
                    return farmInput.TypeText(decision.Text, token) ? "输入文字" : null;
                case FarmRequest.Scroll:
                    return farmInput.Scroll(1, new Point(size.Width / 2, size.Height / 2), size, bounds, token) ? "滚动" : null;
                default:
                    return null;
            }
        }
    }
}
