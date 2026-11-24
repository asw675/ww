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
        public string Material { get; set; }
        public int MaxStamina { get; set; }

        public static readonly string[] Materials = { "共鸣者经验", "武器经验", "贝币" };

        public static DomainOptions Defaults()
        {
            return new DomainOptions { Kind = DomainKind.Tacet, Index = 1, Material = Materials[2] };
        }

        internal DomainOptions ForKind(DomainKind kind)
        {
            int last = kind == DomainKind.Forgery ? 20 : 21;
            return new DomainOptions
            {
                Kind = kind, Index = Math.Max(1, Math.Min(last, Index)), Material = Material, MaxStamina = MaxStamina
            };
        }

        public void Validate()
        {
            int last = Kind == DomainKind.Forgery ? 20 : 21;
            if (!Enum.IsDefined(typeof(DomainKind), Kind) || Kind == DomainKind.Cultivation ||
                Index < 1 || Index > last || MaxStamina < 0)
                throw new System.IO.InvalidDataException("体力本配置无效。");
            if (Material == null || Array.IndexOf(Materials, Material) < 0)
                Material = Materials[2];
        }
    }

    internal static class DomainMath
    {
        internal const double BookScrollBottom = .8806;
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
                    if (!line.Box.IsEmpty && line.Score >= .70f && Math.Abs(line.Box.Top - label.Top) <= 20 &&
                        int.TryParse(OcrText.Compact(line.Text), out amount) && amount == cost)
                        return label;
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
        internal bool NoStamina { get; private set; }
        internal int UsedStamina { get { return usedStamina; } }

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
                    Fail("已打开入口，未读到顶部结晶波片的当前值和上限。");
                    return;
                }
                CaptureSpendEvidence("stamina-header-read");
                if (!DomainMath.CanEnter(options.Kind, available.Current, available.Backup, mustUse, once))
                {
                    if (!available.BackupKnown &&
                        DomainMath.CanEnter(options.Kind, available.Current, 4800, mustUse, once))
                    {
                        Fail("备用体力读数缺失，无法确认是否满足入场条件。");
                        return;
                    }
                    CaptureSpendEvidence("stamina-insufficient");
                    if (!EnsureWorld())
                    {
                        if (!token.IsCancellationRequested)
                            Fail("体力不足，未确认返回大世界。");
                        return;
                    }
                    NoStamina = usedStamina == 0;
                    CompletionVerified = true;
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
                    if (!token.IsCancellationRequested) Fail("传送进副本失败。");
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
                    bool returned = options.Kind == DomainKind.Tacet || EnsureWorld();
                    if (!token.IsCancellationRequested)
                        Fail(returned ? "连续 3 次未能正常战斗或恢复，停止刷取。" :
                            "连续 3 次未能正常战斗或恢复，未确认返回大世界。");
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
            while (!token.IsCancellationRequested)
            {
                Report("进入战斗");
                if (!tacet)
                {
                    if (!WalkUntilF(4000) || !PickF())
                        return StopDomain("未确认开始挑战的交互提示。");
                    DismissWrongClaim();
                }
                CombatOutcome combat = CombatRunner.Engage(world, settings, token, 300000, 5000,
                    seekTargets: options.Kind == DomainKind.Tacet);
                RunLog.Info("体力本", "战斗结果 · " + combat);
                bool rewardReached = false;
                if (combat == CombatOutcome.Finished)
                {
                    if (options.Kind != DomainKind.Tacet) world.Sleep(3000);
                    rewardReached = WalkToTreasure();
                    if (!rewardReached && options.Kind == DomainKind.Tacet &&
                        world.WaitUntil(() => world.InRealm && world.Hud != null &&
                            (world.Hud.EnemyHealth || world.Hud.Locked), 5000))
                    {
                        Report("宝箱未出现且战斗仍在进行，原地继续战斗");
                        combat = CombatRunner.Engage(world, settings, token, 300000, 5000, seekTargets: true);
                        if (combat == CombatOutcome.Finished)
                            rewardReached = WalkToTreasure();
                    }
                }
                if (token.IsCancellationRequested) return false;
                if (combat == CombatOutcome.Dead)
                {
                    if (!RecoverAfterDeath()) return StopDomain("角色倒下后未确认退本回血。");
                    return false;
                }
                if (!tacet && combat == CombatOutcome.NoCombat)
                {
                    Report("等待开战超时，退出副本重试");
                    return false;
                }
                if (combat != CombatOutcome.Finished)
                    return StopDomain("未确认战斗结束（" + combat + "）。");
                if (!rewardReached)
                    return StopDomain("战斗结束后未到达宝箱交互位置。");
                PickF();
                if (tacet)
                {
                    world.Sleep(2000);
                    if (!world.Tick()) return StopDomain("无法读取宝箱交互后的画面。");
                    if (!HasClaimStamina())
                    {
                        Report("当前交互没有打开奖励，返回挑战");
                        if (!EscCancel()) return StopDomain("退出交互后未确认返回挑战。");
                        continue;
                    }
                }
                int claimedBefore = runs;
                bool canContinue;
                if (!UseStamina(ref mustUse, out canContinue))
                    return false;
                if (runs == claimedBefore)
                {
                    return EndInWorld();
                }
                world.Sleep(4000);
                if (!canContinue)
                {
                    if (tacet ? world.ClickCentered(.365, .853) : world.Click(.42, .84))
                        world.Sleep(1800);
                    return EndInWorld();
                }
                if (!Rechallenge())
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

        private bool Rechallenge()
        {
            if (options.Kind == DomainKind.Tacet)
            {
                Rectangle button = Rectangle.Empty;
                world.WaitUntil(() =>
                {
                    if (world.VerifiedWorld || world.Ui == null || world.Ui.ClaimReward)
                        return false;
                    button = DomainMath.RechallengeButton(world.Read(.54, .81, .72, .90));
                    return !button.IsEmpty;
                }, 3000);
                if (button.IsEmpty)
                {
                    if (!world.Captured || world.VerifiedWorld || world.Ui == null || world.Ui.ClaimReward ||
                        !world.ClickCentered(.640, .851))
                        return false;
                }
                else if (!world.Click(button))
                    return false;
            }
            else
            {
                if (!world.Click(.68, .84)) return false;
                world.Sleep(1000);
                if (!ConfirmDomainRestart()) return false;
                return WaitTeam();
            }
            world.Sleep(200);
            WaitForEntryDialog();
            return WaitTeam();
        }

        private bool ConfirmDomainRestart()
        {
            Rectangle button = Rectangle.Empty;
            int route = world.InputRouteVersion;
            Func<bool> find = () => !world.VerifiedWorld && world.Vision.FindOptionalButton(world.Frame, .60, out button);
            if (!world.WaitUntil(find, 2000)) return !token.IsCancellationRequested;
            if (route != world.InputRouteVersion || !world.Click(.49, .55)) return false;
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
            if (!world.WaitUntil(() =>
            {
                heading = DomainMath.BookKindButton(world.Read(.095, .11, .35, .91), kind);
                return !heading.IsEmpty;
            }, 5000))
                return false;
            if (DomainMath.IsHighlightedBookKind(world.Frame, heading))
                return true;
            if (!world.ClickMenuButton(heading))
                return false;
            return world.WaitUntil(() =>
            {
                heading = DomainMath.BookKindButton(world.Read(.095, .11, .35, .91), kind);
                return DomainMath.IsHighlightedBookKind(world.Frame, heading);
            }, 5000);
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
                        if (!world.Click(0.891, 0.910)) return false;
                        world.Sleep(1000);
                    }
                    break;
                default:
                    if (!world.Click(0.9730, 0.8806)) return false;
                    world.Sleep(1000);
                    int index = options.Material == DomainOptions.Materials[0] ? 0 :
                        options.Material == DomainOptions.Materials[1] ? 1 : 2;
                    if (!world.Click(0.898, 0.533 + index * 0.14)) return false;
                    world.Sleep(1000);
                    if (!world.Click(0.93, 0.90)) return false;
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
                rows = ReadTargetRows();
                if (rows.Count == 0) return BookDestination.None;
                if (index < rows.Count)
                    return ClickTargetRow(rows[index], route);
            }
            double offset = DomainMath.ScrollOffset(top, serial, total, structure);
            if (!ClickBookScrollbar(offset, route))
                return BookDestination.None;
            world.Sleep(1000);
            rows = ReadTargetRows();
            if (serial > 4 && structure != null && (rows.Count == 3 || rows.Count == 4) &&
                rows[rows.Count - 1].Y < world.Frame.Height * .73)
            {
                double rowHeight = DomainMath.BookRowHeight(top, total, structure);
                if (!ClickBookScrollbar(Math.Min(DomainMath.BookScrollBottom, offset + (rowHeight > 0 ? rowHeight * .55 : .018)), route))
                    return BookDestination.None;
                world.Sleep(1000);
                rows = ReadTargetRows();
            }
            return rows.Count == 0 ? BookDestination.None : ClickTargetRow(rows[rows.Count - 1], route);
        }

        private bool ClickBookScrollbar(double y, int route)
        {
            if (!world.Captured) return false;
            return world.ClickOnRoute(GuidebookList.PointAt(new Size(world.Frame.Width, world.Frame.Height), .973, y), route);
        }

        private List<Rectangle> ReadTargetRows()
        {
            return world.Tick() && world.Ui != null ? world.Ui.BossRows : new List<Rectangle>();
        }

        private BookDestination ClickTargetRow(Rectangle row, int route)
        {
            if (!world.ClickOnRoute(row, route))
                return BookDestination.None;
            world.Sleep(1000);
            BookDestination observed = BookDestination.None;
            long since = world.Now;
            bool stable = world.WaitUntil(delegate
            {
                if (world.InputRouteVersion != route) return true;
                BookDestination current = ReadBookDestination();
                if (observed != current) since = world.Now;
                observed = current;
                return observed != BookDestination.None && world.Now - since >= 500;
            }, 10000);
            return stable && world.InputRouteVersion == route ? observed : BookDestination.None;
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
            if (!world.WaitClick("team_start_challenge", 10000, .75))
                return false;
            WaitForEntryDialog();
            return true;
        }

        internal bool StartTeamChallenge()
        {
            return TeamChallenge() && WaitTeam();
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

        internal bool WalkToTreasure()
        {
            if (!ApproachTreasure(true) && (token.IsCancellationRequested || !ApproachTreasure(false))) return false;
            if (!WalkUntilF(2000)) return false;
            world.Sleep(1000);
            return !token.IsCancellationRequested;
        }

        private bool ApproachTreasure(bool requireClaim)
        {
            var watch = Stopwatch.StartNew();
            char held = '\0';
            bool centered = false;
            int route = world.InputRouteVersion;
            Rectangle target = Rectangle.Empty;
            try
            {
                while (watch.ElapsedMilliseconds < 30000 && !token.IsCancellationRequested)
                {
                    if (!world.Tick() || world.InputRouteVersion != route || world.Ui == null) return false;
                    if (HasInteraction() && (!requireClaim || IsRewardPrompt())) return true;
                    if (world.Ui.Treasure) target = world.Ui.TreasureBox;
                    if (!target.IsEmpty)
                    {
                        char direction = DomainMath.TreasureDirection(target, new Size(world.Frame.Width, world.Frame.Height),
                            held, ref centered);
                        if (held != direction)
                        {
                            if (held != '\0') world.KeyUp(held);
                            held = '\0';
                            if (!world.KeyDown(direction)) return false;
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
            var area = new Rectangle(key.Left + 5 * key.Width, key.Top - key.Height,
                7 * key.Width, 6 * key.Height);
            foreach (OcrLine line in world.Ocr.Read(world.Frame, area))
            {
                string text = line.Text ?? "";
                if (line.Score >= .6f && (text.Contains("领取") || text.Contains("領取") ||
                    text.IndexOf("claim", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            }
            return false;
        }

        private bool UseStamina(ref int mustUse, out bool canContinue)
        {
            canContinue = false;
            StaminaReadout before = default(StaminaReadout);
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
            if (!HasClaimStamina() || world.InputRouteVersion != route)
                return UnverifiedSpend("领取前的体力领奖界面不再可确认。");
            if (!world.ClickOnRoute(button, route))
                return UnverifiedSpend("体力领取点击未发送。");

            StaminaReadout debitBaseline = before;
            int stableDebit = 0;
            bool replenishment = false, sawReceipt = false, routeChanged = false;
            Func<bool> observeClaim = delegate
            {
                if (world.InputRouteVersion != route)
                {
                    routeChanged = true;
                    return true;
                }
                Rectangle gem;
                replenishment = world.Vision.FindLabel(world.Frame, "gem_add_stamina", .80, out gem);
                if (replenishment)
                    return true;
                StaminaReadout observed;
                if (ReadStaminaHeader(out observed) && ExpectedStaminaDebit(debitBaseline, observed, used))
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
            bool confirmed = world.WaitUntil(observeClaim, 9000);
            if (confirmed && replenishment && !routeChanged && world.InputRouteVersion == route)
            {
                if (!ConfirmSelectedRefillAndRetry(before, used, useDouble, route, out debitBaseline))
                    return UnverifiedSpend("未确认所选方式已补充足够波片并返回领奖界面。");
                stableDebit = 0;
                replenishment = false;
                confirmed = world.WaitUntil(observeClaim, 9000);
            }
            if (routeChanged || world.InputRouteVersion != route)
                return UnverifiedSpend("领取后游戏窗口或输入方式发生变化，无法核对本次扣除。");
            if (!confirmed || replenishment)
                return UnverifiedSpend(replenishment ? "游戏显示补充结晶波片，领取费用未扣除。"
                    : "点击后未确认体力扣减或新奖励回执。");

            CaptureSpendEvidence(sawReceipt ? "stamina-spend-receipt" : "stamina-debit-confirmed");
            canContinue = DomainMath.CanContinue(before.Current, before.Backup, used, mustUse, once);
            mustUse -= used;
            usedStamina += used;
            runs++;
            RunLog.Info("体力本", string.Format("本次确认使用={0} · 依据={1} · 累计={2} · 继续下一轮={3}",
                used, sawReceipt ? "奖励回执" : "同一体力计数", usedStamina, canContinue));
            return true;
        }

        internal static Rectangle RefillConfirmButton(IEnumerable<OcrLine> lines)
        {
            return DailyClaimVision.UniqueButton(lines, "确认", "確認", "确定", "確定", "Confirm");
        }

        private Rectangle ReadRefillConfirm()
        {
            return RefillConfirmButton(world.Read(.55, .60, .85, .82));
        }

        private bool ConfirmSelectedRefillAndRetry(StaminaReadout before, int cost, bool useDouble, int route,
            out StaminaReadout debitBaseline)
        {
            debitBaseline = default(StaminaReadout);
            CaptureSpendEvidence("stamina-refill-method");
            Rectangle gem;
            if (!world.Tick() || world.InputRouteVersion != route || !world.Vision.FindLabel(world.Frame,
                "gem_add_stamina", .80, out gem)) return false;
            var cards = new Rectangle((int)(world.Frame.Width * .36), (int)(world.Frame.Height * .36),
                (int)(world.Frame.Width * .27), (int)(world.Frame.Height * .16));
            int[] selection = BattlePassVision.ButtonPatch(world.Frame, cards);
            if (!world.ClickOnRoute(ReadRefillConfirm(), route)) return false;

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
                if (!world.ClickOnRoute(confirmation, route)) return false;
                world.Sleep(1000);
            }
            StaminaReadout replenished = default(StaminaReadout);
            if (!world.WaitUntil(() => world.InputRouteVersion == route && ReadStaminaHeader(out replenished) &&
                replenished.Limit == before.Limit && replenished.Current > before.Current &&
                replenished.Current >= cost, 4000)) return false;
            CaptureSpendEvidence("stamina-refill-confirmed");
            if (!HasClaimStamina() && !world.Key((char)0x1B)) return false;
            if (!world.WaitUntil(() => HasClaimStamina() && ReadStaminaHeader(out replenished) &&
                replenished.Limit == before.Limit && replenished.Current >= cost, 3500) ||
                world.InputRouteVersion != route) return false;
            debitBaseline = replenished;
            return world.ClickOnRoute(FindSpendControl(useDouble, cost), route);
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
            return before.Limit == after.Limit &&
                Rectangle.Inflate(before.Position, 30, 10).IntersectsWith(after.Position) &&
                (debit == cost || debit == cost - 1);
        }

        private Rectangle FindSpendControl(bool useDouble, int cost)
        {
            var lines = useDouble ? world.Read(.56, .57, .76, .73) : world.Read(.23, .57, .47, .73);
            return DomainMath.SpendControl(lines, useDouble, cost);
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

        private bool EscCancel()
        {
            if (!world.Key((char)0x1B)) return false;
            world.Sleep(1000);
            if (world.Tick() && world.VerifiedWorld) return true;
            Rectangle cancel;
            if (!(world.FindNear("cancel_button_hcenter_vcenter", .80, out cancel) ||
                  world.FindNear("cancel_button_highlight_hcenter_vcenter", .80, out cancel)) ||
                !world.Click(cancel)) return false;
            world.Sleep(2000);
            return WaitTeam();
        }

        internal bool WaitTeam()
        {
            return world.WaitUntil(() => world.VerifiedWorld && world.TeamHud, 120000);
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
