using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TideCombat
{
    internal sealed class EnhanceReport
    {
        internal string State;
        internal int Enhanced;
        internal int Discarded;
        internal Size Size;
        internal bool Finished;
        internal bool Paused;
        internal string Failure;
    }

    internal static class EnhanceLoop
    {
        private static readonly Rectangle CultivateArea = new Rectangle(1312, 774, 240, 90);
        private static readonly Rectangle EchoSkillArea = new Rectangle(1040, 315, 560, 198);
        private static readonly Rectangle StageArea = new Rectangle(144, 540, 464, 234);
        private static readonly Rectangle EnhanceArea = new Rectangle(160, 792, 304, 72);
        private static readonly Rectangle DialogArea = new Rectangle(384, 162, 816, 720);
        private static readonly Rectangle StatsArea = new Rectangle(144, 270, 496, 207);
        private static readonly Rectangle IconArea = new Rectangle(420, 105, 170, 66);

        internal static void Run(CombatSettings settings, EnhanceOptions options, IProgress<EnhanceReport> progress,
            CancellationToken cancellation, ManualResetEventSlim pause = null)
        {
            var window = new GameWindow(settings.Source);
            var input = new FarmInput(window);
            var vision = new FarmVision();
            var task = new EchoEnhanceTask(options);
            OcrReader ocr = null;
            var timer = Stopwatch.StartNew();
            long lastReport = -500, lastOcr = -1000;
            try
            {
                string directory = OcrReader.Directory;
                if (File.Exists(Path.Combine(directory, "det.onnx")))
                    ocr = new OcrReader(directory);
            }
            catch (Exception)
            {
                ocr = null;
            }
            if (ocr == null)
            {
                progress.Report(new EnhanceReport
                {
                    State = "缺少 OCR 模型，无法运行", Failure = "缺少 OCR 模型，无法运行", Finished = true
                });
                return;
            }
            try
            {
                using (var capture = new BattleLoop.WindowFrames(window))
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        long started = timer.ElapsedMilliseconds;
                        Rectangle bounds = Rectangle.Empty;
                        string state = "等待《鸣潮》客户端";
                        PixelFrame frame = default(PixelFrame);
                        bool captured = false;
                        if (window.TryLocate(out bounds, out state))
                        {
                            if (window.InputBlockedByIntegrity)
                                throw new InputElevationRequiredException();
                            try { captured = capture.Read(bounds, out frame) && window.Available(); }
                            catch (System.ComponentModel.Win32Exception) { state = "游戏截图暂不可用"; }
                        }
                        long now = timer.ElapsedMilliseconds;
                        if (captured && now - lastOcr >= 250)
                        {
                            lastOcr = now;
                            var current = new EnhanceFrame
                            {
                                Width = frame.Width, Height = frame.Height
                            };
                            try { Scan(task, frame, current, ocr); }
                            catch (Exception) { }
                            if (task.WantsIcons)
                                ReadIcons(frame, current, vision);
                            EnhanceDecision decision = task.Observe(current, now);
                            Execute(decision, frame, bounds, input, window, cancellation);
                            state = task.Status;
                            if (task.ScreenshotKind != null)
                            {
                                TrySave(frame, task.ScreenshotKind, task.ScreenshotReason);
                                task.ClearScreenshot();
                            }
                        }
                        else if (!captured)
                        {
                            state = window.Available() ? "等待画面" : "等待切回游戏";
                        }
                        if (task.Finished || task.Paused)
                        {
                            progress.Report(new EnhanceReport
                            {
                                State = task.Status, Enhanced = task.Enhanced, Discarded = task.Discarded,
                                Size = bounds.Size, Finished = task.Finished, Paused = task.Paused,
                                Failure = task.Failure
                            });
                            if (task.Finished)
                                break;
                            if (pause == null)
                                break;
                            while (!cancellation.IsCancellationRequested)
                            {
                                if (pause.Wait(100))
                                    break;
                            }
                            if (cancellation.IsCancellationRequested)
                                break;
                            pause.Reset();
                            task.Resume();
                            continue;
                        }
                        if (now - lastReport >= 250)
                        {
                            progress.Report(new EnhanceReport
                            {
                                State = state, Enhanced = task.Enhanced, Discarded = task.Discarded,
                                Size = bounds.Size
                            });
                            lastReport = now;
                        }
                        int remaining = (int)Math.Max(50, 120 - (timer.ElapsedMilliseconds - started));
                        cancellation.WaitHandle.WaitOne(remaining);
                    }
                }
            }
            finally
            {
                try { input.ReleaseAll(); } catch (Exception) { }
                if (ocr != null) ocr.Dispose();
            }
        }

        private static void Scan(EchoEnhanceTask task, PixelFrame frame, EnhanceFrame current, OcrReader ocr)
        {
            switch (task.Scan)
            {
                case EnhanceScan.Cultivate:
                {
                    var lines = ocr.Read(frame, Scale(frame, CultivateArea));
                    current.Lines = lines;
                    foreach (OcrLine line in lines)
                    {
                        if (line.Text == null || !line.Text.Contains("培养")) continue;
                        current.HasCultivate = true;
                        current.CultivateBox = line.Box;
                        break;
                    }
                    break;
                }
                case EnhanceScan.EchoSkill:
                    current.LevelZero = Contains(ocr.Read(frame, Scale(frame, EchoSkillArea)), "声骸技能");
                    break;
                case EnhanceScan.StageInsert:
                {
                    var lines = ocr.Read(frame, Scale(frame, StageArea));
                    current.Lines = lines;
                    foreach (OcrLine line in lines)
                    {
                        if (line.Text == null || !line.Text.Contains("阶段放入")) continue;
                        current.HasStageInsert = true;
                        current.StageBox = line.Box;
                        break;
                    }
                    break;
                }
                case EnhanceScan.EnhanceButtons:
                {
                    var lines = ocr.Read(frame, Scale(frame, EnhanceArea));
                    current.Lines = lines;
                    foreach (OcrLine line in lines)
                    {
                        string text = line.Text ?? string.Empty;
                        if (text.Contains("强化并调谐"))
                        {
                            current.HasEnhanceTune = true;
                            current.EnhanceBox = line.Box;
                        }
                        else if (text.Contains("强化") && !text.Contains("调谐"))
                            current.HasPlainEnhance = true;
                    }
                    break;
                }
                case EnhanceScan.Dialog:
                    current.Lines = ocr.Read(frame, Scale(frame, DialogArea));
                    break;
                case EnhanceScan.Stats:
                    current.Lines = ocr.Read(frame, Scale(frame, StatsArea));
                    break;
            }
        }

        private static void ReadIcons(PixelFrame frame, EnhanceFrame current, FarmVision vision)
        {
            Rectangle box;
            if (vision.FindFirst(frame, "echo_locked", IconArea, 2, .82, .72, out box))
            {
                current.Locked = true;
                current.LockKnown = true;
            }
            else if (vision.FindFirst(frame, "echo_not_locked", IconArea, 2, .82, .72, out box))
            {
                current.LockKnown = true;
            }
            if (vision.FindFirst(frame, "echo_dropped", IconArea, 2, .82, .72, out box))
            {
                current.Dropped = true;
                current.DropKnown = true;
            }
            else if (vision.FindFirst(frame, "echo_not_dropped", IconArea, 2, .82, .72, out box))
            {
                current.DropKnown = true;
            }
        }

        private static void Execute(EnhanceDecision decision, PixelFrame frame, Rectangle bounds, FarmInput input,
            GameWindow window, CancellationToken cancellation)
        {
            if (decision.Action == EnhanceAction.None)
                return;
            var size = new Size(frame.Width, frame.Height);
            try
            {
                if (decision.Action == EnhanceAction.Click)
                    input.Click(decision.Point, size, bounds, FarmMouse.Left, 60, cancellation);
                else
                    input.Key(decision.Key, 60, cancellation);
            }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static bool Contains(System.Collections.Generic.List<OcrLine> lines, string text)
        {
            foreach (OcrLine line in lines)
                if (line.Text != null && line.Text.Contains(text))
                    return true;
            return false;
        }

        private static Rectangle Scale(PixelFrame frame, Rectangle area)
        {
            return new Rectangle((int)(area.X * frame.Width / 1600.0), (int)(area.Y * frame.Height / 900.0),
                Math.Max(1, (int)(area.Width * frame.Width / 1600.0)),
                Math.Max(1, (int)(area.Height * frame.Height / 900.0)));
        }

        private static void TrySave(PixelFrame frame, string kind, string reason)
        {
            Screenshots.Save(frame, kind, reason, new Rectangle(144, 81, 448, 414));
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char character in value)
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
            return builder.ToString();
        }
    }
}
