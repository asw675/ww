using System;
using System.Collections.Generic;
using System.Drawing;
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

        internal RahaiRunner(CombatSettings settings, CancellationToken token, IProgress<string> progress)
        {
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
        }

        internal void Run()
        {
            if (!world.Tick() || !RahaiVision.Supported(world.Frame))
                throw new InvalidOperationException("拉海洛方块需要 16:9 的 Windows 客户端。");
            List<Stage> stages = WaitForList("请先打开简体中文的「" + EventName + "」活动关卡列表。");
            if (world.Window.Mode != GameSource.Foreground && !world.TryRecoverForegroundCapture())
                RequireForeground();
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
            Match match = StageProgress.Match(OcrText.Compact(status));
            int total;
            return match.Success && int.TryParse(match.Groups[1].Value, out completed) &&
                int.TryParse(match.Groups[2].Value, out total) && total == 2 && completed < total;
        }

        private List<Stage> WaitForList(string failure)
        {
            List<Stage> stages = null;
            if (world.WaitUntil(() => (stages = ReadStages()).Count > 0, 5000))
                return stages;
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException(failure);
        }

        private List<Stage> ReadStages()
        {
            var stages = new List<Stage>();
            if (!HasTitle())
                return stages;
            var lines = world.Read(.045, .18, .965, .79);
            double width = world.Frame.Width, height = world.Frame.Height;
            foreach (OcrLine line in lines)
            {
                double center = (line.Box.Top + line.Box.Height / 2.0) / height;
                if (!(center > .39 && center < .44 || center > .74 && center < .79) ||
                    !StageStatus.IsMatch(line.Text ?? ""))
                    continue;
                OcrLine title = lines.Where(other => (line.Box.Y - other.Box.Y) / height > .18 &&
                    (line.Box.Y - other.Box.Y) / height < .23 && Math.Abs(other.Box.X - line.Box.X) < .025 * width &&
                    !string.IsNullOrWhiteSpace(other.Text)).OrderBy(other => Math.Abs(other.Box.X - line.Box.X)).FirstOrDefault();
                if (title != null)
                    stages.Add(new Stage { Name = OcrText.Compact(title.Text), Status = line.Text, Box = line.Box });
            }
            return stages.OrderBy(stage => stage.Box.X).ToList();
        }

        private bool HasTitle() { return !Find(.045, .04, .20, .09, "拉海洛方块").IsEmpty; }
        private bool IsDetails() { return HasTitle() && !Find(.68, .43, .82, .48, "关卡目标").IsEmpty; }
        private Rectangle StartButton() { return Find(.69, .89, .96, .95, "^开始挑战$"); }
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
            double start = forward ? .81 : .34, end = forward ? .34 : .81;
            bool sent = Drag(new PointF((float)(start * RahaiVision.Width), .49f * RahaiVision.Height), move =>
            {
                for (int step = 1; step <= 18; step++)
                {
                    if (!move(new PointF((float)((start + (end - start) * step / 18) * RahaiVision.Width),
                        .49f * RahaiVision.Height)) || token.WaitHandle.WaitOne(36))
                        return false;
                }
                return true;
            });
            RequireForeground();
            if (!sent)
                throw new InvalidOperationException("关卡列表滑动未完成。");
            world.Sleep(550);
            List<Stage> after = WaitForList("滑动后未识别到关卡列表。");
            moved = StageListMoved(stages, after, world.Frame.Width);
            return after;
        }

        internal static bool StageListMoved(IReadOnlyList<Stage> before, IReadOnlyList<Stage> after, int width)
        {
            return before.Count != after.Count || before.Zip(after, (previous, current) =>
                Math.Abs(previous.Box.X - current.Box.X) > .005 * width).Any(changed => changed);
        }

        private void Challenge(Stage stage, int completed)
        {
            Click(stage.Box, 450);
            if (!world.WaitUntil(IsDetails, 5000))
                throw new InvalidOperationException("无法打开关卡「" + stage.Name + "」。");
            string difficulty = completed == 0 ? "简单" : "困难";
            Func<bool> selected = () => !Find(.68, .125, .96, .181, difficulty + "$").IsEmpty;
            if (!selected())
            {
                Click(.085, completed == 0 ? .189 : .276, 350);
                if (!world.WaitUntil(selected, 4000))
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
                Click(start, 1300);
                start = StartButton();
            }
            if (!start.IsEmpty)
                throw new ChallengeFailed("开始挑战按钮未响应。");
            try
            {
                if (!world.WaitUntil(ReadyBoard, 15000))
                    throw new ChallengeFailed("无法识别棋盘。");
                PlayBoard();
            }
            catch (ChallengeFailed)
            {
                LeaveChallenge();
                throw;
            }
            Click(.5, .88, 600);
            if (!world.WaitUntil(IsDetails, 5000))
                throw new InvalidOperationException("领取奖励后未返回关卡详情。");
            Report("已通关 " + stage.Name + " · " + difficulty);
        }

        private bool DismissHelp()
        {
            RequireForeground();
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
                ? Rectangle.Empty : Find(.60, .59, .71, .67, "^离开$");
            Rectangle leave = leaveButton();
            if (leave.IsEmpty && HasTitle() && (RahaiVision.Read(world.Frame) != null ||
                !Find(.09, .125, .235, .181, @"\d+\s*[/／]\s*\d+").IsEmpty))
            {
                Click(.942, .06, 450);
                world.WaitUntil(() => !(leave = leaveButton()).IsEmpty, 5000);
            }
            if (leave.IsEmpty)
                throw new InvalidOperationException("无法确认退出挑战按钮。");
            Click(leave, 450);
            if (!world.WaitUntil(IsDetails, 5000))
                throw new InvalidOperationException("退出挑战后未返回关卡详情。");
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
                if (!world.WaitUntil(() => (board = Playable()) != null, 2000))
                {
                    if (world.WaitUntil(HasReward, 3000))
                        return;
                    token.ThrowIfCancellationRequested();
                    if (!DismissHelp())
                        throw new ChallengeFailed("棋盘消失，未确认通关。");
                    world.WaitUntil(() => !DismissHelp(), 10000);
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
                    world.Sleep(350);
                    continue;
                }
                misses = 0;
                rejected.Clear();
                int lines;
                ulong expected = RahaiSolver.ClearLines(board.Mask | choice.Mask, board.Fixed, out lines);
                world.Sleep(lines > 0 ? 950 : 380);
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
                    world.Sleep(230);
                }
                if (!matched)
                {
                    if (lines > 0 && world.WaitUntil(HasReward, 3000))
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
            PointF source = board.Pieces.First(piece => piece.Slot == choice.Slot).Pickup;
            PointF drop = choice.Drop;
            return Drag(source, move =>
            {
                if (token.WaitHandle.WaitOne(100) || !move(new PointF(source.X - 16, source.Y)) ||
                    token.WaitHandle.WaitOne(120))
                    return false;
                for (int step = 1; step <= 8; step++)
                    if (!move(new PointF(source.X + (drop.X - source.X) * step / 8,
                        source.Y + (drop.Y - source.Y) * step / 8)) || token.WaitHandle.WaitOne(25))
                        return false;
                if (token.WaitHandle.WaitOne(150))
                    return false;
                Func<bool> rotate = () => world.Key('R', 80, requireForeground: true) &&
                    move(new PointF(drop.X + 5, drop.Y)) && !token.WaitHandle.WaitOne(130);
                for (int turns = 0; turns < choice.Turns; turns++)
                    if (!rotate())
                        return false;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (attempt > 0 && !rotate())
                        return false;
                    foreach (Point offset in PreviewOffsets)
                    {
                        if (!move(new PointF(drop.X + offset.X, drop.Y + offset.Y)) ||
                            token.WaitHandle.WaitOne(160))
                            return false;
                        if (world.Tick(false) && RahaiVision.MatchesPreview(world.Frame, board, choice))
                            return true;
                    }
                }
                return false;
            });
        }

        private bool Drag(PointF source, Func<Func<PointF, bool>, bool> finish)
        {
            RequireForeground();
            PixelFrame frame = world.Frame;
            return world.Input.DragForeground(RahaiVision.At(frame, source), new Size(frame.Width, frame.Height),
                world.Bounds, token, move => finish(point => move(RahaiVision.At(frame, point))));
        }

        private Rectangle Find(double left, double top, double right, double bottom, string pattern)
        {
            foreach (OcrLine line in world.Read(left, top, right, bottom))
                if (line.Score >= .60 && Regex.IsMatch((line.Text ?? "").Trim(), pattern))
                    return line.Box;
            return Rectangle.Empty;
        }

        private void Click(Rectangle box, int delay)
        {
            RequireForeground();
            if (!world.Click(box, 90))
                throw new InvalidOperationException("活动按钮点击未发送。");
            world.Sleep(delay);
        }

        private void Click(double x, double y, int delay)
        {
            RequireForeground();
            if (!world.Click(x, y, 90))
                throw new InvalidOperationException("活动按钮点击未发送。");
            world.Sleep(delay);
        }

        private void RequireForeground()
        {
            token.ThrowIfCancellationRequested();
            if (!world.Tick(false) || world.Window.Mode != GameSource.Foreground)
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
