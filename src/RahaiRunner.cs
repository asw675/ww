using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    internal sealed class RahaiRunner : IDisposable
    {
        private const string EventName = "拉海洛方块·逆转";
        private static readonly Regex StageStatus = new Regex(@"进行中|已(?:通关|完成)|\d+\s*(?:天|小时|分钟)");
        private static readonly Regex StageProgress = new Regex(@"进行中.*?(\d+)\s*[/／]\s*(\d+)");
        private static readonly Point[] PreviewOffsets =
        {
            Point.Empty, new Point(0, -12), new Point(-12, 0), new Point(12, 0), new Point(0, 12)
        };

        private sealed class ChallengeFailed : Exception
        {
            internal ChallengeFailed(string message) : base(message) { }
        }

        internal sealed class Stage
        {
            internal string Name, Status;
            internal Rectangle Box;
        }

        private readonly WorldSession world;
        private readonly CancellationToken token;
        private readonly IProgress<string> progress;
        private IntPtr targetHandle;
        private uint targetProcess;
        private int foregroundRoute = -1;
        private Size targetSize;

        internal RahaiRunner(CombatSettings settings, CancellationToken token, IProgress<string> progress)
        {
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
            world.RetainEchoFrames = true;
        }

        internal void Run()
        {
            if (world.Settings.Source != GameSource.AutoLocal && world.Settings.Source != GameSource.Foreground)
                throw new InvalidOperationException("拉海洛方块仅允许自动本地或前台 Windows 客户端。\n请手动选择允许前台操作的路线。");
            if (!WaitUntil(() => true, 6000) || !RahaiVision.Supported(world.Frame))
                throw new InvalidOperationException("拉海洛方块需要 16:9 的 Windows 客户端。");
            targetHandle = world.Window.Handle;
            targetProcess = world.Window.ProcessId;
            List<Stage> stages = WaitForList("请先打开简体中文的「" + EventName + "」活动关卡列表。");
            if (world.Window.Mode != GameSource.Foreground &&
                (!world.Window.TryActivateForAutoTask() || !WaitUntil(() => world.Window.Mode == GameSource.Foreground &&
                    world.Window.StillForeground(), 3000)))
                throw new InvalidOperationException("请将鸣潮窗口切到前台后再次启动任务。");
            if (!RefreshFrame() || world.Window.Mode != GameSource.Foreground || !world.Window.StillForeground())
                throw new InvalidOperationException("请将鸣潮窗口切到前台后再次启动任务。");
            foregroundRoute = world.InputRouteVersion;
            targetSize = new Size(world.Frame.SourceWidth, world.Frame.SourceHeight);
            // A successful switch can change the visible client size. Re-read
            // the list from the now-confirmed foreground route before clicking.
            stages = WaitForList("切换到前台后无法识别拉海洛方块关卡列表。");
            stages = FirstPage(stages);

            var attempted = new HashSet<Tuple<string, int>>();
            var failures = new List<string>();
            int cleared = 0;
            for (int page = 0; page < 32; page++)
            {
                token.ThrowIfCancellationRequested();
                bool tried = false;
                foreach (Stage stage in stages)
                {
                    int completed;
                    if (!TryIncompleteDifficulty(stage.Status, out completed) ||
                        !attempted.Add(Tuple.Create(stage.Name, completed)))
                        continue;
                    tried = true;
                    try
                    {
                        Challenge(stage, completed);
                        cleared++;
                    }
                    catch (ChallengeFailed failure)
                    {
                        failures.Add(stage.Name);
                        RunLog.Warn(EventName, stage.Name + " · " + failure.Message);
                    }
                    if (IsDetails())
                        Click(.942, .06, 450);
                    stages = WaitForList("挑战结束后无法返回关卡列表。");
                    break;
                }
                if (tried)
                    continue;
                bool moved;
                stages = Scroll(stages, true, out moved);
                if (moved)
                    continue;
                FirstPage(stages);
                if (failures.Count > 0)
                    throw new InvalidOperationException("已通关 " + cleared + " 个难度，未完成 " + string.Join("、", failures));
                Report("已核对所有已解锁关卡，通关 " + cleared + " 个难度。");
                return;
            }
            throw new InvalidOperationException("扫描关卡列表达到上限。");
        }

        internal static bool TryIncompleteDifficulty(string status, out int completed)
        {
            completed = 0;
            Match match = StageProgress.Match(status ?? "");
            int total;
            return match.Success && TryDecimal(match.Groups[1].Value, out completed) &&
                TryDecimal(match.Groups[2].Value, out total) && total == 2 && completed < total;
        }

        private static bool TryDecimal(string text, out int value)
        {
            value = 0;
            foreach (char c in text)
            {
                int digit = CharUnicodeInfo.GetDecimalDigitValue(c);
                if (digit < 0 || value > (int.MaxValue - digit) / 10) return false;
                value = value * 10 + digit;
            }
            return text.Length != 0;
        }

        private List<Stage> WaitForList(string failure)
        {
            List<Stage> stages = null;
            if (WaitUntil(() => (stages = ReadStages()).Count > 0, 5000))
                return stages;
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException(failure);
        }

        private List<Stage> ReadStages()
        {
            var stages = new List<Stage>();
            if (!HasTitle())
                return stages;
            var lines = Read(.045, .18, .965, .79);
            return StagesFromLines(lines, world.Frame.SourceWidth, world.Frame.SourceHeight);
        }

        internal static List<Stage> StagesFromLines(IReadOnlyList<OcrLine> lines, int width, int height)
        {
            var stages = new List<Stage>();
            if (width <= 0 || height <= 0) return stages;
            foreach (OcrLine line in lines)
            {
                double center = Math.Round(line.Box.Top + line.Box.Height / 2.0) / height;
                if (!(center > .39 && center < .44 || center > .74 && center < .79) ||
                    !StageStatus.IsMatch(line.Text ?? ""))
                    continue;
                OcrLine title = lines.Where(other => (line.Box.Y - other.Box.Y) / (double)height > .18 &&
                    (line.Box.Y - other.Box.Y) / (double)height < .23 && Math.Abs(other.Box.X - line.Box.X) < .025 * width &&
                    !string.IsNullOrWhiteSpace(other.Text)).OrderBy(other => Math.Abs(other.Box.X - line.Box.X)).FirstOrDefault();
                if (title != null)
                    stages.Add(new Stage { Name = title.Text, Status = line.Text, Box = line.Box });
            }
            return stages.OrderBy(stage => stage.Box.X).ToList();
        }

        private bool HasTitle() { return !Find(.045, .04, .20, .09, "拉海洛方块").IsEmpty; }
        private bool IsDetails() { return HasTitle() && !Find(.68, .43, .82, .48, "关卡目标", true).IsEmpty; }
        private Rectangle StartButton() { return Find(.69, .89, .96, .95, "开始挑战", true); }
        private bool HasReward() { return !Find(.36, .216, .64, .29, @"^获\s*得$").IsEmpty; }

        private List<Stage> FirstPage(List<Stage> stages)
        {
            for (int index = 0; index < 12; index++)
            {
                bool moved;
                stages = Scroll(stages, false, out moved);
                if (!moved)
                    return stages;
            }
            throw new InvalidOperationException("无法定位到拉海洛方块关卡列表起点。");
        }

        private List<Stage> Scroll(List<Stage> stages, bool forward, out bool moved)
        {
            RequireForeground();
            Point[] path = ScrollPath(targetSize, forward);
            bool sent = world.Input.DragForeground(path[0], targetSize, world.Bounds, token, move =>
            {
                // PynputInteraction.swipe(650) in ok-script 3.0.13 actually
                // emits six 10ms steps, then moves to the final endpoint.
                for (int step = 0; step < path.Length - 1; step++)
                    if (!move(path[step]) || token.WaitHandle.WaitOne(10))
                        return false;
                return move(path[path.Length - 1]);
            }, preHoldMs: 100);
            RequireForeground();
            if (!sent)
                throw new InvalidOperationException("关卡列表滑动未完成。");
            Pause(550);
            List<Stage> after = WaitForList("滑动后未识别到关卡列表。");
            moved = StageListMoved(stages, after, world.Frame.SourceWidth);
            return after;
        }

        internal static Point[] ScrollPath(Size size, bool forward)
        {
            int left = (int)Math.Round(size.Width * .34), right = (int)Math.Round(size.Width * .81);
            int y = (int)Math.Round(size.Height * .49), start = forward ? right : left, end = forward ? left : right;
            var points = new Point[7];
            for (int index = 0; index < 6; index++)
                points[index] = new Point(start + (int)(index * ((end - start) / 6.0)), y);
            points[6] = new Point(end, y);
            return points;
        }

        internal static bool StageListMoved(IReadOnlyList<Stage> before, IReadOnlyList<Stage> after, int width)
        {
            return before.Count != after.Count || before.Zip(after, (previous, current) =>
                Math.Abs(previous.Box.X - current.Box.X) > .005 * width).Any(changed => changed);
        }

        private void Challenge(Stage stage, int completed)
        {
            if (!NavigateNative(() =>
                {
                    List<Stage> matches = ReadStages().Where(candidate => candidate.Name == stage.Name &&
                        stage.Box.IntersectsWith(candidate.Box)).ToList();
                    return matches.Count == 1 ? matches[0].Box : Rectangle.Empty;
                }, IsDetails))
                throw new InvalidOperationException("无法打开关卡「" + stage.Name + "」。");
            string difficulty = completed == 0 ? "简单" : "困难";
            Func<bool> selected = () => !Find(.68, .125, .96, .181, difficulty + "$").IsEmpty;
            if (!selected())
            {
                Click(.085, completed == 0 ? .189 : .276, 350);
                if (!WaitUntil(selected, 4000))
                    throw new ChallengeFailed("无法确认已选择" + difficulty + "难度。");
            }
            if (Find(.68, .48, .88, .55, "积分|奖章").IsEmpty)
                throw new ChallengeFailed("无法识别关卡目标。");
            Rectangle start = StartButton();
            if (start.IsEmpty)
                throw new ChallengeFailed("无法识别开始挑战按钮。");
            Report("开始挑战 " + stage.Name + " · " + difficulty);
            for (int attempt = 0; attempt < 2 && !start.IsEmpty; attempt++)
            {
                Click(start, 1300, 90);
                start = StartButton();
            }
            if (!start.IsEmpty)
                throw new ChallengeFailed("开始挑战按钮未响应。");
            try
            {
                if (!WaitUntil(ReadyBoard, 15000))
                    throw new ChallengeFailed("无法识别棋盘。");
                PlayBoard();
            }
            catch (ChallengeFailed)
            {
                LeaveChallenge();
                throw;
            }
            Click(.5, .88, 600);
            if (!WaitUntil(IsDetails, 5000))
                throw new InvalidOperationException("领取奖励后未返回关卡详情。");
            Report("已通关 " + stage.Name + " · " + difficulty);
        }

        private bool DismissHelp()
        {
            if (Find(.05, .28, .32, .35, "模式|方块").IsEmpty)
                return false;
            Rectangle confirm = Find(.41, .88, .59, .95, "确?认$");
            if (!confirm.IsEmpty)
                Click(confirm, 500);
            else if (!Find(.38, .90, .62, .96, "切换至最后一页").IsEmpty)
                Click(.75, .805, 500);
            return true;
        }

        private bool ReadyBoard()
        {
            if (DismissHelp())
                return false;
            if (!Find(.54, .39, .81, .47, "选择右侧方块").IsEmpty)
                Click(.863, .444, 500);
            else if (!Find(.28, .28, .72, .37, "方块旋转后填入空格").IsEmpty)
                Click(.50, .48, 500);
            else if (!Find(.035, .23, .37, .30, "方块消除后将获得积分").IsEmpty)
                Click(.16, .16, 500);
            else
                return Playable() != null;
            return false;
        }

        private void LeaveChallenge()
        {
            RequireForeground();
            if (IsDetails())
                return;
            Func<Rectangle> leaveButton = () => Find(.33, .45, .67, .52, "退出挑战|关卡进度").IsEmpty
                ? Rectangle.Empty : Find(.60, .59, .71, .67, "离开", true);
            Rectangle leave = leaveButton();
            if (leave.IsEmpty && HasTitle() && (RahaiVision.Read(world.Frame) != null ||
                HasScore()))
            {
                Click(.942, .06, 450);
                WaitUntil(() => !(leave = leaveButton()).IsEmpty, 5000);
            }
            if (leave.IsEmpty)
                throw new InvalidOperationException("无法确认退出挑战按钮。");
            if (!NavigateNative(leaveButton, IsDetails))
                throw new InvalidOperationException("退出挑战后未返回关卡详情。");
        }

        private bool NavigateNative(Func<Rectangle> findControl, Func<bool> destination)
        {
            Rectangle original = Rectangle.Empty;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                RequireForeground();
                if (destination()) return true;
                Rectangle button = findControl();
                if (button.IsEmpty || attempt != 0 && !original.IntersectsWith(button)) return false;
                if (attempt == 0) original = button;
                var center = new Point(button.Left + button.Width / 2, button.Top + button.Height / 2);
                if (SendNativeClick(center, 35, settleMs: 150, useWindowMessage: attempt != 0))
                    Pause(450);
                if (WaitUntil(destination, 5000)) return true;
            }
            return false;
        }

        private RahaiBoard Playable()
        {
            RahaiBoard board = RahaiVision.Read(world.Frame);
            return board != null && board.Pieces.Count > 0 ? board : null;
        }

        private void PlayBoard()
        {
            var rejected = new HashSet<Tuple<int, ulong>>();
            int misses = 0, stagnant = 0;
            ulong? lastMask = null;
            for (int step = 0; step < 160; step++)
            {
                RequireForeground();
                RahaiBoard board = null;
                if (!WaitUntil(() => (board = Playable()) != null, 2000))
                {
                    if (WaitUntil(HasReward, 3000))
                        return;
                    token.ThrowIfCancellationRequested();
                    if (!DismissHelp())
                        throw new ChallengeFailed("棋盘消失，未确认通关。");
                    WaitUntil(() => !DismissHelp(), 10000);
                    misses = 0;
                    rejected.Clear();
                    continue;
                }
                RahaiMove choice = RahaiSolver.Choose(board, rejected);
                if (choice == null)
                    throw new ChallengeFailed("剩余方块无合法落点。");
                if (!Place(board, choice))
                {
                    RequireForeground();
                    rejected.Add(Tuple.Create(choice.Slot, choice.Mask));
                    if (++misses >= 3)
                        throw new ChallengeFailed("连续三次无法匹配落点预览。");
                    Pause(350);
                    continue;
                }
                misses = 0;
                rejected.Clear();
                int lines;
                ulong expected = RahaiSolver.ClearLines(board.Mask | choice.Mask, board.Fixed, out lines);
                Pause(lines > 0 ? 950 : 380);
                RahaiBoard actual = null;
                bool matched = false;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    RequireForeground();
                    actual = RahaiVision.Read(world.Frame);
                    if (actual != null && actual.Mask == expected && actual.Fixed == board.Fixed)
                    {
                        matched = true;
                        break;
                    }
                    Pause(230);
                }
                if (!matched)
                {
                    if (lines > 0 && WaitUntil(HasReward, 3000))
                        return;
                    RunLog.Warn(EventName, "落块预测有差异，按实际棋盘继续。");
                }
                if (actual != null)
                {
                    stagnant = lastMask == actual.Mask && actual.Fixed == board.Fixed ? stagnant + 1 : 0;
                    if (stagnant >= 4)
                        throw new ChallengeFailed("连续四次落块后棋盘未变化。");
                    lastMask = actual.Mask;
                }
            }
            throw new ChallengeFailed("达到落块次数上限，未确认通关。");
        }

        private bool Place(RahaiBoard board, RahaiMove choice)
        {
            RahaiPoint source = board.Pieces.First(piece => piece.Slot == choice.Slot).Pickup;
            RahaiPoint drop = choice.Drop;
            return Drag(source, move =>
            {
                if (token.WaitHandle.WaitOne(100) || !move(new RahaiPoint(source.X - 16, source.Y)) ||
                    token.WaitHandle.WaitOne(120))
                    return false;
                for (int step = 1; step <= 8; step++)
                    if (!move(new RahaiPoint(source.X + (drop.X - source.X) * step / 8,
                        source.Y + (drop.Y - source.Y) * step / 8)) || token.WaitHandle.WaitOne(25))
                        return false;
                if (token.WaitHandle.WaitOne(150))
                    return false;
                Func<bool> rotate = () => world.Key('R', 80, requireForeground: true, route: foregroundRoute) &&
                    move(new RahaiPoint(drop.X + 5, drop.Y)) && !token.WaitHandle.WaitOne(130);
                for (int turns = 0; turns < choice.Turns; turns++)
                    if (!rotate())
                        return false;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (attempt > 0 && !rotate())
                        return false;
                    foreach (Point offset in PreviewOffsets)
                    {
                        if (!move(new RahaiPoint(drop.X + offset.X, drop.Y + offset.Y)) ||
                            token.WaitHandle.WaitOne(160))
                            return false;
                        if (RefreshFrame() && RahaiVision.MatchesPreview(world.Frame, board, choice))
                            return true;
                    }
                }
                return false;
            });
        }

        private bool Drag(RahaiPoint source, Func<Func<RahaiPoint, bool>, bool> finish)
        {
            RequireForeground();
            PixelFrame frame = world.Frame.FullResolution;
            return world.Input.DragForeground(RahaiVision.At(frame, source), new Size(frame.Width, frame.Height),
                world.Bounds, token, move => finish(point => move(RahaiVision.At(frame, point))), preHoldMs: 140);
        }

        private bool HasScore()
        {
            foreach (OcrLine line in Read(.09, .125, .235, .181))
            {
                Match match = Regex.Match(line.Text, @"(\d+)\s*[/／]\s*(\d+)");
                int goal;
                if (match.Success && TryDecimal(match.Groups[2].Value, out goal) && goal > 0) return true;
            }
            return false;
        }

        private List<OcrLine> Read(double left, double top, double right, double bottom)
        {
            // The framework's frame property returns its cached snapshot until
            // next_frame / sleep / wait_condition. Multiple ROIs in a single
            // decision must therefore describe the same observed UI.
            if (!world.Captured && !RefreshFrame()) return new List<OcrLine>();
            // BaseTask.ocr operates directly on the captured image, with rounded
            // origin and extent, no target-height scaling, and native OCR boxes.
            return ReadReferenceRoi(world.Ocr, world.Frame, left, top, right, bottom);
        }

        internal static List<OcrLine> ReadReferenceRoi(OcrReader ocr, PixelFrame frame, double left, double top, double right, double bottom)
        {
            PixelFrame source = frame.FullResolution;
            var area = new Rectangle((int)Math.Round(left * source.Width), (int)Math.Round(top * source.Height),
                (int)Math.Round((right - left) * source.Width), (int)Math.Round((bottom - top) * source.Height));
            return ReferenceLines(ocr.Read(source, area));
        }

        internal static List<OcrLine> ReferenceLines(IEnumerable<OcrLine> lines)
        {
            var result = new List<OcrLine>();
            foreach (OcrLine line in lines)
            {
                // ok-script 3.0.13's OCR task threshold is .2; its underlying
                // onnxocr 0.0.1 engine already rejects scores below .5. Do not
                // impose the old Rahai-specific .60 gate on normal text.
                if (line.Score < .5f) continue;
                Rectangle box = line.Box;
                if (line.NativeQuad != null && line.NativeQuad.Length >= 3)
                {
                    var first = line.NativeQuad[0];
                    var third = line.NativeQuad[2];
                    box = new Rectangle((int)Math.Round(first.X), (int)Math.Round(first.Y),
                        (int)Math.Round(third.X - first.X), (int)Math.Round(third.Y - first.Y));
                }
                if (box.Width <= 0 || box.Height <= 0) continue;
                // This task supports only zh_CN. The reference trims output and
                // only applies its traditional-to-simplified conversion on zh_TW/HK/MO.
                var item = new OcrLine { Box = box, Score = line.Score, Text = (line.RawText ?? line.Text ?? "").Trim() };
                int index = result.Count;
                while (index > 0 && CompareReferenceLines(result[index - 1], item) > 0) index--;
                result.Insert(index, item);
            }
            return result;
        }

        private static int CompareReferenceLines(OcrLine a, OcrLine b)
        {
            bool overlap = a.Box.Top <= b.Box.Bottom && a.Box.Bottom >= b.Box.Top;
            int compare = overlap ? a.Box.X.CompareTo(b.Box.X) : a.Box.Y.CompareTo(b.Box.Y);
            if (compare == 0) compare = overlap ? a.Box.Y.CompareTo(b.Box.Y) : a.Box.X.CompareTo(b.Box.X);
            if (compare == 0) compare = (int)(a.Score * 1000 - b.Score * 1000);
            if (compare == 0) compare = string.CompareOrdinal(a.Text, b.Text);
            return compare;
        }

        internal static Rectangle FindReferenceLine(IEnumerable<OcrLine> lines, string pattern, bool exact = false)
        {
            foreach (OcrLine line in lines)
                if (exact ? line.Text == pattern : Regex.IsMatch(line.Text, pattern))
                    return line.Box;
            return Rectangle.Empty;
        }

        private Rectangle Find(double left, double top, double right, double bottom, string pattern, bool exact = false)
        {
            return FindReferenceLine(Read(left, top, right, bottom), pattern, exact);
        }

        private void Click(Rectangle box, int delay, int hold = 20)
        {
            if (box.Width <= 0 || box.Height <= 0)
                throw new InvalidOperationException("无法确认活动按钮坐标。");
            ClickNative(new Point((int)Math.Round(box.X + box.Width / 2.0), (int)Math.Round(box.Y + box.Height / 2.0)), delay, hold);
        }

        private void Click(double x, double y, int delay)
        {
            RequireForeground();
            ClickNative(new Point((int)Math.Round(x * targetSize.Width), (int)Math.Round(y * targetSize.Height)), delay, 20);
        }

        private void ClickNative(Point point, int delay, int hold)
        {
            if (!SendNativeClick(point, hold))
                throw new InvalidOperationException("活动按钮点击未发送。");
            Pause(delay);
        }

        private bool SendNativeClick(Point point, int hold, int settleMs = 0, bool useWindowMessage = false)
        {
            RequireForeground();
            return point.X >= 0 && point.X < targetSize.Width && point.Y >= 0 && point.Y < targetSize.Height &&
                world.Input.Click(point, targetSize, world.Bounds, FarmMouse.Left, hold, token,
                    settleMs: settleMs, useWindowMessage: useWindowMessage);
        }

        private bool RefreshFrame()
        {
            token.ThrowIfCancellationRequested();
            bool captured = world.Tick(false, nativePixels: true);
            if (targetHandle != IntPtr.Zero && (world.Window.Handle != targetHandle || world.Window.ProcessId != targetProcess))
                throw new InvalidOperationException("拉海洛方块游戏实例已改变，请重新打开活动后启动。");
            if (foregroundRoute >= 0 && (world.InputRouteVersion != foregroundRoute ||
                world.Window.Mode != GameSource.Foreground || !world.Window.StillForeground()))
                throw new InvalidOperationException("拉海洛方块游戏前台路线已丢失，已停止发送输入。");
            if (!captured || !world.Frame.HasSourcePixels) return false;
            if (foregroundRoute >= 0 && targetSize != new Size(world.Frame.SourceWidth, world.Frame.SourceHeight))
                throw new InvalidOperationException("拉海洛方块游戏画面尺寸已改变，请重新启动。");
            return true;
        }

        private bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            do
            {
                if (RefreshFrame() && condition()) return true;
                if (token.WaitHandle.WaitOne(60)) token.ThrowIfCancellationRequested();
            }
            while (watch.ElapsedMilliseconds < timeoutMs);
            return false;
        }

        private void Pause(int delay)
        {
            if (token.WaitHandle.WaitOne(delay)) token.ThrowIfCancellationRequested();
            RefreshFrame();
        }

        private void RequireForeground()
        {
            if (!RefreshFrame() || world.Window.Mode != GameSource.Foreground || !world.Window.StillForeground())
                throw new InvalidOperationException("拉海洛方块需要保持鸣潮 Windows 客户端在前台。");
        }

        private void Report(string message)
        {
            RunLog.Info(EventName, message);
            progress.Report(message);
        }

        public void Dispose() { world.Dispose(); }
    }
}
