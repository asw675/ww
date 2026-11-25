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
            {
                // OK-WW 的空 Which to Farm 回退默认两种分类。
                TacetNest = true;
                Purification = true;
            }
        }
    }

    internal enum NestTab { TacetNest, TacetScroll, Purification, PurificationScroll }
    internal enum EchoPrompt { None, Selected, Below }

    internal enum NestTravelView { Unknown, Button, Confirm, Guidebook, Terminal, World }
    internal enum NestTeamExitState { Unverified, Quiet, Enemies }

    internal sealed class NestTeamExitCheck
    {
        private readonly CombatClearCheck clear = new CombatClearCheck();

        internal NestTeamExitState Observe(HudSnapshot hud, FarmUi ui, bool verifiedWorld, long now)
        {
            bool playable = NestMath.IsPlayableTeam(hud, ui, verifiedWorld);
            bool enemies = playable && (hud.Locked || hud.EnemyHealth);
            clear.Observe(playable, enemies, now);
            if (enemies) return NestTeamExitState.Enemies;
            return clear.IsQuiet(now) ? NestTeamExitState.Quiet : NestTeamExitState.Unverified;
        }
    }

    internal sealed class NestTravelObservation
    {
        private NestTravelView lastView;
        private int consecutiveFrames;
        private bool confirmationAttempted;

        internal NestTravelView StableView { get { return consecutiveFrames >= 2 ? lastView : NestTravelView.Unknown; } }
        internal bool Arrived { get { return StableView == NestTravelView.World; } }
        internal bool CanConfirm { get { return !confirmationAttempted && StableView == NestTravelView.Confirm; } }

        internal void Observe(NestTravelView view)
        {
            consecutiveFrames = view == lastView ? consecutiveFrames + 1 : 1;
            lastView = view;
        }

        internal bool ObserveButton(NestTravelView view, Rectangle anchor, Rectangle current)
        {
            Observe(view == NestTravelView.Button && !current.IsEmpty &&
                Rectangle.Inflate(anchor, 24, 24).IntersectsWith(current) ? NestTravelView.Button : NestTravelView.Unknown);
            return StableView == NestTravelView.Button;
        }

        internal void ConfirmationAttempted()
        {
            confirmationAttempted = true;
            ResetSamples();
        }

        internal void ResetSamples()
        {
            lastView = NestTravelView.Unknown;
            consecutiveFrames = 0;
        }
    }

    internal sealed class NestTravelRoute
    {
        private readonly GameSource source;
        private readonly IntPtr window;
        private readonly uint process;
        private readonly Size size;
        private GameSource mode;
        internal int Version { get; private set; }

        internal NestTravelRoute(GameSource source, IntPtr window, uint process, Size size, GameSource mode, int version)
        {
            this.source = source;
            this.window = window;
            this.process = process;
            this.size = size;
            this.mode = mode;
            Version = version;
        }

        internal bool Observe(IntPtr observedWindow, uint observedProcess, Size observedSize, GameSource observedMode, int version)
        {
            if (window != observedWindow || process != observedProcess || size != observedSize) return false;
            if (version == Version) return mode == observedMode;
            if (source != GameSource.AutoLocal || window == IntPtr.Zero || process == 0 || version < Version ||
                !((mode == GameSource.Background && observedMode == GameSource.Foreground) ||
                  (mode == GameSource.Foreground && observedMode == GameSource.Background)))
                return false;
            mode = observedMode;
            Version = version;
            return true;
        }
    }

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
        internal string ScanKey;
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
        private static readonly string[] TacetHeadings =
            { "残象聚落", "殘象聚落", "声骸巢穴", "聲骸巢穴", "Tacet Discord Nest" };
        private static readonly string[] PurificationHeadings =
            { "梦魇祓除", "夢魘祓除", "梦魔祓除", "夢魔祓除", "梦魇拔除", "夢魘拔除", "梦魔拔除", "夢魔拔除",
                "梦魇净化", "夢魘淨化", "Nightmare Purification" };
        private static readonly string[] TacetSubtitles = { "产出声骸套件", "產出聲骸套件" };
        private static readonly string[] PurificationSubtitles =
            { "产出梦魇声骸", "產出夢魘聲骸", "产出梦魔声骸", "產出夢魔聲骸" };

        internal static bool CanRetryUnstartedCombat(CombatOutcome outcome, bool started, bool retried)
        {
            return !started && !retried && (outcome == CombatOutcome.NoCombat || outcome == CombatOutcome.Unverified);
        }

        internal static bool IsPlayableTeam(HudSnapshot hud, FarmUi ui, bool verifiedWorld)
        {
            return verifiedWorld && hud != null && hud.Valid && !hud.Dead && !hud.ChallengePrompt &&
                ui != null && !ui.Death && !ui.MonthlyCard && !ui.Claim && !ui.Challenge && !ui.SkipDialog &&
                ui.ConfirmBox.IsEmpty;
        }

        internal static Rectangle BookCategoryButton(IEnumerable<OcrLine> lines, bool purification)
        {
            Rectangle heading = DailyClaimVision.UniqueButton(lines, purification ? PurificationHeadings : TacetHeadings);
            return !heading.IsEmpty ? heading :
                DailyClaimVision.UniqueButton(lines, purification ? PurificationSubtitles : TacetSubtitles);
        }

        internal static Rectangle ScrollThumb(PixelFrame frame, bool sidebar)
        {
            var size = new Size(frame.Width, frame.Height);
            Rectangle area = sidebar
                ? GuidebookList.Area(size, .352, .120, .360, .889)
                : GuidebookList.Area(size, .969, .135, .978, .889);
            return GuidebookList.FindVerticalThumb(frame, area, Math.Max(12, size.Height / 40), 185, 255);
        }

        internal static bool AtBookListEdge(Rectangle thumb, int height, bool bottom)
        {
            if (thumb.IsEmpty) return !bottom;
            int tolerance = Math.Max(5, height / 160);
            int edge = (int)Math.Round(height * (bottom ? DomainMath.BookScrollBottom : GuidebookList.DefaultTop));
            return bottom ? thumb.Bottom >= edge - tolerance : thumb.Top <= edge + tolerance;
        }

        internal static bool BookViewportMovedDown(Rectangle before, Rectangle after, int frameHeight)
        {
            if (before.IsEmpty || after.IsEmpty) return false;
            int overlap = Math.Min(before.Bottom, after.Bottom) - Math.Max(before.Top, after.Top);
            int shorter = Math.Min(before.Height, after.Height);
            return Math.Abs(after.X - before.X) <= 3 &&
                Math.Abs(after.Height - before.Height) <= Math.Max(5, frameHeight / 160) &&
                after.Top - before.Top >= Math.Max(1, frameHeight / 900) && overlap >= (shorter + 1) / 2;
        }

        internal static bool CountedPurificationEnded(List<OcrLine> lines)
        {
            Rectangle special = DailyClaimVision.UniqueButton(lines, "特异梦魇", "特異夢魘", "特异梦魔", "特異夢魔");
            if (special.IsEmpty) return false;
            Rectangle lastRegularTitle = Rectangle.Empty, lastProgress = Rectangle.Empty;
            foreach (OcrLine line in lines)
            {
                if (line.Box.IsEmpty || line.Score < .75f) continue;
                if (line.Box.Bottom <= special.Top && line.Box.Top > lastRegularTitle.Top &&
                    NestTitle.IsMatch(line.Text ?? string.Empty))
                    lastRegularTitle = line.Box;
                int defeated, total;
                if (!TryProgress(line.Text, out defeated, out total)) continue;
                if (line.Box.Top >= special.Top) return false;
                if (line.Box.Top > lastProgress.Top) lastProgress = line.Box;
            }
            int gap = lastProgress.Top - lastRegularTitle.Bottom;
            return !lastRegularTitle.IsEmpty && !lastProgress.IsEmpty && gap >= 0 &&
                gap <= 2 * lastProgress.Height && Math.Abs(lastRegularTitle.Left - lastProgress.Left) <= 2 * lastProgress.Height;
        }

        internal static Rectangle BookTrackControl(PixelFrame frame, Rectangle thumb, bool bottom)
        {
            if (thumb.IsEmpty) return Rectangle.Empty;
            int edge = (int)Math.Round(frame.Height * (bottom ? DomainMath.BookScrollBottom : GuidebookList.DefaultTop));
            int radius = Math.Max(3, frame.Height / 225);
            int inset = Math.Max(radius + 3, frame.Height / 110);
            int first = bottom ? edge - inset : edge + inset;
            int last = first + (bottom ? -1 : 1) * Math.Max(6, frame.Height / 60);
            int left = Math.Max(0, thumb.Left - 2), right = Math.Min(frame.Width - 1, thumb.Left + 3);
            for (int y = first; bottom ? y >= last : y <= last; y += bottom ? -1 : 1)
            {
                if (y - radius < 0 || y + radius >= frame.Height ||
                    (bottom ? y - radius < thumb.Bottom : y + radius >= thumb.Top)) continue;
                for (int x = left; x <= right; x++)
                {
                    bool darkRail = true;
                    for (int yy = y - radius; yy <= y + radius; yy++)
                        if (frame.Light(x, yy) > 18) { darkRail = false; break; }
                    if (darkRail) return new Rectangle(x, y, 1, 1);
                }
            }
            return Rectangle.Empty;
        }

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
                // 参考将 OCR 计数框改成 x=.9，y=原 y-.9*h。确认该点确在
                // 同行可见按钮内时使用；否则采用已定位按钮中心而不是盲点。
                var reference = new Point((int)(frameWidth * .9), (int)(count.Y - count.Height * .9));
                point = line.Box.Contains(reference) ? reference : center;
            }
            return !point.IsEmpty;
        }

        internal static NestTarget FindTarget(List<OcrLine> lines, NestTab tab, int width, int height,
            HashSet<string> excludedNests, out bool missingButton, int revealScrolls = 0, bool includePartial = false)
        {
            missingButton = false;
            foreach (OcrLine line in lines)
            {
                int defeated = 0;
                int denominator;
                if (line == null || line.Box.IsEmpty ||
                    (includePartial ? line.Score < .75f || !TryProgress(line.Text, out defeated, out denominator) ||
                        defeated >= denominator : !TryIncomplete(line.Text, out denominator)))
                    continue;
                bool named;
                string key = TargetKey(lines, tab, denominator, line.Box, height, out named, revealScrolls);
                string scanKey = CacheKey(tab, denominator, line.Box, height);
                if (revealScrolls > 0) scanKey += ":scroll:" + revealScrolls;
                if (key != null && excludedNests.Contains(key) ||
                    (!includePartial || !named) && excludedNests.Contains(scanKey))
                    continue;
                Point point;
                if (key != null && (defeated == 0 || named) && TryVisitPoint(lines, line.Box, width, out point))
                    return new NestTarget { Tab = tab, Key = key, ScanKey = scanKey, Point = point, Named = named,
                        RevealScrolls = revealScrolls, Defeated = defeated, Total = denominator };
                missingButton = true;
            }
            return null;
        }

        internal static bool HasVisibleProgress(IEnumerable<OcrLine> lines)
        {
            foreach (OcrLine line in lines)
            {
                int defeated, total;
                if (line != null && line.Score >= .75f && !line.Box.IsEmpty &&
                    TryProgress(line.Text, out defeated, out total)) return true;
            }
            return false;
        }

        internal static NestTarget InspectBookViewport(List<OcrLine> lines, NestTab tab, Size size, Rectangle thumb,
            HashSet<string> excludedNests, int revealScrolls, bool includePartial, out bool missingButton, out bool endReached)
        {
            NestTarget target = FindTarget(lines, tab, size.Width, size.Height, excludedNests,
                out missingButton, revealScrolls, includePartial);
            endReached = target == null && IsCountedBookEnd(lines, tab, thumb, size.Height);
            return target;
        }

        internal static bool IsCountedBookEnd(List<OcrLine> lines, NestTab tab, Rectangle thumb, int height)
        {
            return AtBookListEdge(thumb, height, true) ||
                (tab == NestTab.Purification || tab == NestTab.PurificationScroll) && CountedPurificationEnded(lines);
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
                current = new NestTarget { Key = target.Key, ScanKey = target.ScanKey, Tab = target.Tab, Named = named, Point = point,
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

        internal static bool ShouldContinueCombatAfterPickup(bool captureOnce, Func<bool> nextWave)
        {
            // 短路：单次捕获模式下甚至不发送下一波探测。
            return !captureOnce && nextWave();
        }

        internal static bool AcceptPickupFlow(bool inputSent, bool sameRoute, bool playable, bool claimPopup)
        {
            // 参考 pick_f / pick_echo 接受已发送且没有领奖弹窗的交互；这并非获取凭证。
            return inputSent && sameRoute && playable && !claimPopup;
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
        private const int NextWaveWaitMs = 5000;
        private const string TeamStart = "team_start_challenge";
        private static readonly string[] TravelFeatures = { "fast_travel_custom", "gray_teleport", "remove_custom" };
        private static readonly string[] NestEntryFeatures =
            { "fast_travel_custom", "gray_teleport", "remove_custom", TeamStart };
        private readonly GuidebookNavigation guide;
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
        private readonly HashSet<string> unconfirmedProgress = new HashSet<string>();
        private readonly Dictionary<string, int> noProgressVisits = new Dictionary<string, int>();
        private readonly Dictionary<string, int> deaths = new Dictionary<string, int>();
        private EchoDetector detector;
        private NestTab? visibleBookPage;
        private int bookScrollSteps;
        private const int MaxBookScrollSteps = 64;
        private readonly Stopwatch pickupClock = Stopwatch.StartNew();
        private long nextPickupOcr;
        private EchoPrompt lastEchoPrompt;
        internal bool Failed { get; private set; }
        internal bool CaptureConfirmed { get { return captureDone; } }
        internal bool ScanResultVerified { get; private set; }
        internal bool HasUnverifiedPickup { get { return pickupSubmissionPending; } }
        internal string Failure { get; private set; }
        internal string CompletionIssue { get; private set; }
        internal string PickupIssue
        {
            get
            {
                return unconfirmedPickupNests > 0 ? unconfirmedPickupNests + " 处声骸吸收回执未核实" :
                    pickupSubmissionPending ? "本次声骸吸收回执未核实" : null;
            }
        }
        internal bool CombatUnresolved { get; private set; }
        internal bool TryGetContinuationWindow(out IntPtr handle, out uint processId)
        {
            return TaskContinuation.TryGetCurrentWindow(world, out handle, out processId);
        }
        private int nests, completedFights, noEntryNests, pagesWithoutButtons, unconfirmedPickupNests, uncertainFightNests;
        private int exhaustedDeaths, confirmedReceipts;
        private bool captureDone, captureStopRequested, pickupSubmissionPending;

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
            options.Validate();
            if (options.TacetNest) queue.Add(NestTab.TacetNest);
            if (options.Purification) queue.Add(NestTab.Purification);
        }

        internal void Run(bool fromDailyReference = false)
        {
            RunLog.Info(LogArea, string.Format("启动巢穴扫描。声骸巢穴={0}, 梦魇净化={1}, 单次捕获={2}, 声骸检测模型就绪={3}",
                options.TacetNest, options.Purification, options.CaptureOnce, detector != null));
            // 参考 run/run_capture_mode 先 ensure_main，而非补打启动时碰到的战斗。
            var recovery = new WorldRecovery(world, state => Report(state));
            if (!(fromDailyReference && AtKnownDailyBoundary()) && !recovery.EnsureMain(30000))
            {
                if (!token.IsCancellationRequested) Fail("启动时未确认返回大世界，无法开始巢穴扫描。");
                return;
            }
            while (!captureDone && !captureStopRequested && !token.IsCancellationRequested)
            {
                NestTarget target = NextNest();
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
            ScanResultVerified = CompletionIssue == null && (!options.CaptureOnce || captureDone);
            RecordFrame(ScanResultVerified ? "after-nightmare-scan-verified" : "nightmare-scan-end");
            if (!(fromDailyReference && AtKnownDailyBoundary()) && !EnsureWorld())
            {
                if (!token.IsCancellationRequested)
                    Fail("巢穴扫描结束后未确认返回大世界。");
                return;
            }
            string final = captureDone ? "已确认获取声骸，任务结束" : "巢穴扫描结束";
            if (CompletionIssue != null)
                final += "，未完成核验：" + CompletionIssue;
            else if (!options.CaptureOnce && PickupIssue != null)
                final += "，" + PickupIssue;
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
            if (unconfirmedProgress.Count > 0)
                issues.Add(unconfirmedProgress.Count + " 处未核实满击败进度");
            if (uncertainFightNests > 0)
                issues.Add(uncertainFightNests + " 处战斗结束状态未核实");
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
                int route = world.InputRouteVersion;
                var lines = world.Read(0.35, 0.25, 1, 0.96);
                if (lines.Count == 0 && !token.IsCancellationRequested)
                {
                    RunLog.Warn(LogArea, "图鉴 " + tab + " 首次 OCR 无结果，等待画面稳定后重读");
                    world.Sleep(700);
                    lines = world.Read(0.35, 0.25, 1, 0.96);
                }
                if (token.IsCancellationRequested) return null;
                if (!world.Captured || world.InputRouteVersion != route)
                {
                    UnconfirmedBookTab(BookCategoryName(tab), "核对原列表画面");
                    return null;
                }
                bool missingButton, endReached;
                NestTarget target = NestMath.InspectBookViewport(lines, tab, new Size(world.Frame.Width, world.Frame.Height),
                    NestMath.ScrollThumb(world.Frame, sidebar: false), excludedNests, bookScrollSteps,
                    !options.CaptureOnce, out missingButton, out endReached);
                if (target != null)
                {
                    RunLog.Info(LogArea, string.Format(
                        "图鉴 {0} 选中候选巢穴。入口按钮={1}, 缓存键={2}, OCR 行数={3}",
                        tab, target.Point, target.ScanKey, lines.Count));
                    Report("发现未完成巢穴");
                    return target;
                }
                if (missingButton || lines.Count == 0 || !options.CaptureOnce && !NestMath.HasVisibleProgress(lines))
                {
                    pagesWithoutButtons++;
                    RecordFrame("nightmare-" + tab + "-unconfirmed");
                    RunLog.Warn(LogArea, "图鉴 " + tab + " 当前画面未核实全部挑战入口");
                }
                // Inspect every reached viewport before using a bottom or section marker.
                if (!endReached)
                {
                    if (AdvanceBookViewport(tab)) continue;
                    UnconfirmedBookTab(BookCategoryName(tab), "继续扫描剩余列表");
                    return null;
                }
                RunLog.Info(LogArea, "核实「" + BookCategoryName(tab) + "」已扫描到常规聚落末尾");
                queue.RemoveAt(0);
            }
            return null;
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
            bool purification = IsPurification(tab);
            int route = world.InputRouteVersion;
            Func<Rectangle> category = () => FindBookCategory(purification, route);
            Func<bool> isSelected = () =>
            {
                Rectangle heading = category();
                return !heading.IsEmpty && DomainMath.IsHighlightedBookKind(world.Frame, heading);
            };
            if (visibleBookPage == tab && isSelected()) return true;
            string name = BookCategoryName(tab);
            Report("扫描图鉴 · " + name);
            if (category().IsEmpty && !RevealBookCategory(category, route))
                return UnconfirmedBookTab(name, "识别分类入口");
            if (!isSelected() && !world.ClickMenuTransition(route, category, isSelected, 5000, 500))
                return UnconfirmedBookTab(name, "切换分类");
            if (!EnsureBookListTop(isSelected, route))
                return UnconfirmedBookTab(name, "滚动到列表顶部");
            visibleBookPage = tab;
            bookScrollSteps = 0;
            return true;
        }

        private static bool IsPurification(NestTab tab)
        {
            return tab == NestTab.Purification || tab == NestTab.PurificationScroll;
        }

        private static string BookCategoryName(NestTab tab)
        {
            return IsPurification(tab) ? "梦魇祓除" : "残象聚落";
        }

        private bool UnconfirmedBookTab(string category, string action)
        {
            visibleBookPage = null;
            if (!token.IsCancellationRequested)
            {
                RecordFrame("nightmare-book-unconfirmed");
                Fail("图鉴未确认「" + category + "」" + action + "。");
            }
            return false;
        }

        private Rectangle FindBookCategory(bool purification, int route)
        {
            Rectangle tab;
            bool selected;
            if (!guide.FindBookTab(out tab, out selected) || !selected || world.InputRouteVersion != route)
                return Rectangle.Empty;
            Rectangle heading = NestMath.BookCategoryButton(world.Read(.095, .11, .35, .91), purification);
            if (heading.IsEmpty && world.InputRouteVersion == route)
                heading = NestMath.BookCategoryButton(world.Read(.137, .11, .335, .91), purification);
            return world.InputRouteVersion == route && world.Captured ? heading : Rectangle.Empty;
        }

        private bool RevealBookCategory(Func<Rectangle> category, int route)
        {
            for (int attempt = 0; attempt < 4 && !token.IsCancellationRequested; attempt++)
            {
                if (!category().IsEmpty) return true;
                if (!ScrollBookPane(sidebar: true, clicks: attempt < 2 ? -3 : 3, route: route))
                    return false;
                world.Sleep(350);
            }
            return !category().IsEmpty;
        }

        private bool EnsureBookListTop(Func<bool> categorySelected, int route)
        {
            if (world.InputRouteVersion != route) return false;
            if (BookScrollReached(false)) return true;
            for (int attempt = 0; attempt < 3 && !token.IsCancellationRequested; attempt++)
            {
                if (!categorySelected() || !ScrollBookPane(sidebar: false, clicks: 4, route: route))
                    break;
                if (world.WaitStable(() => world.InputRouteVersion == route && BookScrollReached(false), 1200, 160))
                    return categorySelected() && BookScrollReached(false);
            }
            return world.ClickMenuTransition(route, () => categorySelected()
                ? NestMath.BookTrackControl(world.Frame, NestMath.ScrollThumb(world.Frame, sidebar: false), bottom: false)
                : Rectangle.Empty, () => categorySelected() && BookScrollReached(false), 3000, 450);
        }

        private bool AtCountedBookEnd(NestTab tab, List<OcrLine> lines)
        {
            return NestMath.IsCountedBookEnd(lines, tab, NestMath.ScrollThumb(world.Frame, sidebar: false), world.Frame.Height);
        }

        private bool AdvanceBookViewport(NestTab tab)
        {
            if (bookScrollSteps >= MaxBookScrollSteps || !world.Tick()) return false;
            int route = world.InputRouteVersion;
            Rectangle heading = FindBookCategory(IsPurification(tab), route);
            if (heading.IsEmpty || !DomainMath.IsHighlightedBookKind(world.Frame, heading)) return false;
            Rectangle initial = NestMath.ScrollThumb(world.Frame, sidebar: false);
            if (initial.IsEmpty) return false;

            for (int attempt = 0; attempt < 2 && !token.IsCancellationRequested; attempt++)
            {
                if (!world.Tick() || world.InputRouteVersion != route) return false;
                Rectangle current = NestMath.ScrollThumb(world.Frame, sidebar: false);
                if (current.IsEmpty) return false;
                if (current.Top == initial.Top)
                    ScrollBookPane(sidebar: false, clicks: -4, route: route);
                if (WaitForBookViewportShift(initial, route))
                {
                    bookScrollSteps++;
                    RunLog.Info(LogArea, "图鉴「" + BookCategoryName(tab) + "」核对下一屏，滚动次数=" + bookScrollSteps);
                    return true;
                }
            }
            return false;
        }

        private bool WaitForBookViewportShift(Rectangle initial, int route)
        {
            Rectangle previous = Rectangle.Empty;
            int stable = 0;
            return world.WaitUntil(() =>
            {
                if (world.InputRouteVersion != route) return true;
                Rectangle thumb = NestMath.ScrollThumb(world.Frame, sidebar: false);
                if (!NestMath.BookViewportMovedDown(initial, thumb, world.Frame.Height))
                {
                    stable = 0;
                    previous = Rectangle.Empty;
                    return false;
                }
                stable = !previous.IsEmpty && Math.Abs(thumb.Top - previous.Top) <= 1 ? stable + 1 : 1;
                previous = thumb;
                return stable >= 3;
            }, 1600) && world.InputRouteVersion == route && stable >= 3;
        }

        private bool ScrollBookPane(bool sidebar, int clicks, int route)
        {
            Rectangle tab;
            bool selected;
            if (!guide.FindBookTab(out tab, out selected) || !selected || world.InputRouteVersion != route)
                return false;
            var size = new Size(world.Frame.Width, world.Frame.Height);
            Point point = GuidebookList.PointAt(size, sidebar ? .23 : .58, .57);
            return world.Input.Scroll(clicks, point, size, world.Bounds, token);
        }

        private bool BookScrollReached(bool bottom)
        {
            return NestMath.AtBookListEdge(NestMath.ScrollThumb(world.Frame, sidebar: false), world.Frame.Height, bottom);
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
                return world.ClickMenuButton(observed.Point, retry, route: route);
            }
            RunLog.Warn(LogArea, "图鉴 " + target.Tab + " 未重新定位到同一个巢穴的挑战入口");
            return false;
        }

        private NestTarget ReadVisibleNest(NestTarget target, List<OcrLine> lines = null)
        {
            lines = lines ?? world.Read(.35, .25, 1, .96);
            return world.Captured ? NestMath.ReadTargetProgress(lines, target, world.Frame.Width,
                world.Frame.Height, bookScrollSteps) : null;
        }

        private NestTarget FindNestAgain(NestTarget target)
        {
            if (!SelectTab(target.Tab)) return null;
            NestTarget observed = ReadVisibleNest(target);
            if (observed != null) return observed;
            if (bookScrollSteps > 0)
            {
                visibleBookPage = null;
                if (!SelectTab(target.Tab)) return null;
            }
            while (!token.IsCancellationRequested)
            {
                var lines = world.Read(.35, .25, 1, .96);
                observed = ReadVisibleNest(target, lines);
                if (observed != null) return observed;
                if (AtCountedBookEnd(target.Tab, lines) || !target.Named && bookScrollSteps >= target.RevealScrolls ||
                    !AdvanceBookViewport(target.Tab)) return null;
            }
            return null;
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
                    excludedNests.Add(!options.CaptureOnce && target.Named ? target.Key : target.ScanKey);
                    noEntryNests++;
                    RecordFrame("nightmare-entry-unconfirmed");
                    Report((clickSent ? "已发送挑战入口点击，未确认入场" : "未发出挑战入口点击") +
                        "，仍在图鉴，尝试下一个巢穴", warning: true);
                }
                else
                    Fail(clickSent ? "点击后未识别到开始挑战或传送按钮。" : "未确认目标行的挑战入口。");
                return false;
            }
            if (!FightFromEntry(target, feature, button, reopenBook: true)) return false;
            return captureDone || !captureStopRequested && VerifyNestProgress(target);
        }

        internal bool RunSelectedNest(string name, Rectangle entry)
        {
            int route = world.InputRouteVersion;
            string feature = null;
            Rectangle button = Rectangle.Empty;
            if (!world.ClickMenuTransition(route, () => FindCultivationNestEntry(name, entry, route),
                () => ReadTeamOrTravel(out feature, out button), 8000, 500))
            {
                if (!token.IsCancellationRequested)
                    Fail("点击培养目标的巢穴入口后未识别到入场按钮。");
                return false;
            }
            var target = new NestTarget { Key = "培养目标/" + name, ScanKey = "培养目标/" + name, Named = true, Tab = NestTab.Purification };
            return FightFromEntry(target, feature, button);
        }

        private Rectangle FindCultivationNestEntry(string name, Rectangle previous, int route)
        {
            if (world.InputRouteVersion != route || !CultivationVision.IsPage(world.Read(.10, .12, .70, .26)))
                return Rectangle.Empty;
            var lines = world.Read(.35, .12, .99, .95);
            if (!world.Captured || world.InputRouteVersion != route) return Rectangle.Empty;
            foreach (CultivationEchoRow row in CultivationVision.EchoRows(lines, new Size(world.Frame.Width, world.Frame.Height)))
                if (!row.Tacet && !row.Done && row.Name == name && Rectangle.Inflate(previous, 12, 12).IntersectsWith(row.Button))
                    return row.Button;
            return Rectangle.Empty;
        }

        private bool ReadTeamOrTravel(out string feature, out Rectangle button)
        {
            if (FindNestEntry(world.Vision, world.Frame, out feature, out button))
                return true;
            Rectangle tab;
            bool selected;
            if (world.TeamHud || world.Vision.FindGuidebookBoss(world.Frame, out tab, out selected) ||
                !NestMath.TryChallengeText(world.Read(.63, .83, 1, .99), out button))
                return false;
            feature = TeamStart;
            return true;
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
                int interactions = 0;
                int route = world.InputRouteVersion;
                while (!token.IsCancellationRequested)
                {
                    if (!world.Tick() || world.InputRouteVersion != route || !world.VerifiedWorld ||
                        world.Ui == null || !world.Ui.FPrompt)
                        break;
                    RunLog.Info(LogArea, "传送后识别到交互提示，尝试进入巢穴，第 " + (++interactions) + " 次");
                    if (!world.Key('F', route: route)) break;
                    world.Sleep(1000);
                    if (!WaitTeam(40000)) break;
                }
                if (token.IsCancellationRequested) return false;
                if (!world.Tick() || world.InputRouteVersion != route || !world.VerifiedWorld)
                {
                    Fail("巢穴入场交互后未确认原输入模式和游戏世界。");
                    return false;
                }
                world.WaitUntil(() => HasEnemies, 1500);
            }

            return FightCurrentNestAndExit(target, team, reopenBook);
        }

        private bool FightCurrentNestAndExit(NestTarget target, bool team, bool reopenBook = false)
        {
            while (!token.IsCancellationRequested)
            {
                world.AllowShortEnemyBars = false;
                if (!FightAndCollect(target, team, reopenBook)) return false;
                if (!team)
                {
                    if (world.WaitUntil(() => world.VerifiedWorld, 10000)) return true;
                    if (!token.IsCancellationRequested) Fail("离开巢穴前未能确认当前游戏画面。");
                    return false;
                }

                int route = world.InputRouteVersion;
                NestTeamExitState state = WaitForTeamExit(route);
                if (state == NestTeamExitState.Quiet) state = LeaveTeamNest(route);
                if (state == NestTeamExitState.Quiet) return true;
                if (state == NestTeamExitState.Unverified)
                {
                    if (!token.IsCancellationRequested)
                    {
                        CombatUnresolved = true;
                        RecordFrame("after-nightmare-team-exit-unverified");
                        Fail("未核实巢穴战斗结束或退场后的画面，保留当前进度。");
                    }
                    return false;
                }
                Report("退场前重新发现敌人，留在当前巢穴继续战斗");
            }
            return false;
        }

        private NestTeamExitState WaitForTeamExit(int route)
        {
            var check = new NestTeamExitCheck();
            var wait = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && wait.ElapsedMilliseconds < 5500)
            {
                bool captured = world.Tick();
                if (world.InputRouteVersion != route) break;
                NestTeamExitState state = check.Observe(world.Hud, world.Ui, captured && world.VerifiedWorld,
                    wait.ElapsedMilliseconds);
                if (state != NestTeamExitState.Unverified) return state;
                token.WaitHandle.WaitOne(70);
            }
            return NestTeamExitState.Unverified;
        }

        private bool FightAndCollect(NestTarget target, bool team, bool reopenBook)
        {
            int approach = team || target == null || world.AllowShortEnemyBars ? 0 : 10000;
            int standAloneDeaths = 0;
            bool fought = false, unconfirmedPickup = false, retriedOpening = false, retriedActiveBattle = false;
            while (!token.IsCancellationRequested)
            {
                Report(approach > 0 ? "接近巢穴并尝试锁敌" : "等待锁定巢穴敌人");
                int enemyWait = fought ? NextWaveWaitMs : 10000;
                RunLog.Info(LogArea, string.Format("探测巢穴敌人。队伍挑战={0}, 已完成一波={1}, 接近时限={2}ms, 搜敌时限={3}ms",
                    team, fought, approach, enemyWait));
                bool started = false;
                CombatOutcome outcome = CombatRunner.Engage(world, settings, token, 0, enemyWait,
                    seekTargets: true, approachMs: approach, onCombatStarted: () =>
                    {
                        started = true;
                        retriedOpening = false;
                        Report("战斗中");
                    },
                    captureDuringCombat: options.CaptureOnce && !captureStopRequested ? WatchCombatCapture() : null,
                    openWorldAutoCombat: false, returnRevivePrompt: true, recordEvidence: recordEvidence);
                approach = 0;
                RunLog.Info(LogArea, "巢穴战斗结果 " + outcome);
                if (token.IsCancellationRequested)
                    return false;
                if (NestMath.CanRetryUnstartedCombat(outcome, started, retriedOpening) &&
                    world.WaitUntil(() => world.VerifiedWorld && HasEnemies, 2500))
                {
                    retriedOpening = true;
                    Report("已看到巢穴敌人，留在当前巢穴重新锁敌");
                    if (settings.Source == GameSource.AutoLocal && world.Window.Mode == GameSource.Background)
                        world.TryRecoverForegroundCapture();
                    continue;
                }
                if (token.IsCancellationRequested)
                    return false;
                if (!started && (outcome == CombatOutcome.Unverified ||
                    outcome == CombatOutcome.NoCombat && (retriedOpening || world.Tick() && world.VerifiedWorld && HasEnemies)))
                {
                    RecordFrame("nightmare-combat-unverified");
                    CombatUnresolved = true;
                    Fail("当前巢穴未能确认开战，已保留未完成进度。");
                    return false;
                }
                if (outcome == CombatOutcome.Captured)
                {
                    Report(captureDone ? "战斗中已确认捕获声骸，结束梦魇巢穴任务" :
                        "已发现参考捕获结束信号，停止扫描；本次新声骸尚未核实", warning: !captureDone);
                    break;
                }
                if (outcome == CombatOutcome.NoCombat)
                {
                    if (team && !fought)
                    {
                        CombatUnresolved = true;
                        RecordFrame("after-nightmare-team-combat-unverified");
                        Fail("未确认队伍挑战开战，保留当前副本。");
                        return false;
                    }
                    // combat_once(..., raise_if_not_found=False) 返回 need_find=False：
                    // 不搜掉落，但仍等待通知/3 秒结算，再按模式判断下一波。
                    if (options.CaptureOnce)
                    {
                        bool notice = world.WaitUntil(() => world.TeamHud && FarmVision.HasEchoReceipt(world.Frame), 3000);
                        if (!notice) world.Sleep(3000);
                        if (!world.Tick() || !world.VerifiedWorld)
                        {
                            Fail("未找到战斗且未确认返回游戏画面。");
                            return false;
                        }
                        captureStopRequested = true;
                        Report("未找到战斗，按参考结束本次单捕；未确认获取", warning: true);
                    }
                    else
                    {
                        world.Sleep(3000);
                        if (WaitForNextWave())
                        {
                            fought = true;
                            RunLog.Info(LogArea, "未开战轮次之后侦测下一波，继续当前巢穴");
                            continue;
                        }
                    }
                    break;
                }
                if (outcome == CombatOutcome.Timeout || outcome == CombatOutcome.Unverified)
                {
                    RecordFrame("nightmare-combat-unverified");
                    int route = world.InputRouteVersion;
                    if (started && !retriedActiveBattle && world.WaitUntil(() =>
                        world.InputRouteVersion == route && CanResumeInNest(team) && HasEnemies, 1600))
                    {
                        retriedActiveBattle = true;
                        Report("原挑战仍有敌人，留在原处再尝试战斗一次");
                        continue;
                    }
                    if (team || !world.Tick() || !world.VerifiedWorld || HasEnemies)
                    {
                        CombatUnresolved = true;
                        Fail("当前巢穴未确认清场，停止在当前进度。");
                        return false;
                    }
                    uncertainFightNests++;
                    Report("未核实战斗完成，但当前已确认无敌人的游戏画面，按参考检查掉落", warning: true);
                }
                if (outcome == CombatOutcome.Dead || outcome == CombatOutcome.RevivePrompt)
                {
                    string nestKey = target == null ? null :
                        !options.CaptureOnce && target.Named ? target.Key : target.ScanKey;
                    int count = standAloneDeaths;
                    if (nestKey != null) deaths.TryGetValue(nestKey, out count);
                    bool reviveHere = team || !reopenBook || outcome == CombatOutcome.RevivePrompt;
                    if (reviveHere)
                    {
                        bool revived;
                        if (!TryReviveInNest(team, count, out revived)) return false;
                        if (!revived) continue;
                    }
                    else
                    {
                        int route = world.InputRouteVersion;
                        if (!world.Tick() || world.InputRouteVersion != route)
                            return StopUnresolvedRevival("无法在原输入路线重新核实倒下画面，保留当前巢穴。");
                        if (CanResumeInNest(team))
                        {
                            Report("队伍已恢复，留在当前巢穴继续战斗");
                            continue;
                        }
                        if (CombatRunner.ReadDeathDialog(world.Frame, world.Ocr) != CombatDeathDialog.Defeat)
                            return StopUnresolvedRevival("未能重新核实战斗失败画面，保留当前巢穴。");
                    }
                    count++;
                    if (nestKey == null) standAloneDeaths = count;
                    else deaths[nestKey] = count;
                    RunLog.Warn(LogArea, "本轮在同一巢穴确认倒下 " + count + " 次");
                    if (!reviveHere)
                    {
                        RecoverScannedNest(target, count);
                        return false;
                    }
                    continue;
                }
                fought = true;
                retriedActiveBattle = false;
                if (outcome == CombatOutcome.Finished) completedFights++;
                if (team && captureStopRequested) break;
                world.AllowShortEnemyBars = true;
                int receiptsBefore = confirmedReceipts;
                if (options.CaptureOnce && world.WaitUntil(() => world.TeamHud && FarmVision.HasEchoReceipt(world.Frame), 3000))
                {
                    captureStopRequested = true;
                    Report("战斗后检测到获取通知，按参考结束单捕；未核实该通知是否为本次新获取", warning: true);
                    break;
                }
                // 原版在找掉落之前固定 sleep(3)，避免用首次敌人帧提前结束等待。
                world.Sleep(3000);
                if (world.Tick() && HasEnemies)
                {
                    if (options.CaptureOnce)
                    {
                        unconfirmedPickup = true;
                        break;
                    }
                    RunLog.Info(LogArea, "等待掉落时发现敌人，立即恢复战斗");
                    continue;
                }
                Report("查找战斗掉落的声骸");
                bool dropped = CollectEcho();
                if (!dropped && target != null && !team && CanSearchForEcho())
                {
                    RunLog.Warn(LogArea, "首次搜索未确认吸收，现场也没有交互提示，重新传送至同一巢穴后再搜一次");
                    dropped = RetryAfterTeleport(target);
                    if (Failed)
                        return false;
                }
                if (token.IsCancellationRequested)
                    return false;
                bool verifiedPickup = confirmedReceipts > receiptsBefore;
                if (options.CaptureOnce && dropped)
                {
                    captureStopRequested = true;
                    captureDone = verifiedPickup;
                    unconfirmedPickup |= !verifiedPickup;
                    Report(verifiedPickup ? "已确认捕获声骸，结束梦魇巢穴任务" : "已投递有效拾取，按参考结束单捕；声骸获取未核实", warning: !verifiedPickup);
                    break;
                }
                if (!verifiedPickup)
                {
                    if (world.Tick() && HasEnemies)
                    {
                        if (options.CaptureOnce)
                        {
                            unconfirmedPickup = true;
                            break;
                        }
                        RunLog.Info(LogArea, "拾取暂停，当前仍有敌人，继续战斗");
                        continue;
                    }
                    unconfirmedPickup = true;
                    Report("本轮未确认吸收，检查巢穴是否还有敌人", warning: true);
                }
                else
                {
                    RunLog.Info(LogArea, "本轮已确认吸收声骸");
                }
                // 参考 _should_continue_combat_after_pickup：单次捕获无论拾取
                // 是否成功，都不进入当前聚落的下一波战斗；未成功由外层扫描继续。
                if (!NestMath.ShouldContinueCombatAfterPickup(options.CaptureOnce, WaitForNextWave)) break;
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
            }, options.CaptureOnce ? 3000 : 8000);
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
                excludedNests.Add(!options.CaptureOnce && target.Named ? target.Key : target.ScanKey);
                exhaustedDeaths++;
                RecordFrame("nightmare-death-limit");
                Report("当前巢穴连续三次倒下，跳过并继续扫描下一个巢穴。", warning: true);
            }
            else
            {
                Report("已确认回血，重新打开图鉴继续扫描巢穴。");
            }
        }

        private bool TryReviveInNest(bool team, int deathsAlreadyRecovered, out bool revived)
        {
            revived = false;
            int route = world.InputRouteVersion;
            int playableSamples = 0;
            Rectangle previous = Rectangle.Empty, confirmed = Rectangle.Empty;
            var watch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && watch.ElapsedMilliseconds < 3500)
            {
                bool captured = world.Tick();
                if (world.InputRouteVersion != route) break;
                if (captured && CanResumeInNest(team))
                {
                    previous = Rectangle.Empty;
                    if (++playableSamples >= 2)
                    {
                        Report("队伍已恢复，留在当前巢穴继续战斗");
                        return true;
                    }
                }
                else
                {
                    playableSamples = 0;
                    Rectangle current = captured && world.Ui != null && world.Ui.Death ? world.Ui.DeathBox : Rectangle.Empty;
                    if (!current.IsEmpty && !previous.IsEmpty && previous.IntersectsWith(current) &&
                        CombatRunner.ReadDeathDialog(world.Frame, world.Ocr) == CombatDeathDialog.ReviveItems)
                    {
                        confirmed = current;
                        break;
                    }
                    previous = current;
                }
                token.WaitHandle.WaitOne(70);
            }

            if (token.IsCancellationRequested) return false;
            if (confirmed.IsEmpty)
                return StopUnresolvedRevival("未核实可用的原地复苏按钮，保留当前巢穴。");
            if (!settings.AutoRevive)
                return StopUnresolvedRevival("已确认复苏界面，自动复活未启用，保留当前巢穴。");
            if (deathsAlreadyRecovered >= 2)
            {
                exhaustedDeaths++;
                return StopUnresolvedRevival("当前巢穴连续三次倒下，保留当前巢穴。");
            }

            RecordFrame("after-nightmare-revive-before");
            if (!world.Tick() || world.InputRouteVersion != route)
                return StopUnresolvedRevival("复苏前无法核实原输入路线，保留当前巢穴。");
            if (CanResumeInNest(team))
            {
                Report("复苏前已恢复队伍，留在当前巢穴继续战斗");
                return true;
            }
            if (world.Ui == null || !world.Ui.Death || !confirmed.IntersectsWith(world.Ui.DeathBox) ||
                CombatRunner.ReadDeathDialog(world.Frame, world.Ocr) != CombatDeathDialog.ReviveItems ||
                !world.ClickMenuButton(world.Ui.DeathBox, route: route))
                return StopUnresolvedRevival("未能确认原地复苏点击，保留当前巢穴。");

            playableSamples = 0;
            if (world.WaitUntil(() =>
            {
                if (world.InputRouteVersion != route) return true;
                playableSamples = CanResumeInNest(team) ? playableSamples + 1 : 0;
                return playableSamples >= 2;
            }, 30000) && world.InputRouteVersion == route && playableSamples >= 2)
            {
                revived = true;
                RecordFrame("after-nightmare-revived");
                Report("已确认原地复苏，留在当前巢穴继续战斗");
                return true;
            }
            return StopUnresolvedRevival("原地复苏后未确认恢复队伍，保留当前巢穴。");
        }

        private bool CanResumeInNest(bool team)
        {
            return NestMath.IsPlayableTeam(world.Hud, world.Ui, world.VerifiedWorld) &&
                (!team || world.InRealm || !world.Ui.OpenWorld);
        }

        private bool StopUnresolvedRevival(string message)
        {
            if (!token.IsCancellationRequested)
            {
                CombatUnresolved = true;
                RecordFrame("after-nightmare-revive-unverified");
                Fail(message);
            }
            return false;
        }

        private bool VerifyNestProgress(NestTarget target)
        {
            if (!OpenBook())
                return false;
            NestTarget current = FindNestAgain(target);
            if (token.IsCancellationRequested)
                return false;
            if (current == null)
            {
                if (!Failed) RecordNoProgress(target, "未读到同一聚落的进度");
                return false;
            }

            RecordFrame("nightmare-progress-after");
            RunLog.Info(LogArea, string.Format("核对同一聚落 {0}：{1}/{2} → {3}/{4}",
                target.Key, target.Defeated, target.Total, current.Defeated, current.Total));
            if (current.Defeated == current.Total)
            {
                unconfirmedProgress.Remove(target.Key);
                noProgressVisits.Remove(target.Key);
                RecordFrame("nightmare-cleared");
                Report("已从图鉴读到清场 " + current.Total + "/" + current.Total);
                return true;
            }
            unconfirmedProgress.Add(target.Key);
            if (current.Defeated > target.Defeated)
            {
                noProgressVisits.Remove(target.Key);
                Report("聚落进度 " + current.Defeated + "/" + current.Total + "，继续扫描未完成聚落");
            }
            else
                RecordNoProgress(target, "聚落进度仍未增加（" + current.Defeated + "/" + current.Total + "）");
            return false;
        }

        private void RecordNoProgress(NestTarget target, string reason)
        {
            unconfirmedProgress.Add(target.Key);
            int visits;
            noProgressVisits.TryGetValue(target.Key, out visits);
            noProgressVisits[target.Key] = ++visits;
            if (visits >= 2)
            {
                excludedNests.Add(target.Named ? target.Key : target.ScanKey);
                Report(reason + "，两次核对无进展，保留该巢穴未完成并扫描其他目标", warning: true);
            }
            else
                Report(reason + "，保留该巢穴并允许再核对一次", warning: true);
        }

        private NestTeamExitState LeaveTeamNest(int route)
        {
            if (!world.Tick() || world.InputRouteVersion != route ||
                !NestMath.IsPlayableTeam(world.Hud, world.Ui, world.VerifiedWorld))
                return NestTeamExitState.Unverified;
            if (HasEnemies) return NestTeamExitState.Enemies;
            if (world.Ui.OpenWorld && !world.InRealm) return NestTeamExitState.Quiet;

            RecordFrame("after-nightmare-team-exit-ready");
            if (!world.Tick() || world.InputRouteVersion != route ||
                !NestMath.IsPlayableTeam(world.Hud, world.Ui, world.VerifiedWorld))
                return NestTeamExitState.Unverified;
            if (HasEnemies) return NestTeamExitState.Enemies;
            if (world.Ui.OpenWorld && !world.InRealm) return NestTeamExitState.Quiet;
            Report("已核实无敌人，确认队伍挑战退场");
            if (!world.Key((char)0x1B, route: route))
                return NestTeamExitState.Unverified;

            Rectangle confirm = Rectangle.Empty;
            bool resumedCombat = false;
            if (!world.WaitUntil(() =>
            {
                if (world.InputRouteVersion != route) return true;
                resumedCombat = world.VerifiedWorld && HasEnemies;
                if (resumedCombat) return true;
                confirm = FarmVision.ReadRealmExitDialog(world.Frame, world.Ocr).Exit;
                return !confirm.IsEmpty;
            }, 5000) || world.InputRouteVersion != route)
                return NestTeamExitState.Unverified;
            if (resumedCombat) return NestTeamExitState.Enemies;

            RecordFrame("after-nightmare-team-exit-confirm");
            if (!world.Tick() || world.InputRouteVersion != route || HasEnemies)
                return NestTeamExitState.Unverified;
            Rectangle current = FarmVision.ReadRealmExitDialog(world.Frame, world.Ocr).Exit;
            if (current.IsEmpty || !confirm.IntersectsWith(current) || !world.ClickMenuButton(current, route: route))
                return NestTeamExitState.Unverified;

            var arrival = new NestTravelRoute(settings.Source, world.Window.Handle, world.Window.ProcessId,
                world.Bounds.Size, world.Window.Mode, route);
            int ready = 0;
            if (!world.WaitUntil(() =>
            {
                int previousRoute = arrival.Version;
                if (!arrival.Observe(world.Window.Handle, world.Window.ProcessId, world.Bounds.Size,
                    world.Window.Mode, world.InputRouteVersion))
                {
                    ready = 0;
                    return true;
                }
                if (arrival.Version != previousRoute) ready = 0;
                ready = world.VerifiedWorld && world.Ui.OpenWorld && !world.InRealm ? ready + 1 : 0;
                return ready >= 2;
            }, 120000) || ready < 2)
                return NestTeamExitState.Unverified;
            RecordFrame("after-nightmare-team-exit-world");
            return NestTeamExitState.Quiet;
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
            if (!OpenBook())
                return false;
            if (FindNestAgain(target) == null)
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
            if (prompt != EchoPrompt.Selected) return false;
            int route = world.InputRouteVersion;
            var receipt = new PickupReceiptCheck(FarmVision.HasEchoReceipt(world.Frame));
            // 参考每次 walk_find_echo / pick_echo 只按一次 F；旧通知不阻止合法吸收，
            // Result 与实际回执证据分离，防止误把旧通知计为这次获得。
            pickupSubmissionPending = true;
            if (!world.Key('F', route: route)) return false;
            Report("正在吸收声骸");
            const int receiptWindowMs = 2500;
            long sentAt = world.Now;
            bool confirmed = false, wrongClaim = false;
            world.WaitUntil(delegate
            {
                if (world.InputRouteVersion != route) return true;
                if (world.Ui == null) return false;
                if (world.Ui.Claim)
                {
                    wrongClaim = true;
                    return true;
                }
                if (!world.TeamHud || world.Ui.Book || world.Ui.Death) return false;
                confirmed = receipt.Observe(FarmVision.HasEchoReceipt(world.Frame));
                return confirmed;
            }, receiptWindowMs);
            world.Sleep((int)Math.Max(0, 1000 - (world.Now - sentAt)));
            if (token.IsCancellationRequested || !world.Tick() || world.InputRouteVersion != route) return false;
            if (wrongClaim || world.Ui != null && world.Ui.Claim)
            {
                pickupSubmissionPending = false;
                RunLog.Warn(LogArea, "按 F 后打开的是领取界面，按 Esc 返回");
                if (world.Ui != null && world.Ui.Claim) world.Key((char)0x1B, route: route);
                return false;
            }
            if (confirmed)
            {
                pickupSubmissionPending = false;
                confirmedReceipts++;
                RecordFrame("nightmare-absorb-confirmed");
                Report("已确认吸收声骸");
            }
            bool accepted = NestMath.AcceptPickupFlow(true, true, world.TeamHud && world.Ui != null && !world.Ui.Book && !world.Ui.Death, false);
            if (accepted && !confirmed)
                Report("已投递拾取并确认未打开领取弹窗，实际新声骸回执尚未核实", warning: true);
            return confirmed || accepted;
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
                    pickupSubmissionPending = false;
                    confirmedReceipts++;
                    captureDone = true;
                    captureStopRequested = true;
                    RecordFrame("nightmare-combat-absorb-confirmed");
                    return true;
                }
                if (CurrentEchoPrompt() == EchoPrompt.Selected)
                {
                    pickupSubmissionPending = true;
                    if (world.Key('F', route: route))
                    {
                        receipt = new PickupReceiptCheck(visible);
                        expires = now + 2500;
                    }
                }
                if (visible)
                {
                    captureStopRequested = true;
                    return true;
                }
                return false;
            };
        }

        private bool CollectVisibleEchoes()
        {
            return CanPickupEcho() && CurrentEchoPrompt(fresh: true) != EchoPrompt.None && Pickup();
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
            int route = world.InputRouteVersion;
            var travelRoute = new NestTravelRoute(settings.Source, world.Window.Handle, world.Window.ProcessId,
                world.Bounds.Size, world.Window.Mode, route);
            bool sent = ClickTravelButton(feature, button, travelRoute);
            route = travelRoute.Version;
            int sameMapReads = 0;
            if (sent && !world.WaitUntil(() =>
            {
                Rectangle latest;
                if (world.InputRouteVersion != route || ReadTravelView(feature, out latest) != NestTravelView.Button ||
                    !Rectangle.Inflate(button, 24, 24).IntersectsWith(latest)) return true;
                sameMapReads++;
                return false;
            }, 4500) && sameMapReads >= 2)
            {
                // Only repeat input if the same map button stayed visible
                // throughout the first wait. Unknown/loading is progress.
                if (!token.IsCancellationRequested && world.InputRouteVersion == route &&
                    ClickTravelButton(feature, button, travelRoute, retry: true))
                    Report("仍在原传送界面，已重试巢穴传送");
            }
            NestTravelView view = WaitForTravelView(feature, travelRoute, sent ? 120000 : 4000,
                handleConfirmation: sent, stopAtMap: !sent);
            route = travelRoute.Version;
            if (token.IsCancellationRequested)
                return false;
            if (view == NestTravelView.World && sent)
            {
                visibleBookPage = null;
                RunLog.Info(LogArea, "已确认离开传送界面，世界和队伍画面已恢复");
                return true;
            }
            string nestKey = !options.CaptureOnce && target.Named ? target.Key : target.ScanKey;
            excludedNests.Add(nestKey);
            travelSkips.Add(nestKey);
            Rectangle action;
            if ((view == NestTravelView.Button || view == NestTravelView.Confirm) &&
                world.Tick() && world.InputRouteVersion == route && ReadTravelView(feature, out action) == view)
            {
                RecordFrame("nightmare-travel-unconfirmed");
                RunLog.Warn(LogArea, (sent ? "等待到达巢穴超时" : "尚未投递巢穴传送") + "，仍在已确认的传送界面，返回上一级");
                if (world.Key((char)0x1B, route: route))
                    view = WaitForTravelView(feature, travelRoute, 8000);
            }
            if (token.IsCancellationRequested)
                return false;
            if (view == NestTravelView.World || view == NestTravelView.Guidebook || view == NestTravelView.Terminal)
            {
                visibleBookPage = null;
                RecordFrame("nightmare-travel-skipped");
                Report("传送未到达目标，已返回可识别界面，继续扫描", warning: true);
                return false;
            }
            Fail("巢穴传送进度未确认，最后画面=" + DescribeTravelView(view) + "。请确认地图、传送确认或加载状态。");
            return false;
        }

        private bool ClickTravelButton(string feature, Rectangle previous, NestTravelRoute travelRoute, bool retry = false)
        {
            var samples = new NestTravelObservation();
            var watch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && watch.ElapsedMilliseconds < 4500)
            {
                if (!world.Tick())
                    samples.ResetSamples();
                else
                {
                    int priorRoute = travelRoute.Version;
                    if (!travelRoute.Observe(world.Window.Handle, world.Window.ProcessId, world.Bounds.Size,
                        world.Window.Mode, world.InputRouteVersion)) break;
                    int route = travelRoute.Version;
                    if (route != priorRoute) samples.ResetSamples();
                    Rectangle button;
                    NestTravelView view = ReadTravelView(feature, out button);
                    if (samples.ObserveButton(view, previous, button))
                    {
                        Point point;
                        bool matchedText = ReadTravelButtonText(world, button, out point);
                        Rectangle latest;
                        if (world.Tick() && world.InputRouteVersion == route &&
                            ReadTravelView(feature, out latest) == NestTravelView.Button &&
                            Rectangle.Inflate(button, 12, 12).IntersectsWith(latest))
                        {
                            if (!matchedText)
                                point = new Point(latest.X + latest.Width / 2, latest.Y + latest.Height / 2);
                            bool sent = world.ClickMenuButton(point, retry: retry, route: route);
                            RunLog.Info(LogArea, string.Format("传送按钮输入。方式={0}，位置={1}，模式={2}，投递={3}，重试={4}",
                                matchedText ? "按钮文字" : "模板", point, world.Window.Mode, sent, retry));
                            return sent;
                        }
                        samples.ResetSamples();
                    }
                }
                token.WaitHandle.WaitOne(90);
            }
            if (!token.IsCancellationRequested)
                RunLog.Warn(LogArea, "等待已识别的巢穴传送按钮稳定，仍未确认可点击，耗时 " + watch.ElapsedMilliseconds + "ms");
            return false;
        }

        private static bool ReadTravelButtonText(WorldSession world, Rectangle button, out Point point)
        {
            Rectangle row = Rectangle.Inflate(button, 0, Math.Max(12, button.Height / 2));
            int height = world.Frame.Height;
            List<OcrLine> lines = world.Read(.60, Math.Max(0, row.Top) / (double)height,
                .99, Math.Min(height, row.Bottom) / (double)height);
            return TryTravelButtonText(lines, button, world.Frame.Width, out point);
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

        private NestTravelView WaitForTravelView(string feature, NestTravelRoute travelRoute, int timeoutMs,
            bool handleConfirmation = false, bool stopAtMap = false)
        {
            var observation = new NestTravelObservation();
            NestTravelView last = NestTravelView.Unknown;
            int samples = 0;
            bool sameWindow = true;
            var watch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && watch.ElapsedMilliseconds < timeoutMs)
            {
                Rectangle action = Rectangle.Empty;
                int previousRoute = travelRoute.Version;
                bool captured = world.Tick();
                if (captured)
                {
                    sameWindow = travelRoute.Observe(world.Window.Handle, world.Window.ProcessId, world.Bounds.Size,
                        world.Window.Mode, world.InputRouteVersion);
                    if (!sameWindow) break;
                    if (travelRoute.Version != previousRoute)
                    {
                        observation.ResetSamples();
                        RunLog.Info(LogArea, "同一游戏窗口切换前后台，继续等待巢穴传送");
                    }
                }
                int route = travelRoute.Version;
                NestTravelView current = captured ? ReadTravelView(feature, out action) : NestTravelView.Unknown;
                samples++;
                if (current != last)
                    RunLog.Info(LogArea, "传送画面切换=" + DescribeTravelView(current));
                last = current;
                observation.Observe(current);
                if (handleConfirmation && observation.CanConfirm)
                {
                    Report("确认巢穴传送");
                    observation.ConfirmationAttempted();
                    bool sawConfirm = false;
                    if (!world.ClickMenuTransition(route, () =>
                    {
                        Rectangle control;
                        if (ReadTravelView(feature, out control) != NestTravelView.Confirm ||
                            !Rectangle.Inflate(action, 12, 12).IntersectsWith(control))
                            return Rectangle.Empty;
                        sawConfirm = true;
                        return control;
                    }, () =>
                    {
                        Rectangle ignored;
                        return sawConfirm && ReadTravelView(feature, out ignored) != NestTravelView.Confirm;
                    }, 3500, 500))
                        RunLog.Warn(LogArea, "未确认巢穴传送弹窗已响应，继续核对当前画面");
                }
                if (observation.Arrived || !handleConfirmation &&
                    (observation.StableView == NestTravelView.Terminal || observation.StableView == NestTravelView.Guidebook))
                    break;
                if (stopAtMap && (observation.StableView == NestTravelView.Button ||
                    observation.StableView == NestTravelView.Confirm))
                    break;
                token.WaitHandle.WaitOne(60);
            }
            NestTravelView view = sameWindow && world.InputRouteVersion == travelRoute.Version ? observation.StableView : NestTravelView.Unknown;
            RunLog.Info(LogArea, string.Format("传送画面检查结果={0}，采样={1}，耗时={2}ms",
                DescribeTravelView(view), samples, watch.ElapsedMilliseconds));
            return view;
        }

        private NestTravelView ReadTravelView(string feature, out Rectangle action)
        {
            FarmUi ui = world.Ui;
            NestTravelView controls = ReadTravelControls(world.Vision, world.Frame, ui, feature, world.Ocr, out action);
            if (controls != NestTravelView.Unknown || ui == null || ui.Death || ui.Challenge ||
                ui.MonthlyCard || ui.ClaimReward || ui.Claim || ui.SkipDialog)
                return controls;
            if (world.VerifiedWorld)
                return NestTravelView.World;
            bool selected;
            if ((ui.Book || ui.BookMonsters) && world.Vision.InspectGuidebook(world.Frame, out action, out selected))
                return NestTravelView.Guidebook;
            if (world.Vision.IsTerminal(world.Frame))
                return NestTravelView.Terminal;
            return NestTravelView.Unknown;
        }

        internal static NestTravelView ReadTravelControls(FarmVision vision, PixelFrame frame, FarmUi ui,
            string feature, OcrReader ocr, out Rectangle action)
        {
            action = Rectangle.Empty;
            if (ui == null || ui.Death || ui.Challenge || ui.MonthlyCard || ui.ClaimReward)
                return NestTravelView.Unknown;

            Rectangle mapButton, confirm;
            bool mapVisible = vision.FindLabel(frame, feature, .70, out mapButton);
            // The reward icon on an unobstructed map can match the loose
            // confirmation template. When both controls match, require
            // the actual dialog label or the dialog's paired checkbox.
            if (!ui.ConfirmBox.IsEmpty && vision.FindOptionalButton(frame, .78, out confirm) &&
                (ui.SkipDialog || !mapVisible || HasTravelConfirmationText(frame, confirm, ocr)))
            {
                action = confirm;
                return NestTravelView.Confirm;
            }
            if (mapVisible && !ui.Claim && !ui.SkipDialog)
            {
                action = mapButton;
                return NestTravelView.Button;
            }
            return NestTravelView.Unknown;
        }

        private static bool HasTravelConfirmationText(PixelFrame frame, Rectangle control, OcrReader ocr)
        {
            if (ocr == null) return false;
            Rectangle area = Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height),
                Rectangle.Inflate(control, frame.Width / 24, 12));
            try
            {
                return !DailyClaimVision.UniqueButton(ocr.Read(frame, area), "确认", "確認", "确定", "確定", "Confirm").IsEmpty;
            }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
            {
                return false;
            }
        }

        private static string DescribeTravelView(NestTravelView view)
        {
            switch (view)
            {
                case NestTravelView.Button: return "原地图传送按钮";
                case NestTravelView.Confirm: return "确认弹窗";
                case NestTravelView.World: return "世界与队伍";
                case NestTravelView.Guidebook: return "索拉指南";
                case NestTravelView.Terminal: return "终端";
                default: return "加载或未知画面";
            }
        }

        private bool TeamChallenge(Rectangle button)
        {
            int route = world.InputRouteVersion;
            bool sawStart = false;
            if (!world.ClickMenuTransition(route, () =>
            {
                string feature;
                Rectangle current;
                if (!ReadTeamOrTravel(out feature, out current) || feature != TeamStart ||
                    !Rectangle.Inflate(button, 16, 16).IntersectsWith(current))
                    return Rectangle.Empty;
                sawStart = true;
                return current;
            }, () =>
            {
                if (world.VerifiedWorld || world.Ui != null && world.Ui.SkipDialog) return true;
                string feature;
                Rectangle current;
                return sawStart && !ReadTeamOrTravel(out feature, out current);
            }, 3000, 500))
            {
                if (!token.IsCancellationRequested) Fail("未确认开始挑战按钮已响应。");
                return false;
            }
            world.WaitForOptionalDialog(route);
            return !token.IsCancellationRequested && world.InputRouteVersion == route;
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

        private bool AtKnownDailyBoundary()
        {
            if (!world.Tick()) return false;
            int route = world.InputRouteVersion;
            Rectangle tab;
            bool selected;
            GuideView view;
            guide.InspectBookView(out tab, out selected, out view);
            if (view != GuideView.Guidebook && view != GuideView.World) return false;
            if (!world.Tick() || world.InputRouteVersion != route) return false;
            guide.InspectBookView(out tab, out selected, out view);
            return view == GuideView.Guidebook || view == GuideView.World;
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
