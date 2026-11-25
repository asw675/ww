using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    public enum DomainKind { Tacet, Forgery, Simulation, Cultivation }

    public sealed class DomainOptions
    {
        public DomainKind Kind { get; set; }
        public int Index { get; set; }
        // 每类副本保留自己的序号；旧版只有 Index 时仍作为当前 Kind 的序号。
        public int? TacetIndex { get; set; }
        public int? ForgeryIndex { get; set; }
        public string Material { get; set; }
        public int MaxStamina { get; set; }

        public static readonly string[] Materials = { "共鸣者经验", "武器经验", "贝币" };

        public static DomainOptions Defaults()
        {
            return new DomainOptions { Kind = DomainKind.Tacet, Index = 1, Material = Materials[2] };
        }

        internal DomainOptions ForKind(DomainKind kind)
        {
            return new DomainOptions
            {
                Kind = kind, Index = IndexForKind(kind), TacetIndex = IndexForKind(DomainKind.Tacet),
                ForgeryIndex = IndexForKind(DomainKind.Forgery), Material = Material, MaxStamina = MaxStamina
            };
        }

        internal int IndexForKind(DomainKind kind)
        {
            int value = kind == Kind ? Index : kind == DomainKind.Tacet ? TacetIndex.GetValueOrDefault(1) :
                kind == DomainKind.Forgery ? ForgeryIndex.GetValueOrDefault(1) : 1;
            int last = kind == DomainKind.Forgery ? 20 : 21;
            return Math.Max(1, Math.Min(last, value));
        }

        internal void SetIndexForKind(DomainKind kind, int index)
        {
            if (kind == DomainKind.Tacet) TacetIndex = index;
            else if (kind == DomainKind.Forgery) ForgeryIndex = index;
            if (Kind == kind) Index = index;
        }

        public void Validate()
        {
            int last = Kind == DomainKind.Forgery ? 20 : 21;
            if (!Enum.IsDefined(typeof(DomainKind), Kind) || Kind == DomainKind.Cultivation ||
                Index < 1 || Index > last || MaxStamina < 0 ||
                TacetIndex.HasValue && (TacetIndex < 1 || TacetIndex > 21) ||
                ForgeryIndex.HasValue && (ForgeryIndex < 1 || ForgeryIndex > 20))
                throw new System.IO.InvalidDataException("体力本配置无效。");
            if (Material == null || Array.IndexOf(Materials, Material) < 0)
                Material = Materials[2];
        }
    }

    internal static class DomainMath
    {
        internal const double BookScrollBottom = .8806;
        // OK-WW BaseCombatTask.combat_once 默认等待开战 200 秒。
        internal const int WaitForCombatMs = 200000;
        // combat_once 只限制等开战，不限制已经开始的有效战斗。
        internal const int CombatTimeoutMs = 0;
        private const double BookSectionHeight = .028;

        internal static char TreasureDirection(Rectangle target, Size size, char previous, ref bool centered)
        {
            int x = target.Left + target.Width / 2, y = target.Top + target.Height / 2;
            double dx = x - size.Width * .5;
            centered |= Math.Abs(dx) <= size.Width * (previous == '\0' ? .04 : .07);
            if (!centered) return dx > 0 ? 'D' : 'A';
            double vertical = previous == 'W' ? .60 : previous == 'S' ? .45 : .50;
            return y - size.Height * .05 > size.Height * vertical ? 'S' : 'W';
        }

        internal static bool CanReapproachTacetReward(HudSnapshot hud, FarmUi ui, bool verifiedWorld)
        {
            return verifiedWorld && hud != null && hud.Valid && !hud.Locked && !hud.EnemyHealth &&
                !hud.Dead && !hud.ChallengePrompt && ui != null && !ui.OpenWorld &&
                !ui.Claim && !ui.Challenge && !ui.Death && !ui.SkipDialog && ui.ConfirmBox.IsEmpty;
        }

        internal static int StaminaCost(DomainKind kind)
        {
            return kind == DomainKind.Tacet || kind == DomainKind.Cultivation ? 60 : 40;
        }

        internal static int RewardCost(int current, int once, int mustUse, int backup)
        {
            return current >= 2 * once || (mustUse > once && current + backup >= 2 * once) ? 2 * once : once;
        }

        internal static bool CanEnter(DomainKind kind, int current, int backup, int mustUse, int once)
        {
            int total = current + backup;
            return total >= once && (kind == DomainKind.Tacet ||
                (total >= mustUse && (mustUse != 0 || current >= once)));
        }

        internal static bool CanContinue(int current, int backup, int used, int mustUse, int once)
        {
            return current + backup - used >= once && (mustUse > used || current - used >= once);
        }

        internal static Rectangle SpendControl(IList<OcrLine> lines, bool useDouble, int cost)
        {
            string[] labels = useDouble ? new[] { "双倍领取", "雙倍領取", "Claim Double" } :
                new[] { "单倍领取", "單倍領取", "Claim Once" };
            Rectangle label = DailyClaimVision.UniqueButton(lines, labels);
            if (!label.IsEmpty)
            {
                foreach (OcrLine line in lines)
                {
                    int amount;
                    if (!line.Box.IsEmpty && line.Score >= .70f && line.Box.Right <= label.Left &&
                        Math.Abs(line.Box.Top - label.Top) <= 20 &&
                        int.TryParse(OcrText.Compact(line.Text), out amount) && amount == cost)
                        return Rectangle.Union(line.Box, label);
                }
                return Rectangle.Empty;
            }
            var combined = new List<string>(labels.Length * 4);
            foreach (string text in labels)
            {
                combined.Add(cost + text);
                combined.Add(cost + "|" + text);
                combined.Add(text + cost);
                combined.Add(text + "|" + cost);
            }
            return DailyClaimVision.UniqueButton(lines, combined.ToArray());
        }

        internal static Rectangle RechallengeButton(IEnumerable<OcrLine> lines)
        {
            return DailyClaimVision.UniqueButton(lines, "重新挑战", "重新挑戰", "再次挑战", "再次挑戰", "Rechallenge");
        }

        internal static bool IsSelectedRewardPrompt(IEnumerable<OcrLine> lines, Rectangle key)
        {
            if (key.IsEmpty) return false;
            int center = key.Top + key.Height / 2;
            foreach (OcrLine line in lines)
            {
                if (line == null || line.Box.IsEmpty || line.Score < .62f || line.Box.Left < key.Right - key.Width / 3 ||
                    Math.Abs(line.Box.Top + line.Box.Height / 2 - center) > Math.Max(8, key.Height * .65))
                    continue;
                string text = OcrText.Compact(line.Text);
                if (text == "领取奖励" || text == "領取獎勵" || text == "领奖" ||
                    string.Equals(text, "ClaimReward", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "ClaimRewards", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static List<Rectangle> TacetEntryButtons(IEnumerable<OcrLine> lines, Rectangle area)
        {
            var boxes = new List<Rectangle>();
            foreach (OcrLine line in lines)
            {
                if (line == null || line.Box.IsEmpty || line.Score < .72f || !area.Contains(line.Box))
                    continue;
                string text = OcrText.Compact(line.Text);
                if (text != "直接挑战" && text != "前往" && !string.Equals(text, "DirectChallenge", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(text, "Goto", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!boxes.Exists(box => box.IntersectsWith(line.Box)))
                    boxes.Add(line.Box);
            }
            boxes.Sort((first, second) => first.Top.CompareTo(second.Top));
            return boxes;
        }

        internal static bool SameBookRows(IList<Rectangle> first, IList<Rectangle> second, int frameHeight)
        {
            if (first.Count == 0 || first.Count != second.Count) return false;
            int margin = Math.Max(3, frameHeight / 150);
            for (int i = 0; i < first.Count; i++)
                if (!Rectangle.Inflate(first[i], margin, margin).Contains(second[i]))
                    return false;
            return true;
        }

        internal static double ReferenceCategoryY(DomainKind kind)
        {
            return kind == DomainKind.Forgery ? .40 : kind == DomainKind.Simulation ? .30 :
                kind == DomainKind.Tacet ? .73 : -1;
        }

        internal static Point ReferenceCategoryPoint(DomainKind kind, Size size, Rectangle heading)
        {
            double y = ReferenceCategoryY(kind);
            if (y < 0 || heading.IsEmpty || Math.Abs(heading.Top + heading.Height / 2.0 - y * size.Height) > size.Height * .04)
                return Point.Empty;
            return new Point((int)(size.Width * .24), (int)(size.Height * y));
        }

        internal static Point VerifiedReferencePoint(Rectangle button, Point reference)
        {
            // 同一个按钮内优先原版固定坐标；布局移动时仍只操作已定位按钮。
            return button.IsEmpty ? Point.Empty : button.Contains(reference) ? reference :
                new Point(button.Left + button.Width / 2, button.Top + button.Height / 2);
        }

        internal static Point CenteredPoint(Size size, double x, double y)
        {
            double contentWidth = Math.Min(size.Width, size.Height * (16.0 / 9));
            return new Point((int)(size.Width * .5 + (x - .5) * contentWidth), (int)(size.Height * y));
        }

        internal static Rectangle BookKindButton(IEnumerable<OcrLine> lines, DomainKind kind)
        {
            switch (kind)
            {
                case DomainKind.Forgery:
                    return DailyClaimVision.UniqueButton(lines, "凝素领域", "凝素領域", "Forgery Challenge");
                case DomainKind.Simulation:
                    return DailyClaimVision.UniqueButton(lines, "模拟领域", "模擬領域", "Simulation Challenge");
                case DomainKind.Tacet:
                    return DailyClaimVision.UniqueButton(lines, "无音清剿", "無音清剿", "无音区", "無音區", "Tacet Suppression");
                case DomainKind.Cultivation:
                    return DailyClaimVision.UniqueButton(lines, "培养目标", "培養目標", "Cultivation Target");
                default:
                    return Rectangle.Empty;
            }
        }

        internal static bool IsHighlightedBookKind(PixelFrame frame, Rectangle heading)
        {
            int offset = Math.Max(4, heading.Height / 2);
            int left = heading.Right + offset, right = Math.Min(frame.Width, left + 2 * offset);
            int top = heading.Top + heading.Height / 4;
            int bottom = Math.Min(frame.Height, heading.Bottom - heading.Height / 4);
            if (heading.IsEmpty || left >= right || top < 0 || top >= bottom)
                return false;
            int light = 0;
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                    if (frame.Light(x, y) >= 150)
                        light++;
            return light * 4 >= (right - left) * (bottom - top) * 3;
        }

        internal static void ParseStamina(IEnumerable<string> texts, out int current, out int backup, out int total)
        {
            current = backup = total = 0;
            foreach (string text in texts)
            {
                string value = text ?? string.Empty;
                Match pair = Regex.Match(value, @"(?<!\d)(\d+)\s*/\s*(\d+)(?!\d)");
                if (pair.Success)
                {
                    current = int.Parse(pair.Groups[1].Value);
                    continue;
                }
                Match number = Regex.Match(value, @"(?<!\d)(\d+)(?!\d)");
                if (number.Success && backup == 0)
                    backup = int.Parse(number.Groups[1].Value);
            }
            total = current + backup;
        }

        internal static double ScrollOffset(double top, int serial, int total, int[] structure)
        {
            if (total <= 0)
                return GuidebookList.DefaultTop;
            int crossed = 0, sum = 0;
            if (structure != null)
            {
                foreach (int group in structure)
                {
                    sum += group;
                    if (serial > sum) crossed++;
                    else break;
                }
            }
            return Math.Min(BookScrollBottom, top + BookRowHeight(top, total, structure) * serial + crossed * BookSectionHeight);
        }

        internal static double BookRowHeight(double top, int total, int[] structure)
        {
            int headers = structure == null ? 0 : Math.Max(0, structure.Length - 1);
            return total > 0 ? (BookScrollBottom - top - headers * BookSectionHeight) / total : 0;
        }
    }

    internal sealed class DomainReport
    {
        internal string State;
        internal int Runs;
        internal int UsedStamina;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Port of ok-ww DomainTask / TacetTask / ForgeryTask / SimulationTask.
    internal sealed class DomainRunner : IDisposable
    {
        private enum BookDestination { None, Team, Travel }
        private static readonly int[] TacetStructure = { 4, 5, 5, 7 };
        private static readonly int[] ForgeryStructure = { 5, 5, 5, 5 };
        private readonly CombatSettings settings;
        private readonly DomainOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly bool ownsWorld;
        private readonly IProgress<DomainReport> progress;
        private readonly Action<string, PixelFrame> recordEvidence;
        private readonly int once;
        private int usedStamina, runs;
        private readonly GuidebookNavigation guide;
        private readonly WorldRecovery recovery;
        private bool healPoint;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        internal bool CompletionVerified { get; private set; }
        internal bool HasUnverifiedConsumption { get; private set; }
        internal bool NoStamina { get; private set; }
        internal int UsedStamina { get { return usedStamina; } }

        internal bool TryGetContinuationWindow(out IntPtr handle, out uint processId)
        {
            return TaskContinuation.TryGetCurrentWindow(world, out handle, out processId);
        }

        internal DomainRunner(CombatSettings settings, DomainOptions options, CancellationToken token,
            IProgress<DomainReport> progress, Action<string, PixelFrame> recordEvidence = null, WorldSession session = null)
        {
            this.settings = settings;
            this.options = options;
            this.token = token;
            this.progress = progress;
            this.recordEvidence = recordEvidence;
            ownsWorld = session == null;
            world = session ?? new WorldSession(settings, token);
            recovery = new WorldRecovery(world, value => Report(value));
            guide = new GuidebookNavigation(world, value => Report(value));
            once = DomainMath.StaminaCost(options.Kind);
        }

        internal void Run(Func<bool> openEntry = null, Func<bool> enterTacet = null)
        {
            options.Validate();
            usedStamina = 0;
            runs = 0;
            Failed = false;
            Failure = null;
            CompletionVerified = false;
            NoStamina = false;
            healPoint = false;
            RunLog.Info("体力本", string.Format("开始 · 类型={0} · 序号={1} · 目标体力={2} · 单次体力={3}",
                options.Kind, options.Index, options.MaxStamina, once));
            int mustUse = options.MaxStamina, recovery = 0;
            while (!token.IsCancellationRequested)
            {
                Report("读取体力");
                StaminaReadout available = default(StaminaReadout);
                if (openEntry != null ? !openEntry() : !OpenGuideForStamina())
                {
                    token.ThrowIfCancellationRequested();
                    CaptureSpendEvidence("stamina-guide-failed");
                    Fail(openEntry != null ? "未能打开培养目标入口。" : guide.Failure ?? "未能进入索拉指南。");
                    return;
                }
                if (!ReadEntryStamina(openEntry == null && options.Kind == DomainKind.Tacet, out available))
                {
                    token.ThrowIfCancellationRequested();
                    CaptureSpendEvidence("stamina-header-unreadable");
                    StopAfterReturningWorld("未读到可信体力数，按参考结束本次入场；未核实是否体力不足。");
                    return;
                }
                CaptureSpendEvidence("stamina-header-read");
                if (!DomainMath.CanEnter(options.Kind, available.Current, available.Backup, mustUse, once))
                {
                    if (!available.BackupKnown &&
                        DomainMath.CanEnter(options.Kind, available.Current, 4800, mustUse, once))
                    {
                        CaptureSpendEvidence("stamina-backup-unknown");
                        StopAfterReturningWorld("备用体力缺失，按参考默认值 0 不进本；未核实实际总余额。");
                        return;
                    }
                    CaptureSpendEvidence("stamina-insufficient");
                    NoStamina = usedStamina == 0;
                    CompletionVerified = true;
                    if (!EnsureWorld())
                    {
                        if (!token.IsCancellationRequested)
                            Fail("体力不足，未确认返回大世界。");
                        return;
                    }
                    Report("已确认可用体力不足，任务结束", finished: true);
                    return;
                }
                if (enterTacet == null && !SelectBookCategory(options.Kind))
                {
                    token.ThrowIfCancellationRequested();
                    CaptureSpendEvidence("stamina-category-unconfirmed");
                    Fail("已读取体力，但未确认切换到所选副本的图鉴列表。");
                    return;
                }
                CaptureSpendEvidence("stamina-entry-selected");
                Report("传送进本");
                if (!(enterTacet == null ? TeleportIntoDomain() : enterTacet() && StartTeamChallenge()))
                {
                    if (!token.IsCancellationRequested)
                    {
                        CaptureSpendEvidence("stamina-domain-entry-failed");
                        Fail("传送进副本失败，尚未开始战斗。");
                    }
                    return;
                }
                world.Sleep(1000);
                bool finished = FarmInDomain(ref mustUse);
                if (Failed || token.IsCancellationRequested)
                    return;
                if (finished)
                {
                    CompletionVerified = usedStamina > 0 || NoStamina;
                    Report("体力本结束，已核对本次领取记录", finished: true);
                    return;
                }
                recovery++;
                if (recovery >= 3)
                {
                    StopAfterReturningWorld("连续 3 次恢复，按参考结束刷取，未确认计划已完成。");
                    return;
                }
                Report("返回大世界后重新进本");
                if (!EnsureWorld())
                {
                    if (!token.IsCancellationRequested)
                        Fail("未确认返回大世界，无法重新进本。");
                    return;
                }
                world.Sleep(1000);
            }
        }

        private bool FarmInDomain(ref int mustUse)
        {
            bool tacet = options.Kind == DomainKind.Tacet;
            bool retriedUnclaimedRound = false;
            while (!token.IsCancellationRequested)
            {
                Report("进入战斗");
                if (!tacet)
                {
                    if (!WalkUntilF(4000) || !PickF())
                        return StopDomain("未确认开始挑战的交互提示。");
                    DismissWrongClaim();
                }
                CombatOutcome combat = CombatRunner.Engage(world, settings, token, DomainMath.CombatTimeoutMs, DomainMath.WaitForCombatMs,
                    seekTargets: tacet, openWorldAutoCombat: tacet);
                RunLog.Info("体力本", "战斗结果 · " + combat);
                bool rewardReached = false;
                // 原版 combat_once 吸收 perform 中的 NotInCombatException 后继续找宝箱。
                // 未核实结束时，仅允许在有效安静副本中走到实际已识别的领奖提示。
                bool canVerifyReward = combat == CombatOutcome.Unverified && world.Tick() && world.VerifiedWorld &&
                    world.InRealm && world.Hud != null && !world.Hud.Locked && !world.Hud.EnemyHealth && !world.Hud.Dead;
                if (combat == CombatOutcome.Finished || canVerifyReward)
                {
                    if (options.Kind != DomainKind.Tacet) world.Sleep(3000);
                    rewardReached = WalkToTreasure(requireVerifiedReward: canVerifyReward);
                }
                if (token.IsCancellationRequested) return false;
                if (combat == CombatOutcome.Dead)
                {
                    if (!RecoverAfterDeath())
                        throw new UnresolvedCombatException("体力本战斗失败后未核实可恢复画面，保留当前状态。");
                    return false;
                }
                // BaseCombatTask.wait_combat 超时抛普通 Exception，DomainTask 的指定
                // CharDead/NotInCombat 捕获并不覆盖它，因此初始未开战直接失败。
                if (combat == CombatOutcome.NoCombat)
                    return StopDomain("200 秒内未确认开战。");
                if (combat != CombatOutcome.Finished && !(canVerifyReward && rewardReached))
                {
                    CaptureSpendEvidence("after-stamina-combat-unverified");
                    throw new UnresolvedCombatException("体力本未确认战斗结束（" + combat + "），保留当前挑战。");
                }
                if (!rewardReached)
                    return StopDomain("战斗结束后未到达宝箱交互位置。");
                if (tacet && !TryOpenTacetReward())
                {
                    CaptureSpendEvidence("stamina-reward-interaction-unconfirmed");
                    if (retriedUnclaimedRound)
                        return StopDomain("再次挑战后仍未确认领奖交互，已停在当前副本。");
                    Report("未确认打开体力奖励，尝试重新挑战");
                    if (!RestartTacetAfterUnclaimed())
                        return StopDomain("领奖交互未确认，未能确认重新挑战。");
                    retriedUnclaimedRound = true;
                    continue;
                }
                if (!tacet && !HasClaimStamina() && !PickF())
                    return StopDomain("战斗结束后未确认发送领奖交互。");
                int claimedBefore = runs;
                bool canContinue, tacetSettlement;
                if (!UseStamina(ref mustUse, out canContinue, out tacetSettlement))
                    return false;
                if (runs == claimedBefore)
                {
                    return EndInWorld();
                }
                retriedUnclaimedRound = false;
                world.Sleep(4000);
                if (tacet && !tacetSettlement && world.Captured)
                {
                    TacetSettlement observedSettlement;
                    tacetSettlement = TacetSettlement.TryRead(world.Frame, world.Ocr, out observedSettlement);
                }
                if (!canContinue)
                {
                    CompletionVerified = true;
                    if (tacetSettlement)
                    {
                        if (!NavigateTacetSettlement(restart: false) && !token.IsCancellationRequested)
                            Fail("体力已扣减，尚未确认从无音区结算页退出。");
                        return !Failed && !token.IsCancellationRequested;
                    }
                    if (tacet ? world.ClickCentered(.365, .853) : world.Click(.42, .84))
                        world.Sleep(1800);
                    return EndInWorld();
                }
                if (!(tacetSettlement ? NavigateTacetSettlement(restart: true) : Rechallenge()))
                {
                    if (!token.IsCancellationRequested)
                        Fail("已领取奖励，但未确认再挑战进入副本。");
                    return false;
                }
                world.Sleep(1000);
            }
            return false;
        }

        private bool StopDomain(string message)
        {
            if (!token.IsCancellationRequested) Fail(message);
            return false;
        }

        private void StopAfterReturningWorld(string message)
        {
            if (token.IsCancellationRequested) return;
            if (!EnsureWorld())
            {
                if (!token.IsCancellationRequested) Fail(message + " 未确认返回大世界。");
                return;
            }
            CompletionVerified = false;
            NoStamina = false;
            RunLog.Warn("体力本", message);
            Report(message, finished: true);
        }

        private bool Rechallenge()
        {
            if (options.Kind == DomainKind.Tacet)
            {
                Rectangle button = Rectangle.Empty;
                int route = world.InputRouteVersion;
                Func<Rectangle> find = () =>
                {
                    if (world.InputRouteVersion != route || world.VerifiedWorld || world.Ui == null || world.Ui.ClaimReward)
                        return Rectangle.Empty;
                    return DomainMath.RechallengeButton(world.Read(.54, .81, .72, .90));
                };
                if (!world.WaitUntil(() => world.InputRouteVersion != route || !(button = find()).IsEmpty, 3000) ||
                    button.IsEmpty || world.InputRouteVersion != route)
                    return false;
                if (!world.ClickMenuTransition(route, find,
                    () => world.VerifiedWorld && world.TeamHud || world.Ui != null && world.Ui.SkipDialog || find().IsEmpty,
                    4000, firstPoint: matched => DomainMath.VerifiedReferencePoint(matched,
                        DomainMath.CenteredPoint(new Size(world.Frame.Width, world.Frame.Height), .640, .851))))
                    return false;
            }
            else
            {
                if (!world.ClickMenuButton(.68, .84)) return false;
                world.Sleep(1000);
                if (!ConfirmDomainRestart()) return false;
                return WaitTeam();
            }
            world.Sleep(200);
            WaitForEntryDialog();
            return WaitTeam();
        }

        private bool NavigateTacetSettlement(bool restart)
        {
            if (!world.Tick()) return false;
            int route = world.InputRouteVersion;
            Func<Rectangle> control = () =>
            {
                TacetSettlement settlement;
                if (!TacetSettlement.TryRead(world.Frame, world.Ocr, out settlement)) return Rectangle.Empty;
                return restart ? settlement.Restart : settlement.Exit;
            };
            Func<bool> arrived = () => restart ? world.VerifiedWorld && world.TeamHud : TaskContinuation.IsSafeWorld(world);
            Func<bool> entryDialog = () => restart && world.Ui != null && world.Ui.SkipDialog;
            Func<bool> destination = () => arrived() || entryDialog();

            CaptureSpendEvidence(restart ? "stamina-settlement-restart-before" : "stamina-settlement-exit-before");
            bool sourceSeen = !control().IsEmpty;
            bool transitioned = world.ClickMenuTransition(route, control, destination, 15000, 450);
            if (!transitioned && sourceSeen && world.InputRouteVersion == route &&
                world.Captured && control().IsEmpty && !token.IsCancellationRequested)
            {
                RunLog.Info("体力本", "结算控件消失，继续等待场景加载");
                transitioned = world.WaitUntil(() => world.InputRouteVersion != route || destination(), 100000);
            }
            if (transitioned && world.InputRouteVersion == route)
            {
                if (entryDialog()) world.WaitForOptionalDialog(route);
                if (world.WaitStable(() => world.InputRouteVersion == route && arrived(), 20000, 180))
                    return true;
            }
            CaptureSpendEvidence(restart ? "stamina-settlement-restart-unconfirmed" : "stamina-settlement-exit-unconfirmed");
            return false;
        }

        private bool ConfirmDomainRestart()
        {
            Rectangle button = Rectangle.Empty;
            int route = world.InputRouteVersion;
            Func<bool> find = () => !world.VerifiedWorld && world.Vision.FindOptionalButton(world.Frame, .60, out button);
            if (!world.WaitUntil(find, 2000)) return !token.IsCancellationRequested;
            if (route != world.InputRouteVersion || !world.ClickOnRoute(new Point(
                (int)(world.Frame.Width * .49), (int)(world.Frame.Height * .55)), route)) return false;
            world.Sleep(500);
            if (!find() || !world.ClickOnRoute(button, route)) return false;
            world.Sleep(500);
            if (world.WaitUntil(find, 1000) && !world.ClickOnRoute(button, route)) return false;
            return !token.IsCancellationRequested;
        }

        private bool EndInWorld()
        {
            if (EnsureWorld())
                return true;
            if (!token.IsCancellationRequested)
                Fail("用量已核对，退出副本后未确认返回大世界。");
            return false;
        }

        private bool OpenGuideForStamina()
        {
            return guide.Open(() => world.InRealm ? EnsureWorld() : recovery.EnsureMain(180000, loginPending: true));
        }

        internal bool ReadEntryStamina(bool retryBossTab, out StaminaReadout available)
        {
            StaminaReadout readout = default(StaminaReadout);
            bool found = world.WaitUntil(() => ReadStaminaHeader(out readout), 5000);
            if (!found && retryBossTab && !token.IsCancellationRequested)
            {
                Rectangle tab;
                bool selected;
                if (guide.FindBookTab(out tab, out selected) && world.ClickMenuButton(tab))
                    found = world.WaitUntil(() => ReadStaminaHeader(out readout), 5000);
            }
            available = readout;
            if (found)
                Report(string.Format("体力 {0}+{1}", available.Current,
                    available.BackupKnown ? available.Backup.ToString() : "备用数未识别"));
            return found;
        }

        internal bool OpenBookCategory(DomainKind kind)
        {
            return OpenGuideForStamina() && SelectBookCategory(kind);
        }

        private bool SelectBookCategory(DomainKind kind)
        {
            Rectangle heading = Rectangle.Empty;
            bool selected = false;
            int route = world.InputRouteVersion;
            if (!world.WaitUntil(() => world.InputRouteVersion != route ||
                TryReadBookCategory(kind, route, out heading, out selected), 5000) ||
                world.InputRouteVersion != route)
                return false;

            for (int attempt = 0; attempt < 2 && !token.IsCancellationRequested; attempt++)
            {
                if (attempt != 0 && !TryReadBookCategory(kind, route, out heading, out selected))
                    return false;
                if (selected) return WaitForSelectedBookCategory(kind, route, 1500);

                Point preferred = attempt == 0 ? DomainMath.ReferenceCategoryPoint(kind,
                    new Size(world.Frame.Width, world.Frame.Height), heading) : Point.Empty;
                Point click = preferred.IsEmpty ? DomainMath.VerifiedReferencePoint(heading, Point.Empty) : preferred;
                bool sent = world.ClickMenuButton(click, retry: attempt != 0, route: route);
                RunLog.Info("体力本", string.Format("切换图鉴分类 · 类型={0} · 位置={1} · 尝试={2} · 投递={3}",
                    kind, click, attempt + 1, sent));
                if (sent)
                {
                    world.Sleep(2000);
                    if (WaitForSelectedBookCategory(kind, route, 5000)) return true;
                }
            }
            return false;
        }

        private bool TryReadBookCategory(DomainKind kind, int route, out Rectangle heading, out bool selected)
        {
            heading = Rectangle.Empty;
            selected = false;
            Rectangle guideTab;
            bool bookSelected;
            if (world.InputRouteVersion != route ||
                !guide.FindBookTab(out guideTab, out bookSelected) || !bookSelected)
                return false;
            heading = DomainMath.BookKindButton(world.Read(.095, .11, .35, .91), kind);
            if (!world.Captured || world.InputRouteVersion != route || heading.IsEmpty)
                return false;
            selected = DomainMath.IsHighlightedBookKind(world.Frame, heading);
            return true;
        }

        private bool WaitForSelectedBookCategory(DomainKind kind, int route, int timeoutMs)
        {
            int stable = 0;
            bool matched = world.WaitUntil(() =>
            {
                if (world.InputRouteVersion != route) return true;
                Rectangle heading;
                bool selected;
                stable = TryReadBookCategory(kind, route, out heading, out selected) && selected ? stable + 1 : 0;
                return stable >= 2;
            }, timeoutMs);
            return matched && world.InputRouteVersion == route;
        }

        private bool TeleportIntoDomain()
        {
            switch (options.Kind)
            {
                case DomainKind.Tacet:
                    if (ClickBookTarget(options.Index, 21, TacetStructure) != BookDestination.Team) return false;
                    break;
                case DomainKind.Forgery:
                    if (ClickBookTarget(options.Index, 20, ForgeryStructure) != BookDestination.Team) return false;
                    if (!world.Ui.TeamStart)
                    {
                        if (!world.ClickMenuButton(0.891, 0.910)) return false;
                        world.Sleep(1000);
                    }
                    break;
                default:
                    if (!world.ClickMenuButton(0.9730, 0.8806)) return false;
                    world.Sleep(1000);
                    int index = options.Material == DomainOptions.Materials[0] ? 0 :
                        options.Material == DomainOptions.Materials[1] ? 1 : 2;
                    if (!world.ClickMenuButton(0.898, 0.533 + index * 0.14)) return false;
                    world.Sleep(1000);
                    if (!world.ClickMenuButton(0.93, 0.90)) return false;
                    world.Sleep(1000);
                    break;
            }
            return StartTeamChallenge();
        }

        private BookDestination ClickBookTarget(int serial, int total, int[] structure)
        {
            world.Sleep(500);
            if (!world.Tick(nativePixels: true)) return BookDestination.None;
            double top = GuidebookList.ScrollTop(world.Frame);
            int route = world.InputRouteVersion;
            int index = serial - 1;
            var rows = new List<Rectangle>();
            if (serial <= 4)
            {
                rows = ReadStableTargetRows(route);
                if (rows.Count == 0) return BookDestination.None;
                if (index < rows.Count)
                    return ClickTargetRow(rows, index, route);
            }
            double offset = DomainMath.ScrollOffset(top, serial, total, structure);
            if (!ClickBookScrollbar(offset, route))
                return BookDestination.None;
            world.Sleep(1000);
            rows = ReadStableTargetRows(route);
            if (serial > 4 && structure != null && (rows.Count == 3 || rows.Count == 4) &&
                rows[rows.Count - 1].Y < world.Frame.Height * .73)
            {
                double rowHeight = DomainMath.BookRowHeight(top, total, structure);
                if (!ClickBookScrollbar(Math.Min(DomainMath.BookScrollBottom, offset + (rowHeight > 0 ? rowHeight * .55 : .018)), route))
                    return BookDestination.None;
                world.Sleep(1000);
                rows = ReadStableTargetRows(route);
            }
            return rows.Count == 0 ? BookDestination.None : ClickTargetRow(rows, rows.Count - 1, route);
        }

        private bool ClickBookScrollbar(double y, int route)
        {
            Func<Rectangle> thumb = () => world.Captured && world.Ui != null && world.Ui.BookSelected ?
                NestMath.ScrollThumb(world.Frame, sidebar: false) : Rectangle.Empty;
            Func<int> trackY = () => GuidebookList.PointAt(new Size(world.Frame.Width, world.Frame.Height), .973, y).Y;
            return world.ClickMenuTransition(route, () =>
            {
                Rectangle range = thumb();
                return range.IsEmpty ? Rectangle.Empty : new Rectangle(range.Left - 2, trackY() - 2, 5, 5);
            }, () =>
            {
                Rectangle range = thumb();
                int yAt = trackY();
                return !range.IsEmpty && yAt >= range.Top - 5 && yAt <= range.Bottom + 5;
            }, 3500, settleMs: 500);
        }

        private List<Rectangle> ReadTargetRows()
        {
            if (!world.Tick() || world.Ui == null) return new List<Rectangle>();
            if (options.Kind != DomainKind.Tacet) return world.Ui.BossRows;
            if (!world.Ui.BookSelected) return new List<Rectangle>();
            var size = new Size(world.Frame.Width, world.Frame.Height);
            Rectangle area = GuidebookList.Area(size, .80, .213, .964, .859);
            List<OcrLine> lines = world.Read(area.Left / (double)size.Width, area.Top / (double)size.Height,
                area.Right / (double)size.Width, area.Bottom / (double)size.Height);
            return world.Ui != null && world.Ui.BookSelected && world.Frame.Width == size.Width &&
                world.Frame.Height == size.Height ? DomainMath.TacetEntryButtons(lines, area) : new List<Rectangle>();
        }

        private List<Rectangle> ReadStableTargetRows(int route)
        {
            if (options.Kind != DomainKind.Tacet) return ReadTargetRows();
            var previous = new List<Rectangle>();
            var stable = new List<Rectangle>();
            world.WaitUntil(() =>
            {
                if (world.InputRouteVersion != route) return true;
                List<Rectangle> current = ReadTargetRows();
                if (DomainMath.SameBookRows(previous, current, world.Frame.Height))
                {
                    stable = current;
                    return true;
                }
                previous = current;
                return false;
            }, 3500);
            return world.InputRouteVersion == route ? stable : new List<Rectangle>();
        }

        private BookDestination ClickTargetRow(List<Rectangle> rows, int index, int route)
        {
            const int attempts = 2;
            for (int attempt = 0; attempt < attempts && !token.IsCancellationRequested; attempt++)
            {
                Rectangle row = rows[index];
                RunLog.Info("体力本", string.Format("点击图鉴入口 · 类型={0} · 序号={1} · 可见行={2}/{3} · 按钮={4} · 第 {5} 次",
                    options.Kind, options.Index, index + 1, rows.Count, row, attempt + 1));
                Point point = DomainMath.VerifiedReferencePoint(row, Point.Empty);
                if (world.ClickMenuButton(point, retry: attempt != 0, route: route))
                    world.Sleep(1000);
                BookDestination observed = BookDestination.None;
                bool found = world.WaitUntil(() => world.InputRouteVersion != route ||
                    (observed = ReadBookDestination()) != BookDestination.None, 10000);
                if (world.InputRouteVersion != route) return BookDestination.None;
                if (found)
                {
                    world.Sleep(500);
                    if (world.Tick() && world.InputRouteVersion == route && ReadBookDestination() == observed)
                        return observed;
                }
                if (attempt + 1 >= attempts) break;
                List<Rectangle> current = ReadStableTargetRows(route);
                if (!DomainMath.SameBookRows(rows, current, world.Frame.Height)) break;
                rows = current;
            }
            return BookDestination.None;
        }

        private BookDestination ReadBookDestination()
        {
            if (world.Ui == null) return BookDestination.None;
            Rectangle close;
            bool team = world.Ui.TeamStart;
            bool travel = world.Ui.Teleport;
            if (!team && !travel && !world.FindNear("team_close", .80, out close))
                return BookDestination.None;
            Rectangle tab;
            bool selected;
            if (world.Vision.InspectGuidebook(world.Frame, out tab, out selected)) return BookDestination.None;
            return travel && !team ? BookDestination.Travel : BookDestination.Team;
        }

        private bool TeamChallenge()
        {
            const string label = "team_start_challenge";
            if (!world.WaitFind(label, 10000, .75))
                return false;
            int route = world.InputRouteVersion;
            Func<Rectangle> find = () =>
            {
                Rectangle button;
                return world.Vision.FindLabel(world.Frame, label, .75, out button) ? button : Rectangle.Empty;
            };
            if (!world.ClickMenuTransition(route, find,
                () => world.VerifiedWorld && world.TeamHud || world.Ui != null && world.Ui.SkipDialog || find().IsEmpty,
                4500))
                return false;
            world.WaitForOptionalDialog(route);
            return true;
        }

        internal bool StartTeamChallenge()
        {
            Report("等待开始挑战");
            if (!TeamChallenge()) return false;
            Report("等待副本加载");
            return WaitTeam();
        }

        internal void WaitForEntryDialog()
        {
            world.WaitForOptionalDialog();
        }

        private bool WalkUntilF(int timeoutMs)
        {
            if (!world.Tick()) return false;
            if (HasInteraction()) return true;
            if (!world.Send(BattleAction.Target())) return false;
            world.Sleep(200);
            if (!world.KeyDown('W')) return false;
            int route = world.InputRouteVersion;
            try
            {
                return world.WaitUntil(() => world.InputRouteVersion != route || HasInteraction(), timeoutMs) &&
                    world.InputRouteVersion == route;
            }
            finally { world.KeyUp('W'); }
        }

        private bool HasInteraction()
        {
            if (world.Ui == null) return false;
            if (world.Ui.FPrompt) return true;
            world.Ui.FPrompt = world.Vision.FindPickupKey(world.Frame, out world.Ui.FBox);
            return world.Ui.FPrompt;
        }

        internal bool PickF()
        {
            if (!world.Tick() || !HasInteraction() || !world.Key('F')) return false;
            world.Sleep(1000);
            return true;
        }

        private void DismissWrongClaim()
        {
            Rectangle claim;
            if (!world.TeamHud && world.FindNear("claim_cancel_button_hcenter_vcenter", .80, out claim))
            {
                world.Key((char)0x1B);
                world.Sleep(600);
            }
        }

        private bool HasClaimStamina()
        {
            Rectangle box;
            return !world.TeamHud && world.FindNear("claim_stamina_sign", .82, out box);
        }

        internal bool WalkToTreasure(bool requireVerifiedReward = false)
        {
            if (!ApproachTreasure(true, requireQuietRealm: requireVerifiedReward) &&
                (requireVerifiedReward || token.IsCancellationRequested || !ApproachTreasure(false))) return false;
            if (!WalkUntilF(2000)) return false;
            world.Sleep(1000);
            return !token.IsCancellationRequested;
        }

        private bool ApproachTreasure(bool requireClaim, bool requireQuietRealm = false, int? reapproachRoute = null)
        {
            var watch = Stopwatch.StartNew();
            char held = '\0';
            bool centered = false;
            int route = reapproachRoute ?? world.InputRouteVersion;
            int timeout = reapproachRoute.HasValue ? 6500 : 30000;
            Rectangle target = Rectangle.Empty;
            try
            {
                while (watch.ElapsedMilliseconds < timeout && !token.IsCancellationRequested)
                {
                    if (!world.Tick() || world.InputRouteVersion != route || world.Ui == null) return false;
                    if (requireQuietRealm && (!world.VerifiedWorld || !world.InRealm || world.Hud == null ||
                        world.Hud.Locked || world.Hud.EnemyHealth || world.Hud.Dead)) return false;
                    if (reapproachRoute.HasValue && !DomainMath.CanReapproachTacetReward(world.Hud, world.Ui, world.VerifiedWorld))
                        return false;
                    if (HasInteraction() && (!requireClaim || IsRewardPrompt())) return true;
                    if (world.Ui.Treasure) target = world.Ui.TreasureBox;
                    else if (reapproachRoute.HasValue) target = Rectangle.Empty;
                    if (target.IsEmpty)
                    {
                        if (held != '\0') world.KeyUp(held);
                        held = '\0';
                        centered = false;
                    }
                    else
                    {
                        char direction = DomainMath.TreasureDirection(target, new Size(world.Frame.Width, world.Frame.Height),
                            held, ref centered);
                        if (held != direction)
                        {
                            if (held != '\0') world.KeyUp(held);
                            held = '\0';
                            if (!world.KeyDown(direction, route: route)) return false;
                            held = direction;
                        }
                    }
                    token.WaitHandle.WaitOne(100);
                }
            }
            finally { if (held != '\0') world.KeyUp(held); }
            return false;
        }

        private bool IsRewardPrompt()
        {
            Rectangle key = world.Ui.FBox;
            if (key.IsEmpty) return false;
            var frame = world.Frame;
            var area = Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height),
                new Rectangle(key.Right - key.Width / 3, key.Top - 2 * key.Height, 15 * key.Width, 5 * key.Height));
            return !area.IsEmpty && DomainMath.IsSelectedRewardPrompt(world.Ocr.Read(frame, area), key);
        }

        private bool TryOpenTacetReward()
        {
            if (!world.Tick()) return false;
            int route = world.InputRouteVersion;
            bool reapproached = false;
            for (int attempt = 0; attempt < 2 && !token.IsCancellationRequested; attempt++)
            {
                bool ready = world.WaitUntil(() => world.InputRouteVersion != route || HasClaimStamina() ||
                    HasInteraction() && IsRewardPrompt(), 2500);
                if (world.InputRouteVersion != route) return false;
                if (HasClaimStamina()) return world.InputRouteVersion == route;
                if (!ready && !(HasInteraction() && IsRewardPrompt()))
                {
                    if (reapproached || !world.Tick() || world.InputRouteVersion != route ||
                        !DomainMath.CanReapproachTacetReward(world.Hud, world.Ui, world.VerifiedWorld) ||
                        !world.Ui.Treasure || world.Ui.TreasureBox.IsEmpty) return false;
                    reapproached = true;
                    CaptureSpendEvidence("stamina-reward-reapproach-before");
                    Report("领奖提示暂未出现，已重新定位宝箱，尝试靠近一次");
                    if (!ApproachTreasure(true, reapproachRoute: route)) return false;
                }
                if (!world.Tick() || world.InputRouteVersion != route) return false;
                if (!HasInteraction() || !IsRewardPrompt() || !world.Key('F', route: route)) continue;
                if (world.WaitUntil(() => world.InputRouteVersion != route || HasClaimStamina(), 4500))
                    return world.InputRouteVersion == route;
            }
            return false;
        }

        private bool UseStamina(ref int mustUse, out bool canContinue, out bool tacetSettlement)
        {
            canContinue = false;
            tacetSettlement = false;
            StaminaReadout before = default(StaminaReadout);
            world.Sleep(1000);
            if (!world.WaitUntil(() => HasClaimStamina() && ReadStaminaHeader(out before), 4000))
                return UnverifiedSpend("未识别到领奖界面上的体力计数，领取状态未确认。");
            if (before.Current + before.Backup < once)
            {
                if (!before.BackupKnown)
                    return UnverifiedSpend("缺少备用体力读数，无法核实此次领取。");
                NoStamina = usedStamina == 0;
                CompletionVerified = true;
                CaptureSpendEvidence("stamina-insufficient-at-claim");
                world.Key((char)0x1B);
                return true;
            }

            int used = DomainMath.RewardCost(before.Current, once, mustUse, before.Backup);
            bool useDouble = used > once;
            RunLog.Info("体力本", string.Format("领奖 · 当前体力={0} · 备用={1} · 计划使用={2} · 双倍={3}",
                before.Current, before.Backup, used, useDouble));
            int route = world.InputRouteVersion;
            Rectangle button = FindSpendControl(useDouble, used);
            CaptureSpendEvidence("stamina-spend-before");
            if (button.IsEmpty)
                return UnverifiedSpend("未识别到显示本次体力金额的领取按钮。");
            if (DailyClaimVision.Contains(world.Read(.16, .11, .84, .57), DailyClaimVision.Receipts))
                return UnverifiedSpend("领取前已经显示奖品回执，当前操作未执行。");
            TacetSettlement priorSettlement;
            if (options.Kind == DomainKind.Tacet && TacetSettlement.TryRead(world.Frame, world.Ocr, out priorSettlement))
                return UnverifiedSpend("领取前已显示无音区结算，当前操作未执行。");
            if (!HasClaimStamina() || world.InputRouteVersion != route)
                return UnverifiedSpend("领取前的体力领奖界面不再可确认。");
            // Keep this pending through any refill and its subsequent claim.
            HasUnverifiedConsumption = true;
            if (!ClickSpendControl(button, route))
                RunLog.Warn("体力本", "领取输入未获确认，先核对体力扣减或新回执");
            token.ThrowIfCancellationRequested();
            long initialClick = world.Now;

            StaminaReadout debitBaseline = before;
            int stableDebit = 0;
            var settlementDebit = new TacetDebitCheck();
            bool sawSettlement = false, verifiedSettlement = false;
            bool replenishment = false, sawReceipt = false, routeChanged = false, claimMayHaveAdvanced = false;
            Func<bool> observeClaim = delegate
            {
                if (world.InputRouteVersion != route)
                {
                    routeChanged = true;
                    return true;
                }
                bool claimVisible = HasClaimStamina();
                if (!world.Captured || world.InputRouteVersion != route)
                {
                    stableDebit = 0;
                    settlementDebit.Reset();
                    return false;
                }
                TacetSettlement settlement;
                if (options.Kind == DomainKind.Tacet && !claimVisible &&
                    TacetSettlement.TryRead(world.Frame, world.Ocr, out settlement))
                {
                    claimMayHaveAdvanced = sawSettlement = true;
                    replenishment = false;
                    stableDebit = 0;
                    verifiedSettlement = settlementDebit.Observe(settlement, debitBaseline.Current, used);
                    return verifiedSettlement;
                }
                settlementDebit.Reset();
                if (sawSettlement) return false;
                Rectangle gem;
                replenishment = world.Vision.FindLabel(world.Frame, "gem_add_stamina", .80, out gem);
                if (replenishment)
                    return true;
                if (!claimVisible)
                    claimMayHaveAdvanced = true;
                StaminaReadout observed;
                bool balanceRead = ReadStaminaHeader(out observed);
                if (balanceRead && SameStaminaHeader(debitBaseline, observed) &&
                    (observed.Current < debitBaseline.Current ||
                    debitBaseline.BackupKnown && observed.BackupKnown && observed.Backup < debitBaseline.Backup))
                    claimMayHaveAdvanced = true;
                if (balanceRead && ExpectedStaminaDebit(debitBaseline, observed, used))
                {
                    if (++stableDebit >= 2)
                        return true;
                }
                else
                    stableDebit = 0;
                if (world.Ui != null && !world.Ui.ClaimReward && DailyClaimVision.Contains(
                    world.Read(.16, .11, .84, .57), DailyClaimVision.Receipts))
                {
                    sawReceipt = true;
                    return true;
                }
                return false;
            };
            Func<bool> waitForClaim = delegate
            {
                if (world.WaitUntil(observeClaim, 9000)) return true;
                if (claimMayHaveAdvanced || !RetryUnchangedSpend(debitBaseline, button, useDouble, used, route))
                    return false;
                stableDebit = 0;
                return world.WaitUntil(observeClaim, 9000);
            };
            bool confirmed = waitForClaim();
            // 参考 click_dialog_* after_sleep=2；观察回执期间仍允许及时采样。
            world.Sleep((int)Math.Max(0, 2000 - (world.Now - initialClick)));
            if (confirmed && replenishment && !routeChanged && world.InputRouteVersion == route)
            {
                if (!ConfirmSelectedRefillAndRetry(before, used, useDouble, route, out debitBaseline, out button))
                    return UnverifiedSpend("未确认所选方式已补充足够波片并返回领奖界面。");
                stableDebit = 0;
                settlementDebit.Reset();
                replenishment = false;
                claimMayHaveAdvanced = false;
                long refillClick = world.Now;
                confirmed = waitForClaim();
                world.Sleep((int)Math.Max(0, 1000 - (world.Now - refillClick)));
            }
            if (routeChanged || world.InputRouteVersion != route)
                return UnverifiedSpend("领取后游戏窗口或输入方式发生变化，无法核对本次扣除。");
            if (!confirmed || replenishment)
                return UnverifiedSpend(replenishment ? "游戏显示补充结晶波片，领取费用未扣除。"
                    : sawSettlement ? "已识别无音区结算，但尚未核对剩余波片。" : "点击后未确认体力扣减或新奖励回执。");

            HasUnverifiedConsumption = false;
            tacetSettlement = verifiedSettlement;
            canContinue = DomainMath.CanContinue(before.Current, before.Backup, used, mustUse, once);
            mustUse -= used;
            usedStamina += used;
            runs++;
            CaptureSpendEvidence(tacetSettlement ? "after-stamina-tacet-settlement" :
                sawReceipt ? "after-stamina-spend-receipt" : "after-stamina-debit-confirmed");
            RunLog.Info("体力本", string.Format("本次确认使用={0} · 依据={1} · 累计={2} · 继续下一轮={3}",
                used, tacetSettlement ? "无音区结算剩余 " + settlementDebit.Remaining : sawReceipt ? "奖励回执" : "同一体力计数",
                usedStamina, canContinue));
            return true;
        }

        internal static Rectangle RefillConfirmButton(IEnumerable<OcrLine> lines)
        {
            return DailyClaimVision.UniqueButton(lines, "确认", "確認", "确定", "確定", "Confirm");
        }

        private bool ClickSpendControl(Rectangle button, int route, bool useWindowMessage = false)
        {
            return !button.IsEmpty && world.ClickMenuButton(DomainMath.VerifiedReferencePoint(button, Point.Empty),
                retry: !useWindowMessage, route: route);
        }

        private bool RetryUnchangedSpend(StaminaReadout before, Rectangle originalButton, bool useDouble, int cost, int route)
        {
            Rectangle button = Rectangle.Empty;
            for (int sample = 0; sample < 2; sample++)
            {
                if (token.IsCancellationRequested || !world.Tick() ||
                    world.InputRouteVersion != route || !HasClaimStamina()) return false;
                StaminaReadout balance;
                if (!ReadStaminaHeader(out balance)) return false;
                button = FindSpendControl(useDouble, cost);
                if (!CanRetrySpend(before, balance, originalButton, button) ||
                    DailyClaimVision.Contains(world.Read(.16, .11, .84, .57), DailyClaimVision.Receipts))
                    return false;
                Rectangle refill;
                if (!world.Captured || world.InputRouteVersion != route || !HasClaimStamina() ||
                    world.Vision.FindLabel(world.Frame, "gem_add_stamina", .80, out refill))
                    return false;
                if (sample == 0) world.Sleep(120);
            }
            CaptureSpendEvidence("stamina-spend-retry");
            RunLog.Warn("体力本", "领取弹窗、按钮金额与两项体力余额均未变化，重试一次领取点击");
            if (!ClickSpendControl(button, route, useWindowMessage: true))
                RunLog.Warn("体力本", "重试输入未获确认，继续核对体力扣减或新回执");
            return true;
        }

        private Rectangle ReadRefillConfirm()
        {
            return RefillConfirmButton(world.Read(.55, .60, .85, .82));
        }

        private bool ClickRefillConfirm(Rectangle button, int route)
        {
            if (button.IsEmpty || !world.Captured) return false;
            Point reference = DomainMath.CenteredPoint(new Size(world.Frame.Width, world.Frame.Height), .70, .71);
            return world.ClickMenuButton(DomainMath.VerifiedReferencePoint(button, reference),
                retry: true, route: route);
        }

        private bool ConfirmSelectedRefillAndRetry(StaminaReadout before, int cost, bool useDouble, int route,
            out StaminaReadout debitBaseline, out Rectangle spendButton)
        {
            debitBaseline = default(StaminaReadout);
            spendButton = Rectangle.Empty;
            CaptureSpendEvidence("stamina-refill-method");
            Rectangle gem;
            if (!world.Tick() || world.InputRouteVersion != route || !world.Vision.FindLabel(world.Frame,
                "gem_add_stamina", .80, out gem)) return false;
            var cards = new Rectangle((int)(world.Frame.Width * .36), (int)(world.Frame.Height * .36),
                (int)(world.Frame.Width * .27), (int)(world.Frame.Height * .16));
            int[] selection = BattlePassVision.ButtonPatch(world.Frame, cards);
            if (!ClickRefillConfirm(ReadRefillConfirm(), route)) return false;

            // A refill may apply immediately or open a second confirmation. Never submit the same selector twice.
            world.Sleep(1000);
            Rectangle confirmation = Rectangle.Empty;
            bool applied = false;
            bool observed = world.WaitUntil(delegate
            {
                if (world.InputRouteVersion != route) return true;
                StaminaReadout balance;
                applied = ReadStaminaHeader(out balance) && balance.Limit == before.Limit &&
                    balance.Current > before.Current && balance.Current >= cost;
                if (applied) return true;
                if (!BattlePassVision.ButtonChanged(world.Frame, cards, selection) || HasClaimStamina() ||
                    DailyClaimVision.Contains(world.Read(.24, .23, .77, .36),
                    "请选择补充方式", "請選擇補充方式", "Select a replenishment method")) return false;
                confirmation = ReadRefillConfirm();
                return !confirmation.IsEmpty;
            }, 4000);
            if (!observed || world.InputRouteVersion != route) return false;
            if (!applied)
            {
                if (!ClickRefillConfirm(confirmation, route)) return false;
                world.Sleep(1000);
            }
            StaminaReadout replenished = default(StaminaReadout);
            if (!world.WaitUntil(() => world.InputRouteVersion == route && ReadStaminaHeader(out replenished) &&
                replenished.Limit == before.Limit && replenished.Current > before.Current &&
                replenished.Current >= cost, 4000)) return false;
            CaptureSpendEvidence("stamina-refill-confirmed");
            if (!HasClaimStamina())
            {
                if (!world.Key((char)0x1B, route: route)) return false;
                world.Sleep(1000);
            }
            if (!world.WaitUntil(() => HasClaimStamina() && ReadStaminaHeader(out replenished) &&
                replenished.Limit == before.Limit && replenished.Current >= cost, 3500) ||
                world.InputRouteVersion != route) return false;
            debitBaseline = replenished;
            spendButton = FindSpendControl(useDouble, cost);
            if (spendButton.IsEmpty || world.InputRouteVersion != route || !HasClaimStamina()) return false;
            if (!ClickSpendControl(spendButton, route))
                RunLog.Warn("体力本", "补充后的领取输入未获确认，先核对体力扣减或新回执");
            // 立即进入回执观察，调用者会补足参考的 1 秒等待，避免漏掉短暂回执。
            return !token.IsCancellationRequested && world.InputRouteVersion == route;
        }

        internal struct StaminaReadout
        {
            internal int Current, Backup, Limit;
            internal Rectangle Position;
            internal bool BackupKnown;
        }

        internal bool ReadStaminaHeader(out StaminaReadout readout)
        {
            var lines = world.Read(.49, 0, .92, .10);
            int route = world.InputRouteVersion;
            bool found = TryReadStaminaHeader(lines, world.Frame.Width, out readout);
            if (found && readout.BackupKnown) return true;
            var wider = world.Read(.45, 0, .94, .112);
            if (world.InputRouteVersion != route || !world.Captured) return false;
            StaminaReadout expanded;
            if (TryReadStaminaHeader(wider, world.Frame.Width, out expanded) &&
                (!found || readout.Current == expanded.Current && readout.Limit == expanded.Limit))
            {
                readout = expanded;
                return true;
            }
            return found;
        }

        private static readonly Regex StaminaHeaderText = new Regex(
            @"^\s*(?:([0-9]{1,4})(?:\s*\|\s*|\s+))?([0-9]{1,4})\s*/\s*([0-9]{3})\s*$");

        internal static bool TryReadStaminaHeader(IEnumerable<OcrLine> lines, int width, out StaminaReadout readout)
        {
            readout = default(StaminaReadout);
            foreach (OcrLine line in lines)
            {
                if (line.Score < .72 || line.Box.IsEmpty || string.IsNullOrWhiteSpace(line.Text))
                    continue;
                var pair = StaminaHeaderText.Match(line.Text.Normalize(NormalizationForm.FormKC));
                int current, limit;
                if (!pair.Success || !int.TryParse(pair.Groups[2].Value, out current) ||
                    !int.TryParse(pair.Groups[3].Value, out limit) || limit < 200 || limit > 360)
                    continue;
                if (!readout.Position.IsEmpty)
                    return false;
                readout = new StaminaReadout { Current = current, Limit = limit, Position = line.Box };
                int combinedBackup;
                if (int.TryParse(pair.Groups[1].Value, out combinedBackup) && combinedBackup <= 4800)
                {
                    readout.BackupKnown = true;
                    readout.Backup = combinedBackup;
                }
            }
            if (readout.Position.IsEmpty)
                return false;
            if (readout.BackupKnown) return true;
            int y = readout.Position.Top + readout.Position.Height / 2, best = int.MaxValue;
            foreach (OcrLine line in lines)
            {
                int backup, distance = readout.Position.Left - line.Box.Right;
                if (line.Score < .72 || line.Box.IsEmpty || !int.TryParse(OcrText.Compact(line.Text), out backup) ||
                    backup < 0 || backup > 4800 || distance <= 0 || distance > width * .22 ||
                    Math.Abs(y - line.Box.Top - line.Box.Height / 2) > Math.Max(12, readout.Position.Height) ||
                    distance >= best)
                    continue;
                best = distance;
                readout.Backup = backup;
                readout.BackupKnown = true;
            }
            return true;
        }

        private static bool ExpectedStaminaDebit(StaminaReadout before, StaminaReadout after, int cost)
        {
            int debit = before.Current - after.Current;
            if (before.BackupKnown && after.BackupKnown)
                debit += before.Backup - after.Backup;
            return SameStaminaHeader(before, after) && (debit == cost || debit == cost - 1);
        }

        private static bool SameStaminaHeader(StaminaReadout before, StaminaReadout after)
        {
            return !before.Position.IsEmpty && !after.Position.IsEmpty && before.Limit == after.Limit &&
                Rectangle.Inflate(before.Position, 30, 10).IntersectsWith(after.Position);
        }

        internal static bool CanRetrySpend(StaminaReadout before, StaminaReadout observed,
            Rectangle originalButton, Rectangle observedButton)
        {
            return SameStaminaHeader(before, observed) && before.BackupKnown && observed.BackupKnown &&
                before.Current == observed.Current && before.Backup == observed.Backup &&
                !originalButton.IsEmpty && !observedButton.IsEmpty &&
                Rectangle.Inflate(originalButton, 12, 8).Contains(observedButton);
        }

        private Rectangle FindSpendControl(bool useDouble, int cost)
        {
            var lines = useDouble ? world.Read(.56, .57, .76, .73) : world.Read(.23, .57, .47, .73);
            Rectangle amount = DomainMath.SpendControl(lines, useDouble, cost);
            if (amount.IsEmpty) return amount;
            // 按原版选择左右弹窗按钮，但仅在模板落在已核实金额的同一控件时使用。
            string[] labels = useDouble ? new[] { "confirm_btn_hcenter_vcenter", "confirm_btn_highlight_hcenter_vcenter" } :
                new[] { "cancel_button_hcenter_vcenter", "cancel_button_highlight_hcenter_vcenter" };
            foreach (string label in labels)
            {
                Rectangle button;
                if (world.Vision.FindLabel(world.Frame, label, .70, out button) &&
                    amount.Contains(new Point(button.Left + button.Width / 2, button.Top + button.Height / 2)))
                    return button;
            }
            return amount;
        }

        private void CaptureSpendEvidence(string stage)
        {
            if (recordEvidence != null)
                recordEvidence(stage, world.Captured ? world.Frame : default(PixelFrame));
        }

        private bool UnverifiedSpend(string message)
        {
            CaptureSpendEvidence("stamina-spend-unverified");
            if (!token.IsCancellationRequested)
                Fail(message);
            return false;
        }

        private bool RecoverAfterDeath()
        {
            healPoint = recovery.Recover(healPoint, options.Kind == DomainKind.Tacet);
            return healPoint;
        }

        private bool RestartTacetAfterUnclaimed()
        {
            if (!world.Tick()) return false;
            int route = world.InputRouteVersion;
            Func<Rectangle> restart = () => FarmVision.ReadRechallengeDialog(world.Frame, world.Ocr);
            if (restart().IsEmpty)
            {
                if (!world.VerifiedWorld || !world.TeamHud || world.Hud.Locked || world.Hud.EnemyHealth || world.Hud.Dead ||
                    !world.Key((char)0x1B, route: route) ||
                    !world.WaitUntil(() => world.InputRouteVersion != route || !restart().IsEmpty, 5000) ||
                    world.InputRouteVersion != route)
                    return false;
            }
            CaptureSpendEvidence("stamina-restart-dialog");
            int clearedFrames = 0;
            if (!world.ClickMenuTransition(route, restart, () =>
            {
                if (world.VerifiedWorld && world.TeamHud || world.Ui != null && world.Ui.SkipDialog) return true;
                bool absent = world.Ui != null && world.Ui.ConfirmBox.IsEmpty && restart().IsEmpty;
                clearedFrames = absent ? clearedFrames + 1 : 0;
                return clearedFrames >= 2;
            }, 5000))
                return false;
            world.WaitForOptionalDialog(route);
            return WaitTeam(30000);
        }

        internal bool WaitTeam(int timeoutMs = 120000)
        {
            return world.WaitUntil(() => world.VerifiedWorld && world.TeamHud, timeoutMs);
        }

        internal bool EnsureWorld()
        {
            return recovery.EnsureWorld();
        }

        internal bool EnsureMain(int timeoutMs)
        {
            return recovery.EnsureMain(timeoutMs);
        }

        private void Report(string state, bool finished = false)
        {
            RunLog.Info("体力本", string.Format("{0} · 轮次={1} · 已用体力={2}", state, runs, usedStamina));
            progress.Report(new DomainReport
            {
                State = state, Runs = runs, UsedStamina = usedStamina, Finished = finished,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            RunLog.Warn("体力本", string.Format("任务停止 · {0} · 轮次={1} · 已用体力={2}", message, runs, usedStamina));
            Failed = true;
            Failure = message;
            progress.Report(new DomainReport
            {
                State = message, Failure = message, Runs = runs, UsedStamina = usedStamina,
                Finished = true, Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            if (ownsWorld)
                world.Dispose();
        }
    }
}
