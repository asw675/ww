using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    internal sealed class EnhanceReport
    {
        internal string State;
        internal int Enhanced;
        internal int Discarded;
        internal int ItemNumber, Round;
        internal EnhanceStageReadout LastStage;
        internal string EvidenceDirectory;
        internal Size Size;
        internal bool Finished;
        internal bool Paused;
        internal string Failure;
    }

    internal static class EnhanceLoop
    {
        private const string LogArea = "强化声骸";
        private static readonly Rectangle CultivateArea = new Rectangle(1312, 774, 240, 90);
        private static readonly Rectangle BagLevelArea = new Rectangle(1098, 149, 60, 53);
        private static readonly Rectangle LegacyBagLevelArea = new Rectangle(1465, 200, 72, 40);
        private static readonly Rectangle BagSortArea = new Rectangle(278, 802, 210, 46);
        private static readonly Rectangle FirstCardTop = new Rectangle(157, 108, 100, 16);
        private static readonly Rectangle FirstCardBottom = new Rectangle(157, 260, 100, 16);
        private static readonly Rectangle LegacyFirstCardTop = new Rectangle(184, 96, 98, 15);
        private static readonly Rectangle LegacyFirstCardBottom = new Rectangle(184, 248, 98, 13);
        private static readonly Rectangle SortUp = new Rectangle(555, 827, 3, 8);
        private static readonly Rectangle SortDown = new Rectangle(563, 816, 3, 8);
        private static readonly Rectangle LegacySortUp = new Rectangle(535, 819, 3, 8);
        private static readonly Rectangle LegacySortDown = new Rectangle(540, 813, 3, 8);
        private static readonly Rectangle EnhanceHeader = new Rectangle(40, 30, 280, 66);
        private static readonly Rectangle MaterialArea = new Rectangle(144, 510, 464, 354);
        private static readonly Rectangle PreviewArea = new Rectangle(156, 113, 241, 80);
        private static readonly Rectangle DialogArea = new Rectangle(384, 162, 816, 720);
        private static readonly Rectangle ConfirmArea = new Rectangle(928, 528, 380, 210);
        private static readonly Rectangle StatsArea = new Rectangle(144, 270, 496, 207);
        private static readonly Rectangle MarkArea = new Rectangle(404, 78, 198, 87);
        private static readonly string[] LockLabels = { "echo_locked", "echo_not_locked" };
        private static readonly string[] DiscardLabels = { "echo_dropped", "echo_not_dropped" };
        private static readonly string[] CheckLabels = { "skip_dialog_check" };
        private static readonly string[] ConfirmLabels =
            { "confirm_btn_hcenter_vcenter", "confirm_btn_highlight_hcenter_vcenter" };
        private static readonly double[] MarkScales = { .88, 1, 1.12, 1.23, 1.35, 1.48 };
        private const int MenuClickHoldMs = 200;
        private static readonly Regex MaterialCount = new Regex(@"强化消耗材料\s*\(?\s*(\d{1,3})\s*/\s*\d{1,3}");
        private static readonly Regex PreviewLevel = new Regex(@"\+\s*([0-9]{1,2})(?![0-9])");

        internal static EnhanceReport Run(CombatSettings settings, EnhanceOptions options,
            IProgress<EnhanceReport> progress, CancellationToken cancellation, ManualResetEventSlim pause = null,
            bool openEvidenceFolder = true)
        {
            var task = new EchoEnhanceTask(options);
            var window = GameWindow.ForTask(settings.Source);
            var input = new FarmInput(window);
            var vision = new FarmVision();
            var timer = Stopwatch.StartNew();
            string evidenceDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots",
                "enhance-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            long frameLostAt = 0, pausedMs = 0, lastOcr = -1000, lastReport = -500;
            bool captureError = false, scanError = false;
            string lastState = null;
            IntPtr target = IntPtr.Zero;
            uint targetProcess = 0;
            Size size = Size.Empty;
            OcrReader ocr = null;
            try
            {
                options.Validate();
                if (!File.Exists(Path.Combine(OcrReader.Directory, "det.onnx")))
                    throw new InvalidDataException("缺少强化 OCR 模型。");
                ocr = new OcrReader(OcrReader.Directory);
                RunLog.Info(LogArea, string.Format(
                    "开始 · 来源={0} · 必须双爆={1} · 首爆门槛={2} · 双爆总计={3} · 双爆前全有效={4} · 第一条有效={5} · 有效词条数={6} · 有效词条={7} · 成功暂停={8} · 截图目录={9}",
                    settings.Source, options.RequireBothCrit, options.FirstCritMinimum, options.CritTotalMinimum,
                    options.RequireValidBeforeCrit, options.FirstMustBeValid, options.ValidCountMinimum,
                    string.Join("、", options.ValidStats), options.PauseAfterSuccess, evidenceDirectory));

                using (var capture = new BattleLoop.WindowFrames(window))
                using (var cursorAssist = WorldAssist.CreateCursorOnly(settings.WorldAssist, vision))
                {
                    while (!cancellation.IsCancellationRequested && !task.Finished)
                    {
                        long started = timer.ElapsedMilliseconds;
                        Rectangle bounds;
                        string state;
                        PixelFrame frame = default(PixelFrame);
                        bool captured = false;
                        if (window.TryLocate(out bounds, out state))
                        {
                            if (window.InputBlockedByIntegrity)
                                throw new InputElevationRequiredException();
                            if (target != IntPtr.Zero && (window.Handle != target || window.ProcessId != targetProcess))
                            {
                                task.Fail("游戏窗口已更换，无法继续确认原声骸。");
                                break;
                            }
                            try
                            {
                                captured = capture.Read(bounds, out frame) && window.Available();
                                state = captured ? task.Status : "已找到游戏窗口，等待可用画面";
                                if (captured)
                                {
                                    captureError = false;
                                    target = window.Handle;
                                    targetProcess = window.ProcessId;
                                    size = bounds.Size;
                                }
                            }
                            catch (System.ComponentModel.Win32Exception error)
                            {
                                state = "游戏截图暂不可用";
                                if (!captureError)
                                    RunLog.Error(LogArea, state, error);
                                captureError = true;
                            }
                        }

                        long now = timer.ElapsedMilliseconds;
                        if (cursorAssist != null)
                        {
                            if (captured) cursorAssist.UpdateCursor(window, bounds, now);
                            else cursorAssist.ResetWindow();
                        }
                        if (!captured && frameLostAt < 0)
                            frameLostAt = now;
                        else if (captured && frameLostAt >= 0)
                        {
                            pausedMs += now - frameLostAt;
                            frameLostAt = -1;
                            lastOcr = -1000;
                            RunLog.Info(LogArea, "游戏画面可用 · 模式=" + window.Mode);
                        }
                        if (captured && now - lastOcr >= 250)
                        {
                            lastOcr = now;
                            var current = new EnhanceFrame { Width = frame.Width, Height = frame.Height };
                            try
                            {
                                Scan(task, frame, current, ocr, vision);
                                scanError = false;
                            }
                            catch (Exception error)
                            {
                                if (!scanError)
                                    RunLog.Error(LogArea, "强化界面识别失败 · 检测项=" + task.Scan, error);
                                scanError = true;
                                current = new EnhanceFrame { Width = frame.Width, Height = frame.Height };
                            }
                            if (cancellation.IsCancellationRequested)
                                break;
                            current.Background = window.Mode == GameSource.Background;
                            current.AutoBackground = settings.Source == GameSource.AutoLocal && current.Background;
                            EnhanceDecision decision = task.Observe(current, timer.ElapsedMilliseconds - pausedMs);
                            if (Execute(decision, frame, bounds, window, input, task, cancellation) && decision.Sent != null)
                                decision.Sent(timer.ElapsedMilliseconds - pausedMs);
                            SaveEvidence(evidenceDirectory, frame, task);
                            state = task.Status;
                        }

                        if (state != lastState)
                        {
                            RunLog.Info(LogArea, string.Format("{0} · 检测项={1} · 成功={2} · 弃置={3}",
                                state, task.Scan, task.Enhanced, task.Discarded));
                            lastState = state;
                        }
                        if (task.Paused)
                        {
                            RunLog.Info(LogArea, "已锁定合格声骸，等待继续");
                            if (progress != null)
                                progress.Report(Report(task, size, state, evidenceDirectory));
                            if (pause == null)
                                break;
                            long pauseStarted = timer.ElapsedMilliseconds;
                            while (!cancellation.IsCancellationRequested && !pause.Wait(100)) { }
                            if (cancellation.IsCancellationRequested)
                                break;
                            pause.Reset();
                            pausedMs += timer.ElapsedMilliseconds - pauseStarted;
                            task.Resume(timer.ElapsedMilliseconds - pausedMs);
                            lastOcr = -1000;
                            RunLog.Info(LogArea, "收到继续指令，重新开始返回背包计时");
                            continue;
                        }
                        if (progress != null && !task.Finished && now - lastReport >= 250)
                        {
                            progress.Report(Report(task, size, state, evidenceDirectory));
                            lastReport = now;
                        }
                        cancellation.WaitHandle.WaitOne((int)Math.Max(50, 120 - (timer.ElapsedMilliseconds - started)));
                    }
                }
            }
            catch (InputElevationRequiredException) { throw; }
            catch (Exception error)
            {
                RunLog.Error(LogArea, "强化任务失败", error);
                task.Fail(error.Message);
            }
            finally
            {
                try { input.ReleaseAll(); }
                catch (Exception error) { RunLog.Error(LogArea, "释放输入失败", error); }
                if (ocr != null)
                    ocr.Dispose();
            }

            var result = Report(task, size, cancellation.IsCancellationRequested ? "已停止" : task.Status,
                evidenceDirectory);
            if (progress != null)
                progress.Report(result);
            RunLog.Info(LogArea, string.Format("退出任务 · 已取消={0} · 已完成={1} · 成功={2} · 弃置={3} · 原因={4}",
                cancellation.IsCancellationRequested, task.Finished && task.Failure == null, task.Enhanced, task.Discarded,
                task.Failure ?? result.State));
            if (openEvidenceFolder && task.Finished && task.Failure == null && task.Enhanced > 0 &&
                !cancellation.IsCancellationRequested)
                OpenEvidence(evidenceDirectory);
            return result;
        }

        private static EnhanceReport Report(EchoEnhanceTask task, Size size, string state, string evidenceDirectory)
        {
            return new EnhanceReport
            {
                State = state, Enhanced = task.Enhanced, Discarded = task.Discarded, Size = size,
                ItemNumber = task.ItemNumber, Round = task.Round, LastStage = task.LastStage,
                EvidenceDirectory = Directory.Exists(evidenceDirectory) ? evidenceDirectory : null,
                Finished = task.Finished, Paused = task.Paused, Failure = task.Failure
            };
        }

        internal static void Scan(EchoEnhanceTask task, PixelFrame frame, EnhanceFrame current, OcrReader ocr,
            FarmVision vision)
        {
            switch (task.Scan)
            {
                case EnhanceScan.Cultivate:
                case EnhanceScan.BagCandidate:
                    current.CultivateBox = FindText(ocr.Read(frame, Scale(frame, CultivateArea)), "培养");
                    current.HasCultivate = !current.CultivateBox.IsEmpty;
                    if ((task.Scan == EnhanceScan.BagCandidate || task.CheckCultivateCandidate) && current.HasCultivate)
                        ReadBagCandidate(frame, current, ocr, task.CheckingBagOrder);
                    if (task.CheckEnhancePage && !current.HasCultivate)
                    {
                        current.HasEnhancePanel = IsEnhancePanel(frame, ocr);
                        if (!current.HasEnhancePanel)
                            ReadDialog(frame, current, ocr, vision);
                    }
                    break;
                case EnhanceScan.StageInsert:
                case EnhanceScan.EnhanceButtons:
                    current.Lines = ReadMaterialControls(frame, current, ocr);
                    if (current.HasEnhanceTune && (task.Scan == EnhanceScan.EnhanceButtons || current.MaterialCount > 0))
                        ReadEchoLevels(frame, current, ocr);
                    if (!current.HasStageInsert && !current.MaterialsStaged)
                        ReadDialog(frame, current, ocr, vision);
                    break;
                case EnhanceScan.Dialog:
                    ReadSubmissionFrame(frame, current, ocr, vision);
                    break;
                case EnhanceScan.Stats:
                    if (IsEnhancePanel(frame, ocr))
                    {
                        current.Lines = ocr.Read(frame, Scale(frame, StatsArea));
                        ReadMaterialControls(frame, current, ocr);
                        ReadEchoLevels(frame, current, ocr);
                        if (task.HasUnclosedResult || task.HasPendingUpgrade(current) ||
                            EchoRules.Parse(current.Lines).Count < task.RequiredStatCount)
                            ReadDialog(frame, current, ocr, vision);
                    }
                    else
                    {
                        current.Lines.Clear();
                        ReadSubmissionFrame(frame, current, ocr, vision);
                    }
                    break;
            }
            if (task.WantsLock || task.WantsDiscard)
            {
                if (!IsEnhancePanel(frame, ocr))
                {
                    ReadDialog(frame, current, ocr, vision);
                    return;
                }
                if (task.WantsLock)
                    current.LockMark = ReadMark(frame, vision, LockLabels);
                else
                    current.DropMark = ReadMark(frame, vision, DiscardLabels);
            }
        }

        private static void ReadBagCandidate(PixelFrame frame, EnhanceFrame current, OcrReader ocr, bool checkingOrder)
        {
            bool firstSelected = HasCardEdge(frame, FirstCardTop) && HasCardEdge(frame, FirstCardBottom);
            bool legacy = !firstSelected && HasCardEdge(frame, LegacyFirstCardTop) && HasCardEdge(frame, LegacyFirstCardBottom);
            current.FirstEchoSelected = firstSelected || legacy;
            current.Lines = ocr.Read(frame, Scale(frame, legacy ? LegacyBagLevelArea : BagLevelArea));
            current.BagLevel = FindBagLevel(current.Lines);
            if (current.BagLevel < 0 && !current.FirstEchoSelected)
            {
                List<OcrLine> legacyLines = ocr.Read(frame, Scale(frame, LegacyBagLevelArea));
                current.BagLevel = FindBagLevel(legacyLines);
                current.Lines.AddRange(legacyLines);
                legacy = current.BagLevel >= 0;
            }
            if (current.BagLevel < 0)
            {
                Rectangle skillArea = Rectangle.FromLTRB((int)(frame.Width * .65), (int)(frame.Height * .35),
                    frame.Width, (int)(frame.Height * .57));
                current.HasEchoSkillTitle = ocr.Read(frame, skillArea).Any(line =>
                    line.Score >= .65f && (line.Text ?? string.Empty).Contains("声骸技能"));
            }
            if (current.ZeroLevelCandidate && !checkingOrder)
                return;

            List<OcrLine> order = ocr.Read(frame, Scale(frame, BagSortArea));
            current.Lines.AddRange(order);
            if (FindText(order, "等级顺序", "等级排序").IsEmpty)
                return;
            // The active direction has a brighter arrow in both bag layouts.
            int up = AverageBrightness(frame, legacy ? LegacySortUp : SortUp);
            int down = AverageBrightness(frame, legacy ? LegacySortDown : SortDown);
            if (Math.Max(up, down) >= 145 && Math.Abs(up - down) >= 24)
                current.BagOrder = up > down ? EchoBagOrder.Ascending : EchoBagOrder.Descending;
        }

        private static int FindBagLevel(List<OcrLine> lines)
        {
            int level = -1;
            foreach (OcrLine line in lines)
            {
                string text = EnhanceText.Normalize(line.Text);
                int parsed;
                if (line.Score < .65f || !text.StartsWith("+", StringComparison.Ordinal) ||
                    !int.TryParse(text.Substring(1).Trim(), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed) || parsed > 25)
                    continue;
                if (level >= 0 && level != parsed)
                    return -1;
                level = parsed;
            }
            return level;
        }

        private static bool HasCardEdge(PixelFrame frame, Rectangle region)
        {
            Rectangle area = Scale(frame, region);
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int bright = 0;
                for (int x = area.Left; x < area.Right; x++)
                {
                    int red, green, blue;
                    frame.Rgb(x, y, out red, out green, out blue);
                    if (red >= 180 && green >= 170 && blue >= 105 && green <= red + 25)
                        bright++;
                }
                if (bright * 3 >= area.Width * 2)
                    return true;
            }
            return false;
        }

        private static int AverageBrightness(PixelFrame frame, Rectangle region)
        {
            Rectangle area = Scale(frame, region);
            int total = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int red, green, blue;
                    frame.Rgb(x, y, out red, out green, out blue);
                    total += red + green + blue;
                }
            return total / Math.Max(1, area.Width * area.Height * 3);
        }

        private static bool IsEnhancePanel(PixelFrame frame, OcrReader ocr)
        {
            return !FindText(ocr.Read(frame, Scale(frame, EnhanceHeader)), "声骸强化").IsEmpty;
        }

        private static List<OcrLine> ReadMaterialControls(PixelFrame frame, EnhanceFrame current, OcrReader ocr)
        {
            var lines = ocr.Read(frame, Scale(frame, MaterialArea));
            current.StageBox = FindText(lines, "阶段放入");
            current.HasStageInsert = !current.StageBox.IsEmpty;
            current.HasClearMaterials = !FindText(lines, "清除").IsEmpty;
            current.EnhanceBox = FindText(lines, "强化并调谐", "强化并调律");
            current.HasEnhanceTune = !current.EnhanceBox.IsEmpty;
            current.HasPlainEnhance = !FindText(lines, "强化").IsEmpty;
            foreach (OcrLine line in lines)
            {
                Match count = line.Score >= .65f ? MaterialCount.Match(EnhanceText.Normalize(line.Text)) : Match.Empty;
                if (!count.Success)
                    continue;
                current.MaterialCount = int.Parse(count.Groups[1].Value);
                break;
            }
            return lines;
        }

        private static void ReadSubmissionFrame(PixelFrame frame, EnhanceFrame current, OcrReader ocr, FarmVision vision)
        {
            ReadDialog(frame, current, ocr, vision);
            if (current.OverflowDialog || !current.DismissBox.IsEmpty)
                return;
            ReadMaterialControls(frame, current, ocr);
            if (current.HasEnhanceTune)
            {
                ReadEchoLevels(frame, current, ocr);
                if (current.Preview != null && current.Lines.Count == 0)
                    current.Lines = ocr.Read(frame, Scale(frame, StatsArea));
            }
        }

        private static void ReadEchoLevels(PixelFrame frame, EnhanceFrame current, OcrReader ocr)
        {
            var lines = ocr.Read(frame, Scale(frame, PreviewArea)).Where(line => line.Score >= .65f)
                .Select(line => new { Text = EnhanceText.Normalize(line.Text), line.Box }).ToArray();
            // The experience preview can start inside this OCR crop. Only the left column contains levels.
            int levelRight = 321 * frame.Width / 1600;
            var levelLines = lines.Where(line => line.Box.Left < levelRight && PreviewLevel.IsMatch(line.Text))
                .OrderBy(line => line.Box.Left).ToArray();
            var levels = levelLines.SelectMany(line => PreviewLevel.Matches(line.Text).Cast<Match>())
                .Select(match => int.Parse(match.Groups[1].Value)).ToArray();
            if (levels.Length == 0 || levels[0] > 25)
                return;
            current.CurrentLevel = levels[0];
            if (levels.Length != 2 || levels[0] >= levels[1] || levels[1] > 25)
                return;
            current.PreviewLevel = levels[1];
            current.TargetStatCount = levels[1] / 5;
            int levelTop = levelLines.Min(line => line.Box.Top);
            string name = lines.Where(line => line.Box.Bottom <= levelTop).Select(line => line.Text).FirstOrDefault();
            if (!string.IsNullOrEmpty(name) && current.MaterialCount > 0)
                current.Preview = string.Format("{0} +{1} → +{2} · 材料={3}", name, levels[0], levels[1], current.MaterialCount);
        }

        internal static EchoMark ReadMark(PixelFrame frame, FarmVision vision, string[] labels)
        {
            Rectangle box;
            Func<string, Rectangle, bool> validate = null;
            if (labels[0] == "echo_dropped")
                validate = (candidate, area) => HasDiscardRed(frame, area) == (candidate == "echo_dropped");
            string label = vision.BestMatchInRegion(frame, labels, Scale(frame, MarkArea), 1, .78, .69,
                out box, MarkScales, validate);
            return label == null ? EchoMark.Unknown : label == labels[0] ? EchoMark.Marked : EchoMark.Unmarked;
        }

        private static bool HasDiscardRed(PixelFrame frame, Rectangle area)
        {
            int redPixels = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int red, green, blue;
                    frame.Rgb(x, y, out red, out green, out blue);
                    if (red >= 145 && red - green >= 46 && red - blue >= 24)
                        redPixels++;
                }
            return redPixels >= area.Width * area.Height * .15;
        }

        private static void ReadDialog(PixelFrame frame, EnhanceFrame current, OcrReader ocr, FarmVision vision)
        {
            List<OcrLine> lines = ocr.Read(frame, Scale(frame, DialogArea));
            Rectangle skipText = FindText(lines, "本次登录不再提示", "本次登入不再提示");
            bool overflow = lines.Any(line =>
            {
                string text = EnhanceText.Normalize(line.Text);
                return text.Contains("材料") && text.Contains("溢出");
            });
            current.OverflowDialog = overflow || !skipText.IsEmpty;
            if (current.OverflowDialog)
            {
                Rectangle confirm = FindText(lines, "确认", "确定");
                if (confirm.Left >= frame.Width * .54 && confirm.Top >= frame.Height * .53)
                    current.ConfirmBox = confirm;
                else
                    vision.BestMatchInRegion(frame, ConfirmLabels, Scale(frame, ConfirmArea), 2, .80, .68,
                        out current.ConfirmBox);
                if (!skipText.IsEmpty)
                    current.CheckBox = ReadCheckbox(frame, vision, skipText);
                return;
            }
            current.DismissBox = FindResultDismissTarget(lines);
        }

        internal static Rectangle FindResultDismissTarget(List<OcrLine> lines)
        {
            // The heading and the return instruction can appear together at different positions.
            Rectangle instruction = FindText(lines, "点击任意位置返回", "点击任意位置继续",
                "点击任意位置关闭", "点击任意处返回", "点击任意处关闭");
            return instruction.IsEmpty ? FindText(lines, "调谐成功", "调律成功") : instruction;
        }

        private static Rectangle ReadCheckbox(PixelFrame frame, FarmVision vision, Rectangle text)
        {
            int height = text.Height;
            var area = new Rectangle(text.Left - height * 2, text.Y - height, height * 2, height * 3);
            Rectangle box;
            if (vision.BestMatchInRegion(frame, CheckLabels, area, 1, .72, .65, out box) != null)
                return box;
            var point = new Point(text.Left - height, text.Top + height / 2);
            return point.X > 0 && frame.Pale(point.X, point.Y, 225, 24)
                ? new Rectangle(point, new Size(1, 1)) : Rectangle.Empty;
        }

        private static Rectangle FindText(List<OcrLine> lines, params string[] labels)
        {
            Rectangle result = Rectangle.Empty;
            foreach (OcrLine line in lines)
            {
                if (line.Score < .65f || Array.IndexOf(labels, EnhanceText.Normalize(line.Text)) < 0)
                    continue;
                if (!result.IsEmpty && !result.IntersectsWith(line.Box))
                    return Rectangle.Empty;
                result = line.Box;
            }
            return result;
        }

        private static bool Execute(EnhanceDecision decision, PixelFrame frame, Rectangle bounds, GameWindow window, FarmInput input,
            EchoEnhanceTask task, CancellationToken cancellation)
        {
            if (decision.Action == EnhanceAction.None || cancellation.IsCancellationRequested)
                return false;
            string action = string.Format("第 {0} 只 · 第 {1} 档 · {2}", task.ItemNumber, task.Round, decision.Label);
            try
            {
                if (decision.Action == EnhanceAction.ActivateGame)
                {
                    bool activated = window.TryActivateForAutoTask();
                    RunLog.Info(LogArea, action + " · 请求受理=" + activated);
                    return activated;
                }
                bool sent = decision.Action == EnhanceAction.Click ?
                    input.Click(decision.Point, new Size(frame.Width, frame.Height), bounds, FarmMouse.Left,
                        MenuClickHoldMs, cancellation, decision.SettleMs, decision.UseWindowMessage) :
                    input.Key(decision.Key, 60, cancellation);
                if (sent)
                    RunLog.Info(LogArea, action + " · 已投递");
                else if (!cancellation.IsCancellationRequested)
                    RunLog.Warn(LogArea, action + " · 输入未发出");
                return sent;
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                RunLog.Error(LogArea, action + " · 输入失败", error);
                return false;
            }
        }

        private static Rectangle Scale(PixelFrame frame, Rectangle area)
        {
            return Rectangle.FromLTRB(area.Left * frame.Width / 1600, area.Top * frame.Height / 900,
                area.Right * frame.Width / 1600, area.Bottom * frame.Height / 900);
        }

        private static void SaveEvidence(string directory, PixelFrame frame, EchoEnhanceTask task)
        {
            string kind = task.ScreenshotKind;
            if (kind == null)
                return;
            string reason = task.ScreenshotReason;
            task.ClearScreenshot();
            try
            {
                string folder = Path.Combine(directory, kind);
                Directory.CreateDirectory(folder);
                string name = EvidenceName(kind, task.ItemNumber, task.Round, task.Enhanced, task.Discarded, reason);
                if (kind == "materials")
                    name += "-" + DateTime.Now.ToString("HHmmssfff");
                string path = Path.Combine(folder, name + ".png");
                Rectangle area = kind == "before" || kind == "error" || kind == "selection"
                    ? new Rectangle(0, 0, frame.Width, frame.Height)
                    : Scale(frame, EvidenceCrop(kind));
                Screenshots.WritePng(frame, area, path);
                RunLog.Info(LogArea, "已保存强化截图 · " + kind + " · " + path +
                    (string.IsNullOrEmpty(reason) ? "" : " · 原因=" + reason));
            }
            catch (Exception error)
            {
                RunLog.Error(LogArea, "保存强化截图失败 · 类型=" + kind, error);
            }
        }

        internal static string EvidenceName(string kind, int item, int round, int enhanced, int discarded, string reason)
        {
            if (kind == "success")
                return enhanced.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (kind == "failed")
            {
                // Match the reference's numbered rejected-Echo evidence without
                // allowing the OCR/error text to alter the local directory.
                string safe = Regex.Replace(reason ?? string.Empty, @"[<>:""/\\|?*\x00-\x1F]", string.Empty);
                return discarded.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" + safe;
            }
            return string.Format("{0:D3}-{1:D2}", item, round);
        }

        internal static Rectangle EvidenceCrop(string kind)
        {
            // Reference result screenshot: (0.09,0.09) to (0.37,0.55).
            // Keep the broader diagnostic stage/material screenshots separate.
            return new Rectangle(144, 81, kind == "success" || kind == "failed" ? 448 : 496,
                kind == "materials" ? 783 : 414);
        }

        internal static void OpenEvidence(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    using (Process opened = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true })) { }
            }
            catch (Exception error)
            {
                RunLog.Warn(LogArea, "打开截图目录失败 · " + error.Message);
            }
        }
    }
}
