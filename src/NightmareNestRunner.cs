using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    public sealed class NightmareOptions
    {
        public bool TacetNest { get; set; }
        public bool Purification { get; set; }
        public bool CaptureOnce { get; set; }

        public static NightmareOptions Defaults()
        {
            return new NightmareOptions { TacetNest = true, Purification = true };
        }

        public void Validate()
        {
            if (!TacetNest && !Purification)
                throw new System.IO.InvalidDataException("请至少选择一种梦魇巢穴。");
        }
    }

    internal enum NestTab { TacetNest, TacetScroll, Purification, PurificationScroll }
    internal enum EchoPrompt { None, Selected, Below }

    internal sealed class PickupReceiptCheck
    {
        private bool ready;
        private int visibleFrames, clearFrames;

        internal PickupReceiptCheck(bool alreadyVisible) { ready = !alreadyVisible; }

        internal bool Observe(bool visible)
        {
            if (!visible)
            {
                visibleFrames = 0;
                if (++clearFrames >= 2)
                    ready = true;
                return false;
            }
            clearFrames = 0;
            return ready && ++visibleFrames >= 2;
        }
    }

    internal sealed class NestTarget
    {
        internal Point Point;
        internal string Key;
        internal NestTab Tab;
        internal bool Named;
        internal int RevealScrolls;
        internal int Defeated, Total;
    }

    internal static class NestMath
    {
        private static readonly int[] Nests = { 24, 36, 48, 41 };
        private static readonly Regex Count = new Regex(@"(?<!\d)(\d{1,2})\s*[/／]\s*(\d{1,2})(?!\d)");
        private static readonly Regex NestTitle = new Regex(@"(?:[残殘]象聚落|(?<purification>[梦夢][魇魘魔]聚落)|Tacet Discord Nest)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> VisitLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "前往", "前往挑战", "前往挑戰", "直接挑战", "直接挑戰", "Go", "Proceed" };
        private static readonly HashSet<string> ChallengeLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "开启挑战", "開啟挑戰", "开始挑战", "開始挑戰", "StartChallenge", "BeginChallenge" };

        internal static bool TryIncomplete(string text, out int denominator)
        {
            foreach (Match match in Count.Matches(text ?? string.Empty))
            {
                denominator = int.Parse(match.Groups[2].Value);
                if (match.Groups[1].Value == "0" && Array.IndexOf(Nests, denominator) >= 0)
                    return true;
            }
            denominator = 0;
            return false;
        }

        internal static bool TryProgress(string text, out int defeated, out int denominator)
        {
            defeated = denominator = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (Match match in Count.Matches(text))
            {
                int numerator = int.Parse(match.Groups[1].Value);
                int total = int.Parse(match.Groups[2].Value);
                if (numerator > total || Array.IndexOf(Nests, total) < 0)
                    continue;
                defeated = numerator;
                denominator = total;
                return true;
            }
            return false;
        }

        internal static string CacheKey(NestTab tab, int denominator, Rectangle box, int frameHeight)
        {
            int slot = (int)Math.Round((box.Y + box.Height / 2.0) / Math.Max(1, frameHeight) / .02);
            return tab + ":" + denominator + ":" + slot;
        }

        internal static string TargetKey(List<OcrLine> lines, NestTab tab, int denominator, Rectangle count,
            int frameHeight, out bool named, int revealScrolls = 0)
        {
            OcrLine title = null;
            Match suffix = null;
            foreach (OcrLine line in lines)
            {
                int gap = count.Top - line.Box.Bottom;
                if (gap < 0 || gap > count.Height * 2 || Math.Abs(line.Box.Left - count.Left) > count.Height * 2 ||
                    line.Score < .75f || string.IsNullOrEmpty(line.Text))
                    continue;
                Match match = NestTitle.Match(line.Text);
                if (match.Success && (title == null || line.Box.Bottom > title.Box.Bottom))
                {
                    title = line;
                    suffix = match;
                }
            }
            named = title != null;
            if (named)
            {
                bool purificationTitle = suffix.Groups["purification"].Success;
                bool purificationTab = tab == NestTab.Purification || tab == NestTab.PurificationScroll;
                if (purificationTitle != purificationTab)
                    return null;
                if (purificationTitle)
                    return "Purification:" + Regex.Replace(title.Text.Substring(0, suffix.Index), @"\s+", "") + "梦魇聚落";
                return "TacetNest:" + Regex.Replace(title.Text, @"\s+", "").Replace('殘', '残');
            }

            string key = CacheKey(tab, denominator, count, frameHeight);
            return revealScrolls == 0 ? key : key + ":scroll:" + revealScrolls;
        }

        internal static bool TryVisitPoint(List<OcrLine> lines, Rectangle count, int frameWidth, out Point point)
        {
            point = Point.Empty;
            double expected = count.Y - count.Height * .35;
            double best = count.Height * 1.5;
            foreach (OcrLine line in lines)
            {
                Point center = Center(line.Box);
                if (center.X < frameWidth * .78 || line.Box.Height < count.Height * .6 ||
                    line.Score < .65f || !MatchesText(line.Text, VisitLabels))
                    continue;
                double distance = Math.Abs(center.Y - expected);
                if (distance >= best)
                    continue;
                best = distance;
                point = center;
            }
            return !point.IsEmpty;
        }

        internal static NestTarget FindTarget(List<OcrLine> lines, NestTab tab, int width, int height,
            HashSet<string> excludedNests, out bool missingButton, int revealScrolls = 0)
        {
            missingButton = false;
            foreach (OcrLine line in lines)
            {
                int denominator;
                if (!TryIncomplete(line.Text, out denominator))
                    continue;
                bool named;
                string key = TargetKey(lines, tab, denominator, line.Box, height, out named, revealScrolls);
                if (excludedNests.Contains(key))
                    continue;
                Point point;
                if (key != null && TryVisitPoint(lines, line.Box, width, out point))
                    return new NestTarget { Tab = tab, Key = key, Point = point, Named = named,
                        RevealScrolls = revealScrolls, Defeated = 0, Total = denominator };
                missingButton = true;
            }
            return null;
        }

        internal static NestTarget ReadTargetProgress(List<OcrLine> lines, NestTarget target,
            int width, int height, int revealScrolls)
        {
            NestTarget current = null;
            foreach (OcrLine line in lines)
            {
                int defeated, total;
                bool named;
                if (line.Score < .75f || !TryProgress(line.Text, out defeated, out total) || total != target.Total ||
                    !target.Named && revealScrolls != target.RevealScrolls ||
                    TargetKey(lines, target.Tab, total, line.Box, height, out named, revealScrolls) != target.Key)
                    continue;
                Point point;
                TryVisitPoint(lines, line.Box, width, out point);
                if (!target.Named && (point.IsEmpty || Math.Abs(point.Y - target.Point.Y) > line.Box.Height))
                    continue;
                if (current != null)
                    return null;
                current = new NestTarget { Key = target.Key, Tab = target.Tab, Named = named, Point = point,
                    Defeated = defeated, Total = total, RevealScrolls = revealScrolls };
            }
            return current;
        }

        internal static bool TryChallengeText(List<OcrLine> lines, out Rectangle button)
        {
            foreach (OcrLine line in lines)
            {
                if (line.Score >= .65f && !line.Box.IsEmpty && MatchesText(line.Text, ChallengeLabels))
                {
                    button = line.Box;
                    return true;
                }
            }
            button = Rectangle.Empty;
            return false;
        }

        internal static bool IsAbsorbText(string text)
        {
            return !string.IsNullOrEmpty(text) && (text.IndexOf("吸收", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("Absorb", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static EchoPrompt ReadEchoPrompt(PixelFrame frame, FarmUi ui, FarmVision vision, OcrReader ocr)
        {
            if (ui == null || ui.Claim || ui.Death || ui.Book)
                return EchoPrompt.None;
            Rectangle key = ui.FBox;
            if (ui.FPrompt && !key.IsEmpty && ui.Absorb)
                return EchoPrompt.Selected;
            if (ocr == null)
                return EchoPrompt.None;
            if (key.IsEmpty && !vision.FindPickupKey(frame, out key))
                return EchoPrompt.None;

            var area = new Rectangle(key.Right, key.Top - key.Height, key.Width * 11, key.Height * 5);
            List<OcrLine> lines;
            try { lines = ocr.Read(frame, area); }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { return EchoPrompt.None; }
            EchoPrompt match = EchoPrompt.None;
            foreach (OcrLine line in lines)
            {
                if (line.Score < .60f || !IsAbsorbText(line.Text))
                    continue;
                if (line.Box.Top <= key.Top + key.Height / 2)
                    return EchoPrompt.Selected;
                match = EchoPrompt.Below;
            }
            return match;
        }

        private static Point Center(Rectangle box)
        {
            return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        }

        private static bool MatchesText(string text, HashSet<string> labels)
        {
            return !string.IsNullOrWhiteSpace(text) && labels.Contains(Regex.Replace(text, @"\s+", ""));
        }
    }

    internal sealed class NightmareReport
    {
        internal string State;
        internal int Nests;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Visits every incomplete nightmare nest in the guidebook: teleport, fight,
    // collect the echo, and skip unreachable nests after repeated deaths.
    internal sealed class NightmareNestRunner : IDisposable
    {
        private const string LogArea = "Nightmare";
        private const int MaxRevealScrolls = 3;
        private const int NextWaveWaitMs = 1000;
        private const string TeamStart = "team_start_challenge";
        private static readonly string[] TravelFeatures = { "fast_travel_custom", "gray_teleport", "remove_custom" };
        private static readonly string[] TravelConfirmFeatures =
            { "confirm_btn_hcenter_vcenter", "confirm_btn_highlight_hcenter_vcenter" };
        private static readonly string[] NestEntryFeatures =
            { "fast_travel_custom", "gray_teleport", "remove_custom", TeamStart };
        private readonly GuidebookNavigation guide;
        private enum TravelView { Unknown, Button, Confirm, Guidebook, Terminal, World }
        private readonly CombatSettings settings;
        private readonly NightmareOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly bool ownsWorld;
        private readonly IProgress<NightmareReport> progress;
        private readonly Action<string, PixelFrame> recordEvidence;
        private readonly List<NestTab> queue = new List<NestTab>();
        private readonly HashSet<string> excludedNests = new HashSet<string>();
        private readonly HashSet<string> travelSkips = new HashSet<string>();
        private readonly HashSet<string> retriedStalledNests = new HashSet<string>();
        private readonly Dictionary<string, int> deaths = new Dictionary<string, int>();
        private EchoDetector detector;
        private NestTarget pendingNest;
        private NestTab? visibleBookPage;
        private int revealScrolls;
        private readonly Stopwatch pickupClock = Stopwatch.StartNew();
        private long nextPickupOcr;
        private EchoPrompt lastEchoPrompt;
        internal bool Failed { get; private set; }
        internal bool CaptureConfirmed { get { return captureDone; } }
        internal string Failure { get; private set; }
        internal string CompletionIssue { get; private set; }
        private int nests, completedFights, noEntryNests, pagesWithoutButtons, unconfirmedPickupNests;
        private int exhaustedDeaths, confirmedReceipts;
        private bool captureDone;

        internal NightmareNestRunner(CombatSettings settings, NightmareOptions options, CancellationToken token,
            IProgress<NightmareReport> progress, Action<string, PixelFrame> recordEvidence = null, WorldSession session = null)
        {
            this.settings = settings;
            this.options = options;
            this.token = token;
            this.progress = progress;
            this.recordEvidence = recordEvidence;
            ownsWorld = session == null;
            world = session ?? new WorldSession(settings, token);
            guide = new GuidebookNavigation(world, value => Report(value));
            BuildQueue();
            try
            {
                if (System.IO.File.Exists(EchoModel.ModelPath))
                    detector = new EchoDetector(EchoModel.ModelPath);
            }
            catch (Exception error)
            {
                detector = null;
                RunLog.Error(LogArea, "无法加载声骸检测模型，继续使用提示和走位搜索", error);
            }
        }

        private void BuildQueue()
        {
            if (options.TacetNest)
            {
                queue.Add(NestTab.TacetNest);
                queue.Add(NestTab.TacetScroll);
            }
            if (options.Purification)
            {
                queue.Add(NestTab.Purification);
                queue.Add(NestTab.PurificationScroll);
            }
        }

        internal void Run()
        {
            RunLog.Info(LogArea, string.Format("启动巢穴扫描。声骸巢穴={0}, 梦魇净化={1}, 单次捕获={2}, 声骸检测模型就绪={3}",
                options.TacetNest, options.Purification, options.CaptureOnce, detector != null));
            if (world.Tick() && world.VerifiedWorld && (world.InRealm || HasEnemies))
            {
                bool team = world.InRealm;
                RunLog.Info(LogArea, "启动时已在副本或遇敌，先处理当前战斗。副本=" + team);
                if (!FightCurrentNestAndExit(null, team))
                    return;
            }
            while (!captureDone && !token.IsCancellationRequested)
            {
                NestTarget target = pendingNest ?? NextNest();
                pendingNest = null;
                if (target == null)
                    break;
                Report("处理巢穴 " + (nests + 1) + " · 已击败 " + target.Defeated + "/" + target.Total);
                bool complete = FightNest(target);
                if (Failed)
                    return;
                if (complete)
                    nests++;
            }
            if (Failed || token.IsCancellationRequested)
            {
                if (token.IsCancellationRequested)
                    RunLog.Info(LogArea, "巢穴扫描已取消，已处理 " + nests + " 处");
                return;
            }
            CompletionIssue = BuildCompletionIssue();
            RecordFrame("nightmare-scan-end");
            if (nests == 0 && (noEntryNests > 0 || pagesWithoutButtons > 0))
            {
                Fail("未完成巢穴处理：" + CompletionIssue + "。");
                return;
            }
            if (!EnsureWorld())
            {
                if (!token.IsCancellationRequested)
                    Fail("巢穴扫描结束后未确认返回大世界。");
                return;
            }
            string final = captureDone ? "已确认获取声骸，任务结束" : "巢穴扫描结束";
            if (CompletionIssue != null)
                final += "，未完成核验：" + CompletionIssue;
            RunLog.Info(LogArea, string.Format("扫描统计。已处理={0}, 完成战斗={1}, 获取通知={2}, 无前往按钮的页面={3}",
                nests, completedFights, confirmedReceipts, pagesWithoutButtons));
            Report(final);
            progress.Report(new NightmareReport
            {
                State = final, Nests = nests, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private string BuildCompletionIssue()
        {
            if (options.CaptureOnce && captureDone)
                return null;
            var issues = new List<string>();
            if (options.CaptureOnce)
                issues.Add("未识别到新的声骸获取通知");
            if (pagesWithoutButtons > 0)
                issues.Add(pagesWithoutButtons + " 个图鉴视图还有目标未确认挑战入口");
            if (noEntryNests > 0)
                issues.Add(noEntryNests + " 处未确认入场");
            if (travelSkips.Count > 0)
                issues.Add(travelSkips.Count + " 处未确认传送到达");
            if (exhaustedDeaths > 0)
                issues.Add(exhaustedDeaths + " 处因连续倒下未完成");
            if (unconfirmedPickupNests > 0)
                issues.Add(unconfirmedPickupNests + " 处有轮次未确认吸收");
            return issues.Count == 0 ? null : string.Join("、", issues);
        }

        private void RecordFrame(string stage)
        {
            if (recordEvidence != null && world.Captured)
                recordEvidence(stage, world.Frame);
        }

        private NestTarget NextNest()
        {
            if (queue.Count == 0 || !OpenBook())
                return null;
            while (queue.Count > 0)
            {
                NestTab tab = queue[0];
                if (!SelectTab(tab))
                    return null;
                bool sawBlockedRows = false;
                bool missingButton = false;
                while (!token.IsCancellationRequested)
                {
                    var lines = world.Read(0.35, 0.25, 1, 0.96);
                    if (lines.Count == 0 && !token.IsCancellationRequested)
                    {
                        RunLog.Warn(LogArea, "图鉴 " + tab + " 首次 OCR 无结果，等待画面稳定后重读");
                        world.Sleep(700);
                        lines = world.Read(0.35, 0.25, 1, 0.96);
                    }
                    if (token.IsCancellationRequested)
                        return null;
                    if (lines.Count == 0 && !sawBlockedRows)
                    {
                        Fail("未读取到图鉴列表文字。请确认游戏窗口可用，并已解锁对应页签。");
                        return null;
                    }
                    NestTarget target = NestMath.FindTarget(lines, tab, world.Frame.Width, world.Frame.Height,
                        excludedNests, out missingButton, revealScrolls);
                    if (target != null)
                    {
                        RunLog.Info(LogArea, string.Format(
                            "图鉴 {0} 选中候选巢穴。入口按钮={1}, 使用名称去重={2}, OCR 行数={3}, 补扫滚动={4}",
                            tab, target.Point, target.Named, lines.Count, revealScrolls));
                        Report("发现未完成巢穴");
                        return target;
                    }
                    if (!missingButton)
                        break;
                    if (!sawBlockedRows)
                        RecordFrame("nightmare-" + tab + "-blocked-before-scroll");
                    sawBlockedRows = true;
                    if (!RevealMoreRows(lines))
                        break;
                }
                if (Failed || token.IsCancellationRequested)
                    return null;
                if (missingButton)
                {
                    pagesWithoutButtons++;
                    RecordFrame("nightmare-" + tab + "-blocked-after-scroll");
                    RunLog.Warn(LogArea, "图鉴 " + tab + " 补扫结束，仍有未确认挑战入口的目标");
                }
                else
                    RunLog.Info(LogArea, "图鉴 " + tab + " 没有其他可尝试的巢穴");
                queue.RemoveAt(0);
            }
            return null;
        }

        private bool RevealMoreRows(List<OcrLine> before = null)
        {
            Rectangle tab;
            bool selected;
            if (revealScrolls >= MaxRevealScrolls || token.IsCancellationRequested ||
                !guide.FindBookTab(out tab, out selected) || !selected || EntryAlreadyVisible())
                return false;
            int route = world.InputRouteVersion;
            if (before == null)
                before = world.Read(.35, .25, 1, .96);
            if (before.Count == 0 || !guide.FindBookTab(out tab, out selected) || !selected ||
                world.InputRouteVersion != route || EntryAlreadyVisible())
                return false;
            var size = new Size(world.Frame.Width, world.Frame.Height);
            var point = new Point((int)(size.Width * .67), (int)(size.Height * .64));
            if (!world.Input.Scroll(-1, point, size, world.Bounds, token))
                return false;
            world.Sleep(800);
            if (token.IsCancellationRequested)
                return false;
            if (!guide.FindBookTab(out tab, out selected) || !selected || EntryAlreadyVisible())
            {
                Fail("补扫滚动后未能确认 Boss 图鉴列表。");
                return false;
            }
            var after = world.Read(.35, .25, 1, .96);
            if (after.Count == 0 || !guide.FindBookTab(out tab, out selected) || !selected ||
                world.InputRouteVersion != route ||
                size.Width != world.Frame.Width || size.Height != world.Frame.Height ||
                !ProgressRowsMoved(before, after))
            {
                RunLog.Warn(LogArea, "补扫滚动已投递，未从前后画面确认进度行移动");
                return false;
            }
            revealScrolls++;
            RunLog.Info(LogArea, "确认图鉴行已移动，补扫位置=" + revealScrolls);
            return true;
        }

        private static bool ProgressRowsMoved(List<OcrLine> before, List<OcrLine> after)
        {
            var previous = ProgressRows(before);
            var current = ProgressRows(after);
            if (previous.Count != current.Count)
                return true;
            for (int index = 0; index < previous.Count; index++)
            {
                Point left = previous[index], right = current[index];
                if (left.X != right.X || Math.Abs(left.Y - right.Y) > 8)
                    return true;
            }
            return false;
        }

        private static List<Point> ProgressRows(List<OcrLine> lines)
        {
            var rows = new List<Point>();
            foreach (OcrLine line in lines)
            {
                int defeated, denominator;
                if (NestMath.TryProgress(line.Text, out defeated, out denominator))
                    rows.Add(new Point(denominator, line.Box.Top + line.Box.Height / 2));
            }
            return rows;
        }

        private bool OpenBook()
        {
            bool opened = guide.Open();
            if (guide.ChangedPage) visibleBookPage = null;
            if (!opened && !token.IsCancellationRequested) Fail(guide.Failure);
            return opened;
        }

        private bool SelectTab(NestTab tab)
        {
            if (visibleBookPage == tab)
                return true;
            bool purification = tab == NestTab.Purification || tab == NestTab.PurificationScroll;
            bool scrolled = tab == NestTab.PurificationScroll || tab == NestTab.TacetScroll;
            Report("扫描图鉴 · " + (purification ? "梦魇净化" : "声骸巢穴") + (scrolled ? " · 滚动页" : ""));
            world.Sleep(400);
            if ((!purification || ClickBookControl(.356, .882, 2000)) &&
                ClickBookControl(.24, purification ? .86 : .83, 2000) &&
                (!scrolled || ClickBookControl(purification ? 3737.0 / 3840 : .973, purification ? .54 : .8806, 1000)))
            {
                visibleBookPage = tab;
                revealScrolls = 0;
                return true;
            }
            visibleBookPage = null;
            Fail("未能操作图鉴页签。请确认游戏窗口可用。");
            return false;
        }

        private bool ClickBookControl(double x, double y, int settleMs)
        {
            if (!world.ClickMenuButton(x, y))
                return false;
            world.Sleep(settleMs);
            return !token.IsCancellationRequested;
        }

        private bool WaitScreen(string[] labels, int timeoutMs, out string feature, out Rectangle box)
        {
            string label = null;
            Rectangle match = Rectangle.Empty;
            bool found = world.WaitUntil(() => world.Vision.BestMatch(world.Frame, labels, 20, .72, .70,
                out label, out match), timeoutMs);
            feature = label;
            box = match;
            return found;
        }

        internal static bool FindNestEntry(FarmVision vision, PixelFrame frame, out string feature, out Rectangle box)
        {
            return vision.BestMatch(frame, NestEntryFeatures, 20, .72, .70, out feature, out box);
        }

        private bool WaitNestEntry(out string feature, out Rectangle button)
        {
            const int timeoutMs = 10000;
            string label = null;
            Rectangle match = Rectangle.Empty;
            var watch = Stopwatch.StartNew();
            long nextOcr = 0;
            bool matchedByOcr = false;
            world.Sleep(2000);
            bool found = world.WaitUntil(delegate
            {
                if (FindNestEntry(world.Vision, world.Frame, out label, out match))
                    return true;
                if (watch.ElapsedMilliseconds < nextOcr || world.TeamHud)
                    return false;
                nextOcr = watch.ElapsedMilliseconds + 500;
                Rectangle tab;
                bool selected;
                if (world.Vision.FindGuidebookBoss(world.Frame, out tab, out selected))
                    return false;
                if (!NestMath.TryChallengeText(world.Read(.63, .83, 1, .99), out match))
                    return false;
                label = TeamStart;
                matchedByOcr = true;
                return true;
            }, Math.Max(0, timeoutMs - (int)watch.ElapsedMilliseconds));
            feature = label;
            button = match;
            if (found)
                RunLog.Info(LogArea, string.Format("确认巢穴入场界面。按钮类型={0}, 识别方式={1}, 区域={2}, 等待={3}ms",
                    feature, matchedByOcr ? "OCR" : "模板", button, watch.ElapsedMilliseconds));
            else if (!token.IsCancellationRequested)
                RunLog.Warn(LogArea, "等待巢穴入场界面超时 " + timeoutMs + "ms");
            return found;
        }

        private bool ClickCurrentVisit(NestTarget target, bool retry, bool allowCompleted = false)
        {
            Rectangle tab;
            bool selected;
            if (token.IsCancellationRequested)
                return false;
            bool listReady = retry ? WaitForStableBossList() : guide.FindBookTab(out tab, out selected) && selected;
            if (!listReady)
            {
                RunLog.Warn(LogArea, "定位挑战入口时，未确认已选中的 Boss 图鉴列表");
                return false;
            }

            int route = world.InputRouteVersion;
            NestTarget observed = ReadVisibleNest(target);
            if (observed != null && !observed.Point.IsEmpty && (allowCompleted || observed.Defeated < observed.Total))
            {
                if (!guide.FindBookTab(out tab, out selected) || !selected || world.InputRouteVersion != route ||
                    retry && EntryAlreadyVisible())
                {
                    RunLog.Warn(LogArea, "定位挑战入口后未能再次确认目标列表和输入模式，本次点击作废");
                    return false;
                }
                if (retry)
                    Report("仍在图鉴，重试目标行的挑战入口");
                RunLog.Info(LogArea, string.Format(
                    "确认图鉴 {0} 的挑战入口。目标={1}, 初始位置={2}, 当前坐标={3}, 重试={4}",
                    target.Tab, target.Key, target.Point, observed.Point, retry));
                RecordFrame(retry ? "nightmare-visit-retry" : "nightmare-visit-ready");
                return world.ClickMenuButton(observed.Point, retry);
            }
            RunLog.Warn(LogArea, "图鉴 " + target.Tab + " 未重新定位到同一个巢穴的挑战入口");
            return false;
        }

        private NestTarget ReadVisibleNest(NestTarget target, List<OcrLine> lines = null)
        {
            lines = lines ?? world.Read(.35, .25, 1, .96);
            return world.Captured ? NestMath.ReadTargetProgress(lines, target, world.Frame.Width,
                world.Frame.Height, revealScrolls) : null;
        }

        private bool WaitForStableBossList()
        {
            int matched = 0, route = world.InputRouteVersion;
            return world.WaitUntil(delegate
            {
                Rectangle tab;
                bool selected;
                if (world.InputRouteVersion != route ||
                    !world.Vision.FindGuidebookBoss(world.Frame, out tab, out selected) || !selected ||
                    EntryAlreadyVisible())
                {
                    route = world.InputRouteVersion;
                    matched = 0;
                    return false;
                }
                return ++matched >= 2;
            }, 800);
        }

        private bool EntryAlreadyVisible()
        {
            string feature;
            Rectangle button;
            return FindNestEntry(world.Vision, world.Frame, out feature, out button);
        }

        private bool FightNest(NestTarget target)
        {
            Report("等待队伍或传送界面");
            string feature = null;
            Rectangle button = Rectangle.Empty;
            bool clickSent = ClickCurrentVisit(target, retry: false);
            bool entered = clickSent && WaitNestEntry(out feature, out button);
            if (!entered && !token.IsCancellationRequested)
            {
                if (ClickCurrentVisit(target, retry: true))
                {
                    clickSent = true;
                    entered = WaitNestEntry(out feature, out button);
                }
                else if (world.Tick() && FindNestEntry(world.Vision, world.Frame, out feature, out button))
                {
                    entered = true;
                    RunLog.Info(LogArea, "重试前已识别到巢穴入场按钮。按钮类型=" + feature);
                }
            }
            if (!entered)
            {
                if (token.IsCancellationRequested)
                    return false;
                Rectangle tab;
                bool selected;
                if (guide.FindBookTab(out tab, out selected) && selected)
                {
                    excludedNests.Add(target.Key);
                    noEntryNests++;
                    RecordFrame("nightmare-entry-unconfirmed");
                    Report((clickSent ? "已发送挑战入口点击，未确认入场" : "未发出挑战入口点击") +
                        "，仍在图鉴，尝试下一个巢穴", warning: true);
                }
                else
                    Fail(clickSent ? "点击后未识别到开始挑战或传送按钮。" : "未确认目标行的挑战入口。");
                return false;
            }
            return FightFromEntry(target, feature, button, reopenBook: true) &&
                (captureDone || VerifyNestProgress(target));
        }

        internal bool RunSelectedNest(string name, Rectangle entry)
        {
            string feature;
            Rectangle button;
            if (!world.ClickMenuButton(entry) || !WaitNestEntry(out feature, out button))
            {
                if (!token.IsCancellationRequested)
                    Fail("点击培养目标的巢穴入口后未识别到入场按钮。");
                return false;
            }
            var target = new NestTarget { Key = "培养目标/" + name, Named = true, Tab = NestTab.Purification };
            return FightFromEntry(target, feature, button);
        }

        private bool FightFromEntry(NestTarget target, string feature, Rectangle button, bool reopenBook = false)
        {
            visibleBookPage = null;
            bool team = feature == TeamStart;
            if (team)
            {
                Report("开始队伍挑战");
                if (!TeamChallenge(button))
                    return false;
                Report("等待队伍挑战加载");
                if (!WaitTeam(120000))
                {
                    Fail("队伍挑战加载超时，未识别到队伍界面。");
                    return false;
                }
                RunLog.Info(LogArea, "确认队伍挑战已加载");
            }
            else
            {
                if (!Travel(target, feature, button))
                    return false;
                for (int i = 0; i < 3 && !token.IsCancellationRequested; i++)
                {
                    world.Tick();
                    if (HasEnemies || world.Ui == null || !world.Ui.FPrompt)
                        break;
                    RunLog.Info(LogArea, "传送后识别到交互提示，尝试进入巢穴，第 " + (i + 1) + " 次");
                    world.Key('F');
                    world.Sleep(1200);
                    WaitTeam(40000);
                }
                world.WaitUntil(() => HasEnemies, 1500);
            }

            return FightCurrentNestAndExit(target, team, reopenBook);
        }

        private bool FightCurrentNestAndExit(NestTarget target, bool team, bool reopenBook = false)
        {
            world.AllowShortEnemyBars = false;
            while (FightAndCollect(target, team, reopenBook))
            {
                if (!captureDone)
                {
                    if (!world.Tick() || !world.VerifiedWorld)
                    {
                        if (!token.IsCancellationRequested)
                            Fail("离开巢穴前未能确认当前游戏画面。");
                        return false;
                    }
                    if (HasEnemies)
                    {
                        RunLog.Info(LogArea, "离开巢穴前出现敌人，继续战斗");
                        continue;
                    }
                }
                return !team || LeaveTeamNest();
            }
            return false;
        }

        private bool FightAndCollect(NestTarget target, bool team, bool reopenBook)
        {
            int approach = team || target == null || world.AllowShortEnemyBars ? 0 : 10000;
            int standAloneDeaths = 0;
            bool fought = false, retriedPickup = false, unconfirmedPickup = false;
            while (!token.IsCancellationRequested)
            {
                Report(approach > 0 ? "接近巢穴并尝试锁敌" : "等待锁定巢穴敌人");
                int enemyWait = fought ? NextWaveWaitMs : 10000;
                RunLog.Info(LogArea, string.Format("探测巢穴敌人。队伍挑战={0}, 已完成一波={1}, 接近时限={2}ms, 搜敌时限={3}ms",
                    team, fought, approach, enemyWait));
                CombatOutcome outcome = CombatRunner.Engage(world, settings, token, 300000, enemyWait,
                    seekTargets: true, approachMs: approach, onCombatStarted: () => Report("战斗中"),
                    captureDuringCombat: options.CaptureOnce ? WatchCombatCapture() : null);
                approach = 0;
                RunLog.Info(LogArea, "巢穴战斗结果 " + outcome);
                if (token.IsCancellationRequested)
                    return false;
                if (outcome == CombatOutcome.Captured)
                {
                    captureDone = true;
                    Report("战斗中已捕获声骸，结束梦魇巢穴任务");
                    break;
                }
                if (outcome == CombatOutcome.NoCombat)
                {
                    if (world.Tick() && HasEnemies)
                    {
                        RunLog.Info(LogArea, "离开前重新发现敌人，继续当前巢穴");
                        continue;
                    }
                    break;
                }
                if (outcome == CombatOutcome.Timeout || outcome == CombatOutcome.Unverified)
                {
                    RecordFrame("nightmare-combat-unverified");
                    Fail("当前巢穴未确认清场，已停止在当前进度。");
                    return false;
                }
                if (outcome == CombatOutcome.Dead)
                {
                    int count;
                    if (target == null)
                        count = ++standAloneDeaths;
                    else
                    {
                        deaths.TryGetValue(target.Key, out count);
                        deaths[target.Key] = ++count;
                    }
                    RunLog.Warn(LogArea, "本轮在同一巢穴死亡 " + count + " 次");
                    if (reopenBook)
                    {
                        RecoverScannedNest(target, count);
                        return false;
                    }
                    if (count >= 3)
                    {
                        exhaustedDeaths++;
                        RecordFrame("nightmare-death-limit");
                        Fail("当前巢穴连续三次倒下，停止任务并保留未完成状态。");
                        return false;
                    }
                    if (!TryReviveInNest())
                        return false;
                    continue;
                }
                fought = true;
                completedFights++;
                world.AllowShortEnemyBars = true;
                int receiptsBefore = confirmedReceipts;
                if (world.WaitUntil(() => HasEnemies, 3000))
                {
                    RunLog.Info(LogArea, "等待掉落时发现敌人，立即恢复战斗");
                    continue;
                }
                Report("查找战斗掉落的声骸");
                CollectEcho();
                bool dropped = confirmedReceipts > receiptsBefore;
                if (!dropped && target != null && !team && !retriedPickup &&
                    CanSearchForEcho() && CurrentEchoPrompt() == EchoPrompt.None)
                {
                    retriedPickup = true;
                    RunLog.Warn(LogArea, "首次搜索未确认吸收，现场也没有交互提示，重新传送至同一巢穴后再搜一次");
                    RetryAfterTeleport(target);
                    dropped = confirmedReceipts > receiptsBefore;
                    if (Failed)
                        return false;
                }
                if (token.IsCancellationRequested)
                    return false;
                if (!dropped)
                {
                    if (world.Tick() && HasEnemies)
                    {
                        RunLog.Info(LogArea, "拾取暂停，当前仍有敌人，继续战斗");
                        continue;
                    }
                    unconfirmedPickup = true;
                    Report("本轮未确认吸收，检查巢穴是否还有敌人", warning: true);
                }
                else
                {
                    RunLog.Info(LogArea, "本轮已确认吸收声骸");
                    if (options.CaptureOnce)
                    {
                        captureDone = true;
                        Report("已捕获声骸，结束梦魇巢穴任务");
                        break;
                    }
                }
                if (!WaitForNextWave()) break;
                RunLog.Info(LogArea, "拾取结束后发现敌人，继续当前巢穴");
            }
            if (token.IsCancellationRequested)
                return false;
            if (unconfirmedPickup && !captureDone)
            {
                unconfirmedPickupNests++;
                RecordFrame("nightmare-absorb-unconfirmed");
                Report("巢穴有轮次未确认吸收，准备核对击败进度", warning: true);
            }
            return true;
        }

        private bool WaitForNextWave()
        {
            long probeAt = world.Now;
            return world.WaitUntil(() =>
            {
                if (HasEnemies) return true;
                FarmUi ui = world.Ui;
                if (world.TeamHud && ui != null && !ui.Claim && !ui.Challenge && ui.ConfirmBox.IsEmpty && world.Now >= probeAt)
                {
                    probeAt = world.Now + 500;
                    world.Send(BattleAction.Target());
                }
                return false;
            }, 3000);
        }

        private void RecoverScannedNest(NestTarget target, int count)
        {
            var recovery = new WorldRecovery(world, state => Report(state));
            if (!settings.AutoRevive || !recovery.Recover(enteredFromWaypoint: false, requireRealmForSkip: false))
            {
                if (!token.IsCancellationRequested) Fail("角色倒下后未确认返回大世界，无法继续扫描巢穴。");
                return;
            }
            if (count >= 3)
            {
                excludedNests.Add(target.Key);
                exhaustedDeaths++;
                RecordFrame("nightmare-death-limit");
                Report("当前巢穴连续三次倒下，跳过并继续扫描下一个巢穴。", warning: true);
            }
            else
            {
                Report("已确认回血，重新打开图鉴继续扫描巢穴。");
            }
        }

        private bool TryReviveInNest()
        {
            if (settings.AutoRevive && world.WaitUntil(() => world.Ui != null && world.Ui.Death &&
                !world.Ui.DeathBox.IsEmpty, 3500))
            {
                RecordFrame("nightmare-revive-before");
                if (world.ClickMenuButton(world.Ui.DeathBox) && WaitTeam(30000))
                {
                    RecordFrame("nightmare-revived");
                    RunLog.Info(LogArea, "已确认恢复队伍界面，继续核对同一巢穴");
                    return true;
                }
            }
            if (!token.IsCancellationRequested)
                Fail("角色倒下后未确认恢复，当前巢穴未完成。");
            return false;
        }

        private bool VerifyNestProgress(NestTarget target)
        {
            if (!OpenBook() || !SelectTab(target.Tab))
                return false;
            NestTarget current = null;
            while (!token.IsCancellationRequested)
            {
                var lines = world.Read(.35, .25, 1, .96);
                current = ReadVisibleNest(target, lines);
                if (current != null && (current.Defeated == current.Total || !current.Point.IsEmpty))
                    break;
                if (!RevealMoreRows(lines))
                    break;
            }
            if (token.IsCancellationRequested)
                return false;
            if (current == null)
            {
                Fail("未在图鉴中重新确认同一聚落的击败进度。");
                return false;
            }

            RecordFrame("nightmare-progress-after");
            RunLog.Info(LogArea, string.Format("核对同一聚落 {0}：{1}/{2} → {3}/{4}",
                target.Key, target.Defeated, target.Total, current.Defeated, current.Total));
            if (current.Defeated == current.Total)
            {
                NestTarget confirmed = ReadVisibleNest(target);
                if (confirmed != null && confirmed.Defeated == confirmed.Total)
                {
                    RecordFrame("nightmare-cleared");
                    excludedNests.Add(target.Key);
                    retriedStalledNests.Remove(target.Key);
                    Report("已从图鉴确认清场 " + confirmed.Total + "/" + confirmed.Total);
                    return true;
                }
                Fail("同一聚落的满进度未得到再次确认。");
                return false;
            }
            if (current.Defeated < target.Defeated || current.Point.IsEmpty ||
                current.Defeated == target.Defeated && !retriedStalledNests.Add(target.Key))
            {
                Fail("同一聚落仍是 " + current.Defeated + "/" + current.Total + "，未能继续推进。");
                return false;
            }
            if (current.Defeated > target.Defeated)
                retriedStalledNests.Remove(target.Key);
            pendingNest = current;
            Report("聚落进度 " + current.Defeated + "/" + current.Total + "，继续同一聚落");
            return false;
        }

        private bool LeaveTeamNest()
        {
            if (world.Tick() && world.VerifiedWorld)
            {
                if (world.Ui.OpenWorld && !world.InRealm)
                    return true;
                if (captureDone || !HasEnemies)
                {
                    Report("确认队伍挑战退场");
                    Rectangle confirm = Rectangle.Empty;
                    string confirmFeature;
                    bool sent = world.Key((char)0x1B);
                    if (sent && world.WaitUntil(() => world.FindNear("gray_confirm_exit_button", .74, out confirm) ||
                        world.Vision.BestMatch(world.Frame, TravelConfirmFeatures, 16, .78, .70,
                            out confirmFeature, out confirm), 5000))
                    {
                        RecordFrame("nightmare-team-exit-confirm");
                        int ready = 0;
                        if (world.ClickMenuButton(confirm) && world.WaitUntil(() =>
                        {
                            ready = world.VerifiedWorld && world.Ui.OpenWorld && !world.InRealm ? ready + 1 : 0;
                            return ready >= 2;
                        }, 120000))
                        {
                            RecordFrame("nightmare-team-exit-world");
                            return true;
                        }
                    }
                }
            }
            if (!token.IsCancellationRequested)
                Fail("未确认退出队伍挑战并返回大世界，当前聚落未记为完成。");
            return false;
        }

        private bool HasEnemies
        {
            get { return world.TeamHud && (world.Hud.Locked || world.Hud.EnemyHealth); }
        }

        internal bool CollectEcho()
        {
            if (!CanSearchForEcho()) return false;
            bool prompt = CanPickupEcho() && CurrentEchoPrompt() != EchoPrompt.None;
            if (!prompt)
            {
                if (!world.CenterEchoCamera(world.InputRouteVersion, 20, 200)) return false;
                prompt = WalkFor('S', 2500) || WalkFor('W', 5000);
            }
            if (prompt && CollectVisibleEchoes()) return true;
            return YoloCollect(30000);
        }

        private bool RetryAfterTeleport(NestTarget target)
        {
            if (!target.Named && target.RevealScrolls > 0)
            {
                RunLog.Warn(LogArea, "补扫目标没有可验证的名称，离开列表后无法安全确认是原巢穴");
                return false;
            }
            if (!OpenBook())
                return false;
            if (!SelectTab(target.Tab))
                return false;
            while (revealScrolls < target.RevealScrolls)
                if (!RevealMoreRows())
                    return false;
            if (!ClickCurrentVisit(target, retry: false, allowCompleted: true))
                return false;
            world.Sleep(2000);
            string feature;
            Rectangle button;
            if (!WaitScreen(TravelFeatures, 5000, out feature, out button))
            {
                if (!token.IsCancellationRequested)
                    RunLog.Warn(LogArea, "重搜时未识别到传送按钮");
                return false;
            }
            if (!Travel(target, feature, button))
                return false;
            RunForward(2);
            return CollectEcho();
        }

        private bool Pickup()
        {
            if (!CanPickupEcho())
            {
                if (!token.IsCancellationRequested)
                    RunLog.Warn(LogArea, "停止拾取，当前画面已不符合拾取条件");
                return false;
            }
            EchoPrompt prompt = CurrentEchoPrompt(fresh: true);
            RunLog.Info(LogArea, "拾取前重读交互提示。吸收提示=" + prompt);
            if (prompt == EchoPrompt.Below)
            {
                RunLog.Info(LogArea, "吸收位于下一条交互选项，尝试滚动选中");
                if (!world.ScrollInteraction())
                {
                    if (!token.IsCancellationRequested)
                        RunLog.Warn(LogArea, "未能发送交互滚动");
                    return false;
                }
                world.Sleep(300);
                if (!CanPickupEcho())
                    return false;
                prompt = CurrentEchoPrompt(fresh: true);
                RunLog.Info(LogArea, "滚动后重读交互提示。吸收提示=" + prompt);
            }
            for (int attempt = 0; attempt < 2 && prompt == EchoPrompt.Selected; attempt++)
            {
                if (FarmVision.HasEchoReceipt(world.Frame))
                {
                    RunLog.Info(LogArea, "按 F 前已有获取通知，等待其消失后重新确认交互提示");
                    if (!WaitForOldPickupReceipt() ||
                        !CanPickupEcho() || CurrentEchoPrompt(fresh: true) != EchoPrompt.Selected)
                    {
                        if (!token.IsCancellationRequested)
                            RunLog.Warn(LogArea, "旧获取通知未消失或吸收选项已经变化，取消本次按 F");
                        return false;
                    }
                }
                var receipt = new PickupReceiptCheck(FarmVision.HasEchoReceipt(world.Frame));
                RunLog.Info(LogArea, "尝试吸收，第 " + (attempt + 1) + " 次，输入=" +
                    (attempt == 0 ? "按 F" : "补按 F"));
                if (!world.Interact(retry: attempt > 0))
                {
                    if (!token.IsCancellationRequested)
                        RunLog.Warn(LogArea, "未能发送吸收按键");
                    return false;
                }
                Report("正在吸收声骸");
                int checkedFrames = 0, receiptFrames = 0;
                bool combatReturned = false;
                bool observed = world.WaitUntil(delegate
                {
                    if (world.Ui == null)
                        return false;
                    if (world.Ui.Claim)
                        return true;
                    if (!world.TeamHud || world.Ui.Book || world.Ui.Death)
                        return false;
                    checkedFrames++;
                    bool visible = FarmVision.HasEchoReceipt(world.Frame);
                    if (visible)
                        receiptFrames++;
                    if (receipt.Observe(visible))
                        return true;
                    combatReturned = HasEnemies;
                    return combatReturned;
                }, 2500);
                if (combatReturned)
                {
                    RunLog.Info(LogArea, "等待吸收回执时发现敌人，转回战斗");
                    return false;
                }
                if (observed && world.Ui.Claim)
                {
                    RunLog.Warn(LogArea, "按 F 后打开的是领取界面，按 Esc 返回");
                    world.Key((char)0x1B);
                    return false;
                }
                if (observed)
                {
                    confirmedReceipts++;
                    RecordFrame("nightmare-absorb-confirmed");
                    RunLog.Info(LogArea, string.Format("检测到本次获取通知。检查={0}, 命中={1}",
                        checkedFrames, receiptFrames));
                    Report("已确认吸收声骸");
                    return true;
                }
                if (!token.IsCancellationRequested)
                    RunLog.Warn(LogArea, string.Format("等待新获取通知超时 2500ms。检查={0}, 命中={1}",
                        checkedFrames, receiptFrames));
                if (!CanPickupEcho())
                    return false;
                prompt = CurrentEchoPrompt(fresh: true);
                RunLog.Info(LogArea, "吸收未确认后重读交互提示。吸收提示=" + prompt);
            }
            return false;
        }

        private Func<long, bool> WatchCombatCapture()
        {
            PickupReceiptCheck receipt = null;
            long expires = 0;
            int route = world.InputRouteVersion;
            return now =>
            {
                if (route != world.InputRouteVersion || now >= expires)
                {
                    receipt = null;
                    route = world.InputRouteVersion;
                }
                bool visible = FarmVision.HasEchoReceipt(world.Frame);
                if (receipt != null)
                {
                    if (!receipt.Observe(visible))
                        return false;
                    confirmedReceipts++;
                    RecordFrame("nightmare-combat-absorb-confirmed");
                    return true;
                }
                if (!visible && CurrentEchoPrompt() == EchoPrompt.Selected && world.Interact(retry: false))
                {
                    receipt = new PickupReceiptCheck(false);
                    expires = now + 2500;
                }
                return false;
            };
        }

        private bool WaitForOldPickupReceipt()
        {
            int clearFrames = 0;
            return world.WaitUntil(delegate
            {
                if (HasEnemies)
                    return true;
                clearFrames = world.TeamHud && !FarmVision.HasEchoReceipt(world.Frame) ? clearFrames + 1 : 0;
                return clearFrames >= 2;
            }, 3200) && clearFrames >= 2;
        }

        private bool CollectVisibleEchoes()
        {
            bool confirmed = false;
            while (CanPickupEcho())
            {
                if (CurrentEchoPrompt(fresh: true) == EchoPrompt.None)
                    return confirmed;
                if (!Pickup())
                    return false;
                confirmed = true;
                if (options.CaptureOnce)
                    return true;
            }
            return confirmed;
        }

        private EchoPrompt CurrentEchoPrompt(bool fresh = false)
        {
            if (fresh || pickupClock.ElapsedMilliseconds >= nextPickupOcr)
            {
                lastEchoPrompt = NestMath.ReadEchoPrompt(world.Frame, world.Ui, world.Vision, world.Ocr);
                nextPickupOcr = pickupClock.ElapsedMilliseconds + 180;
            }
            return lastEchoPrompt;
        }

        private bool CanPickupEcho()
        {
            return !token.IsCancellationRequested && world.Tick() && world.TeamHud && !HasEnemies &&
                world.Ui != null && !world.Ui.Death && !world.Ui.Claim && !world.Ui.Book;
        }

        private bool CanSearchForEcho()
        {
            return !token.IsCancellationRequested && world.Tick() && world.TeamHud && !HasEnemies;
        }

        private bool WalkFor(char direction, int milliseconds)
        {
            if (!CanSearchForEcho() || !world.KeyDown(direction))
                return false;
            RunLog.Info(LogArea, string.Format("走位搜索声骸。方向={0}, 时限={1}ms", direction, milliseconds));
            var watch = Stopwatch.StartNew();
            try
            {
                while (watch.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
                {
                    if (!CanSearchForEcho())
                    {
                        if (!token.IsCancellationRequested)
                            RunLog.Info(LogArea, "当前画面已不符合搜索条件，停止走位");
                        return false;
                    }
                    if (CurrentEchoPrompt() != EchoPrompt.None)
                    {
                        RunLog.Info(LogArea, "走位 " + direction + " 时识别到吸收提示 " + lastEchoPrompt);
                        return true;
                    }
                    world.Token.WaitHandle.WaitOne(100);
                }
            }
            finally
            {
                world.KeyUp(direction);
            }
            if (!token.IsCancellationRequested)
                RunLog.Info(LogArea, "走位 " + direction + " 未找到吸收提示");
            return false;
        }

        private bool YoloCollect(int milliseconds)
        {
            if (detector == null)
            {
                RunLog.Info(LogArea, "声骸检测模型不可用，跳过模型搜索");
                return false;
            }
            RunLog.Info(LogArea, "开始模型搜索，最多检查 4 个方向，靠近掉落时限 " + milliseconds + "ms");
            bool wasRetaining = world.RetainEchoFrames;
            world.RetainEchoFrames = true;
            try
            {
                if (!CanSearchForEcho()) return false;
                if (CurrentEchoPrompt(fresh: true) != EchoPrompt.None && CollectVisibleEchoes()) return true;
                int route = world.InputRouteVersion;
                for (int view = 0; view < 4; view++)
                {
                    if (!CanSearchForEcho() || !world.CenterEchoCamera(route) || !CanSearchForEcho() ||
                        world.InputRouteVersion != route) return false;
                    List<EchoBox> echoes = detector.Detect(world.Frame, .5f);
                    RunLog.Info(LogArea, "模型搜索方向 " + (view + 1) + " 检出声骸候选 " + echoes.Count + " 个");
                    if (echoes.Count > 0)
                        return WalkTowardDetectedEcho(milliseconds, route);
                    if (!world.Key('A', 50, route: route)) return false;
                    world.Sleep(500);
                }
                if (CanSearchForEcho()) world.CenterEchoCamera(route);
            }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException error)
            {
                RunLog.Error(LogArea, "本轮模型搜索失败", error);
            }
            finally { world.RetainEchoFrames = wasRetaining; }
            return false;
        }

        private bool WalkTowardDetectedEcho(int milliseconds, int route)
        {
            var watch = Stopwatch.StartNew();
            var approach = new EchoApproach();
            char held = '\0';
            RunLog.Info(LogArea, "开始靠近模型识别到的声骸");
            try
            {
                while (watch.ElapsedMilliseconds < milliseconds && CanSearchForEcho() && world.InputRouteVersion == route)
                {
                    if (CurrentEchoPrompt() != EchoPrompt.None)
                    {
                        if (held != '\0') { world.KeyUp(held); held = '\0'; }
                        if (CollectVisibleEchoes()) return true;
                        if (!CanSearchForEcho() || world.InputRouteVersion != route) break;
                    }
                    var echoes = detector.Detect(world.Frame, .5f);
                    char next = approach.Direction(echoes, new Size(world.Frame.Width, world.Frame.Height), held, watch.ElapsedMilliseconds);
                    if (next == '\0') break;
                    if (next != held)
                    {
                        if (held != '\0')
                        {
                            world.KeyUp(held);
                            held = '\0';
                            if (token.WaitHandle.WaitOne(10)) break;
                        }
                        if (!world.KeyDown(next, route)) break;
                        held = next;
                    }
                    world.Sleep(40);
                }
                return false;
            }
            finally { if (held != '\0') world.KeyUp(held); }
        }

        private bool Travel(NestTarget target, string feature, Rectangle button)
        {
            Report("传送到巢穴");
            RunLog.Info(LogArea, string.Format("巢穴传送准备。图鉴={0}，按钮类型={1}，初始区域={2}",
                target.Tab, feature, button));
            bool sent = ClickTravelButton(feature, button);
            Rectangle action;
            if (sent)
                world.Sleep(1000);
            TravelView view = world.Tick() ? ReadTravelView(feature, out action) : TravelView.Unknown;
            if (view == TravelView.Confirm && sent && !token.IsCancellationRequested)
            {
                Report("确认巢穴传送");
                if (world.Tick() && ReadTravelView(feature, out action) == TravelView.Confirm &&
                    world.ClickMenuButton(action))
                    world.Sleep(1000);
                view = world.Tick() ? ReadTravelView(feature, out action) : TravelView.Unknown;
            }
            if (sent && (view == TravelView.Unknown || view == TravelView.World) && !token.IsCancellationRequested)
                view = WaitForTravelView(feature, 120000);
            if (token.IsCancellationRequested)
                return false;
            if (view == TravelView.World && sent)
            {
                visibleBookPage = null;
                RunLog.Info(LogArea, "已确认离开传送界面，世界和队伍画面已恢复");
                return true;
            }
            if (view != TravelView.Terminal && view != TravelView.Guidebook &&
                view != TravelView.World && world.Tick())
            {
                RecordFrame("nightmare-travel-unconfirmed");
                RunLog.Warn(LogArea, "未确认到达巢穴，返回上一级后再扫描");
                if (world.Key((char)0x1B))
                    view = WaitForTravelView(feature, 8000);
            }
            if (token.IsCancellationRequested)
                return false;
            if (view == TravelView.World || view == TravelView.Guidebook || view == TravelView.Terminal)
            {
                excludedNests.Add(target.Key);
                travelSkips.Add(target.Key);
                visibleBookPage = null;
                RecordFrame("nightmare-travel-skipped");
                Report("传送未到达目标，已返回可识别界面，继续扫描", warning: true);
                return false;
            }
            Fail("巢穴传送进度未确认，最后画面=" + DescribeTravelView(view) + "。请确认地图、传送确认或加载状态。");
            return false;
        }

        private bool ClickTravelButton(string feature, Rectangle previous)
        {
            Rectangle button;
            if (!world.Tick() || ReadTravelView(feature, out button) != TravelView.Button ||
                !Rectangle.Inflate(previous, 24, 24).IntersectsWith(button))
                return false;

            Point point;
            Rectangle row = Rectangle.Inflate(button, 0, Math.Max(12, button.Height / 2));
            List<OcrLine> lines = world.Read(.60, Math.Max(0, row.Top) / (double)world.Frame.Height,
                .99, Math.Min(world.Frame.Height, row.Bottom) / (double)world.Frame.Height);
            bool matchedText = TryTravelButtonText(lines, button, world.Frame.Width, out point);
            Rectangle latest;
            if (!world.Tick() || ReadTravelView(feature, out latest) != TravelView.Button ||
                !Rectangle.Inflate(button, 12, 12).IntersectsWith(latest))
                return false;
            if (!matchedText)
                point = new Point(latest.X + latest.Width / 2, latest.Y + latest.Height / 2);
            bool sent = world.ClickMenuButton(point);
            RunLog.Info(LogArea, string.Format("传送按钮输入。方式={0}，位置={1}，模式={2}，投递={3}",
                matchedText ? "按钮文字" : "模板", point, world.Window.Mode, sent));
            return sent;
        }

        internal static bool TryTravelButtonText(List<OcrLine> lines, Rectangle button, int frameWidth, out Point point)
        {
            point = Point.Empty;
            var center = new Point(button.X + button.Width / 2, button.Y + button.Height / 2);
            foreach (OcrLine line in lines)
            {
                if (line.Box.IsEmpty || string.IsNullOrWhiteSpace(line.Text) || line.Score < .72f)
                    continue;
                string label = Regex.Replace(line.Text, @"\s+", "");
                if (label != "快速旅行" && label != "快速传送" && label != "快速傳送" && label != "传送" &&
                    label != "傳送" && !string.Equals(label, "FastTravel", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(label, "Teleport", StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = new Point(line.Box.X + line.Box.Width / 2, line.Box.Y + line.Box.Height / 2);
                if (Math.Abs(candidate.Y - center.Y) > Math.Max(12, button.Height / 2) ||
                    Math.Abs(candidate.X - center.X) > frameWidth * .22)
                    continue;
                if (!point.IsEmpty)
                {
                    point = Point.Empty;
                    return false;
                }
                point = candidate;
            }
            return !point.IsEmpty;
        }

        private TravelView WaitForTravelView(string feature, int timeoutMs)
        {
            TravelView view = TravelView.Unknown;
            int samples = 0, stable = 0;
            var watch = Stopwatch.StartNew();
            world.WaitUntil(() =>
            {
                Rectangle action;
                TravelView current = ReadTravelView(feature, out action);
                samples++;
                stable = current == view ? stable + 1 : 1;
                if (current != view)
                    RunLog.Info(LogArea, "传送画面切换=" + DescribeTravelView(current));
                view = current;
                return stable >= 2 && (current == TravelView.World || current == TravelView.Terminal ||
                    current == TravelView.Guidebook);
            }, timeoutMs);
            if (stable < 2)
                view = TravelView.Unknown;
            RunLog.Info(LogArea, string.Format("传送画面检查结果={0}，采样={1}，耗时={2}ms",
                DescribeTravelView(view), samples, watch.ElapsedMilliseconds));
            return view;
        }

        private TravelView ReadTravelView(string feature, out Rectangle action)
        {
            action = Rectangle.Empty;
            FarmUi ui = world.Ui;
            if (ui == null || ui.Death || ui.Challenge || ui.MonthlyCard || ui.ClaimReward || ui.SkipDialog)
                return TravelView.Unknown;
            string label;
            if (world.Vision.BestMatch(world.Frame, TravelConfirmFeatures, 20, .72, .60, out label, out action))
                return TravelView.Confirm;
            if (ui.Claim)
                return TravelView.Unknown;
            if (world.Vision.FindLabel(world.Frame, feature, .70, out action))
                return TravelView.Button;
            if (world.VerifiedWorld)
                return TravelView.World;
            bool selected;
            if ((ui.Book || ui.BookMonsters) && world.Vision.InspectGuidebook(world.Frame, out action, out selected))
                return TravelView.Guidebook;
            if (world.Vision.IsTerminal(world.Frame))
                return TravelView.Terminal;
            return TravelView.Unknown;
        }

        private static string DescribeTravelView(TravelView view)
        {
            switch (view)
            {
                case TravelView.Button: return "原地图传送按钮";
                case TravelView.Confirm: return "确认弹窗";
                case TravelView.World: return "世界与队伍";
                case TravelView.Guidebook: return "索拉指南";
                case TravelView.Terminal: return "终端";
                default: return "加载或未知画面";
            }
        }

        private bool TeamChallenge(Rectangle button)
        {
            if (!world.ClickMenuButton(button))
            {
                Fail("已识别到开始挑战按钮，但点击未发送。请确认游戏窗口可用。");
                return false;
            }
            world.Sleep(1000);
            world.WaitForOptionalDialog();
            return !token.IsCancellationRequested;
        }


        private void RunForward(int seconds)
        {
            if (!CanSearchForEcho() || !world.KeyDown('W'))
                return;
            try
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < seconds * 1000 && !token.IsCancellationRequested)
                {
                    if (!CanSearchForEcho())
                        return;
                    world.Token.WaitHandle.WaitOne(100);
                }
            }
            finally
            {
                world.KeyUp('W');
            }
        }

        private bool WaitTeam(int timeoutMs)
        {
            int readyFrames = 0;
            return world.WaitUntil(delegate
            {
                readyFrames = world.VerifiedWorld ? readyFrames + 1 : 0;
                return readyFrames >= 2;
            }, timeoutMs);
        }

        private bool EnsureWorld()
        {
            var closedMenus = new HashSet<GuideView>();
            return world.WaitUntil(delegate
            {
                Rectangle tab;
                bool selected;
                GuideView view;
                if (world.Ui != null && world.Ui.MonthlyCard)
                    return world.DismissMonthlyCard(requireTeam: true) && world.VerifiedWorld && !world.InRealm;
                guide.InspectBookView(out tab, out selected, out view);
                if (view == GuideView.World)
                    return true;
                if ((view == GuideView.Terminal || view == GuideView.Guidebook) && closedMenus.Add(view))
                {
                    RunLog.Info(LogArea, "返回大世界，关闭已识别的菜单：" + GuidebookNavigation.DescribeGuideView(view));
                    if (world.Key((char)0x1B))
                        world.Sleep(700);
                }
                return false;
            }, 10000);
        }

        private void Report(string state, bool warning = false)
        {
            if (warning)
                RunLog.Warn(LogArea, state);
            else
                RunLog.Info(LogArea, state);
            progress.Report(new NightmareReport
            {
                State = state, Nests = nests,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            Failed = true;
            Failure = message;
            CompletionIssue = BuildCompletionIssue() ?? message;
            RecordFrame("nightmare-failed");
            RunLog.Warn(LogArea, message);
            progress.Report(new NightmareReport
            {
                State = message, Failure = message, Finished = true, Nests = nests,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            if (detector != null) detector.Dispose();
            if (ownsWorld)
                world.Dispose();
        }
    }
}
