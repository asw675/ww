using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    public sealed class CultivationOptions
    {
        public bool Ascension { get; set; }
        public bool Skills { get; set; }
        public bool Tacet { get; set; }
        public bool Nightmare { get; set; }

        public static CultivationOptions Defaults()
        {
            return new CultivationOptions { Ascension = true, Skills = true, Tacet = true, Nightmare = true };
        }
    }

    internal sealed class CultivationGoal
    {
        internal string Type, Name;
        internal int Owned, Required;
        internal int? WeeklyLeft;
    }

    internal sealed class CultivationEchoRow
    {
        internal bool Tacet;
        internal string Name;
        internal Rectangle Button;
        internal int? Defeated, Total;
        internal bool Done { get { return Total.HasValue && Defeated >= Total; } }
    }

    internal static class CultivationVision
    {
        internal const string Ascension = "共鸣者突破材料", Skills = "技能升级材料";
        private static readonly Regex Quantity = new Regex(@"^(\d{1,7})/(\d{1,7})$");
        private static readonly Regex Defeated = new Regex(@"已击败残象:?(\d+)/(\d+)");

        internal static bool IsPage(IEnumerable<OcrLine> lines)
        {
            var labels = new HashSet<string>(lines.Select(line => OcrText.Compact(line.Text)));
            return labels.Contains("培养目标") && labels.Overlaps(new[] { "所需材料", "已完成共鸣者培养", "声骸培养" });
        }

        internal static bool HasMaterialRows(IEnumerable<OcrLine> lines)
        {
            return lines.Any(line => OcrText.Compact(line.Text) == "所需材料");
        }

        internal static IEnumerable<string> PendingTypes(IList<CultivationGoal> goals)
        {
            foreach (string type in new[] { Ascension, Skills })
            {
                var row = goals.Where(goal => goal.Type == type).ToList();
                if (row.Any(goal => goal.Owned < goal.Required) &&
                    (type != Skills || row.Min(goal => goal.WeeklyLeft).GetValueOrDefault() != 0))
                    yield return type;
            }
        }

        internal static Tuple<int, int> ParseQuantity(string text)
        {
            Match match = Quantity.Match(OcrText.Compact(text));
            if (!match.Success)
                return null;
            int required = int.Parse(match.Groups[2].Value);
            return required == 0 ? null : Tuple.Create(int.Parse(match.Groups[1].Value), required);
        }

        internal static List<CultivationEchoRow> EchoRows(List<OcrLine> lines, Size size)
        {
            var rows = new List<CultivationEchoRow>();
            var right = lines.Where(line => line.Box.Left > size.Width * .35).ToList();
            var buttons = right.Where(line => OcrText.Compact(line.Text) == "直接挑战").ToList();
            foreach (OcrLine label in right)
            {
                string name = OcrText.Compact(label.Text);
                bool tacet = name.EndsWith("无音区", StringComparison.Ordinal);
                if (!tacet && (!name.EndsWith("聚落", StringComparison.Ordinal) || name.StartsWith("产出", StringComparison.Ordinal)))
                    continue;
                double center = label.Box.Top + label.Box.Height / 2.0;
                var nearby = buttons.Select(line => new { line.Box, Distance = Math.Abs(line.Box.Top + line.Box.Height / 2.0 - center) })
                    .Where(candidate => candidate.Distance < size.Height * .06).OrderBy(candidate => candidate.Distance).FirstOrDefault();
                if (nearby == null)
                    continue;
                var row = new CultivationEchoRow { Tacet = tacet, Name = name, Button = nearby.Box };
                if (!tacet)
                    foreach (OcrLine line in right)
                    {
                        int offset = line.Box.Y - label.Box.Y;
                        if (offset <= 0 || offset >= size.Height * .08)
                            continue;
                        Match match = Defeated.Match(OcrText.Compact(line.Text));
                        if (!match.Success)
                            continue;
                        int defeated, total;
                        if (int.TryParse(match.Groups[1].Value, out defeated) &&
                            int.TryParse(match.Groups[2].Value, out total))
                        {
                            row.Defeated = defeated;
                            row.Total = total;
                        }
                    }
                rows.Add(row);
            }
            return rows;
        }

        internal static void UpdateInventory(IList<CultivationGoal> goals, IDictionary<string, int> inventory)
        {
            if (inventory.Keys.Any(name => !goals.Any(goal => goal.Name == name)))
                throw new InvalidOperationException("结算库存包含培养目标以外的材料。");
            bool increased = false;
            foreach (CultivationGoal goal in goals)
            {
                int owned;
                if (!inventory.TryGetValue(goal.Name, out owned)) continue;
                if (owned < goal.Owned) throw new InvalidOperationException("结算库存少于领取前的数量。");
                if (goal.Owned < goal.Required && owned > goal.Owned) increased = true;
            }
            if (!increased) throw new InvalidOperationException("领取后目标材料库存未增加。");
            foreach (CultivationGoal goal in goals)
            {
                int owned;
                if (inventory.TryGetValue(goal.Name, out owned)) goal.Owned = owned;
            }
        }

        internal static void ApplyClaim(IList<CultivationGoal> goals, string type, IDictionary<string, int> inventory,
            int before, int remaining, int cost)
        {
            if (remaining > before - cost + 1)
                throw new InvalidOperationException("未确认培养奖励扣除 " + cost + " 点体力。");
            foreach (CultivationGoal goal in goals)
                if (type == Skills && goal.Type == type && goal.WeeklyLeft.HasValue) goal.WeeklyLeft--;
            UpdateInventory(goals, inventory);
        }
    }

    internal sealed class CultivationRunner : IDisposable
    {
        private const int Cost = 60;
        private static readonly Regex Weekly = new Regex(@"本周剩余可收取次数:?(\d+)/\d+");
        private static readonly Regex Owned = new Regex(@"^拥有:?(\d{1,7})$");
        private static readonly Regex Remaining = new Regex(@"^剩余(\d{1,7})$");
        private static readonly Regex ClaimCost = new Regex(@"领取奖励需消耗\s*(\d+)\s*点结晶波片");
        private static readonly Regex RewardSlot = new Regex(@"^[xX×]\d{1,7}$");

        private sealed class MaterialRow
        {
            internal string Type;
            internal List<Tuple<Rectangle, int, int>> Quantities;
            internal int? WeeklyLeft;
            internal string Key
            {
                get { return string.Join("|", Quantities.Select(entry => entry.Item2 + "/" + entry.Item3)) + ":" + WeeklyLeft; }
            }
        }

        private readonly CombatSettings settings;
        private readonly CultivationOptions options;
        private readonly WorldSession world;
        private readonly bool ownsWorld;
        private readonly DomainOptions tacetOptions = DomainOptions.Defaults();
        private readonly DomainRunner domain;
        private readonly IProgress<DomainReport> progress;
        private readonly CancellationToken token;
        private readonly Action<string, PixelFrame> record;
        private List<CultivationGoal> goals = new List<CultivationGoal>();
        private int materialSpent;
        private NightmareNestRunner echoSearch;

        internal CultivationRunner(CombatSettings settings, CancellationToken token, IProgress<DomainReport> progress,
            Action<string, PixelFrame> record = null, WorldSession session = null)
        {
            this.settings = settings;
            options = settings.Cultivation ?? CultivationOptions.Defaults();
            this.token = token;
            this.progress = progress;
            this.record = record;
            ownsWorld = session == null;
            world = session ?? new WorldSession(settings, token);
            domain = new DomainRunner(settings, tacetOptions, token, new StepProgress<DomainReport>(report =>
                Report(report.State, report.UsedStamina)), record, world);
        }

        internal int Run(int? dailyBudget = null)
        {
            goals.Clear();
            materialSpent = 0;
            int tacetSpent = 0;
            string stopReason = null;
            try
            {
                if (!world.Tick() || world.Window.Language != GameLanguage.SimplifiedChinese ||
                    Math.Abs(world.Frame.SourceWidth / (double)world.Frame.SourceHeight - 16.0 / 9) > .01)
                    throw new InvalidOperationException("培养目标需要简体中文的 16:9 游戏画面。");
                Require(domain.EnsureWorld(), "培养目标开始前未确认已进入大世界。");
                if (!(options.Ascension || options.Skills || options.Nightmare || options.Tacet))
                {
                    Report("未启用培养项目。");
                    return 0;
                }
                if (options.Nightmare)
                    FarmNightmare();
                if (options.Ascension || options.Skills)
                {
                    Require(OpenPage(), "无法打开培养目标。请先在索拉指南设置培养目标。");
                    goals = ReadGoals();
                    if (CultivationVision.PendingTypes(goals).Any())
                        stopReason = FarmMaterials();
                    else
                    {
                        if (goals.Count > 0 && goals.All(goal => goal.Owned >= goal.Required))
                            stopReason = "培养材料已达到目标";
                        Require(domain.EnsureMain(30000), "结束材料项目后未确认返回大世界。");
                    }
                }

                if (options.Tacet && !CultivationVision.PendingTypes(goals).Any())
                {
                    CultivationEchoRow row = FindEchoRow(true);
                    int returnTimeout = 30000;
                    if (row != null && !dailyBudget.HasValue && ReadStamina() < Cost)
                        stopReason = "体力不足 " + Cost + " 点";
                    else if (row != null)
                    {
                        Report("刷取培养目标无音区 " + row.Name);
                        tacetOptions.MaxStamina = Math.Max(0, dailyBudget.GetValueOrDefault() - materialSpent);
                        domain.Run(OpenPage, EnterEchoTacet);
                        token.ThrowIfCancellationRequested();
                        if (domain.Failed)
                            throw new InvalidOperationException(domain.Failure);
                        tacetSpent = domain.UsedStamina;
                        stopReason = "已执行声骸培养无音区";
                        returnTimeout = 60000;
                    }
                    Require(domain.EnsureMain(returnTimeout), "结束培养无音区后未确认返回大世界。");
                }
                int total = materialSpent + tacetSpent;
                Report((stopReason ?? "培养目标完成") + "，确认消耗 " + total + " 点体力。", tacetSpent);
                return total;
            }
            catch (Exception error)
            {
                if (token.IsCancellationRequested || error is InputElevationRequiredException)
                    throw;
                if (record != null && world.Captured)
                    record("cultivation-failed", world.Frame);
                try { LeaveChallenge(); }
                catch (InvalidOperationException leaveError) { RunLog.Warn("培养目标", leaveError.Message); }
                throw;
            }
        }

        private bool OpenPage()
        {
            if (CultivationVision.IsPage(Headers()))
                return true;
            return domain.OpenBookCategory(DomainKind.Cultivation) &&
                world.WaitUntil(() => CultivationVision.IsPage(Headers()), 5000);
        }

        private List<OcrLine> Headers() { return world.Read(.10, .12, .70, .26); }

        private List<CultivationGoal> ReadGoals()
        {
            var found = new List<CultivationGoal>();
            if (!(options.Ascension || options.Skills) || !CultivationVision.HasMaterialRows(Headers()))
                return found;
            var rows = new List<MaterialRow>();
            foreach (string type in new[] { CultivationVision.Ascension, CultivationVision.Skills })
            {
                if (!(type == CultivationVision.Ascension ? options.Ascension : options.Skills))
                    continue;
                string expected = Stable(() =>
                {
                    MaterialRow row = ReadMaterialRow(type);
                    return row == null ? null : row.Key;
                }, type + "数量");
                MaterialRow observed = ReadMaterialRow(type);
                if (observed == null || observed.Key != expected || type == CultivationVision.Skills && !observed.WeeklyLeft.HasValue)
                    throw new InvalidOperationException("材料数量或本周剩余收取次数未核对一致。");
                rows.Add(observed);
            }
            foreach (MaterialRow observed in rows)
            {
                string type = observed.Type;
                foreach (var quantity in observed.Quantities)
                {
                    Require(world.Click(quantity.Item1, 20), "无法打开培养材料详情。");
                    world.Sleep(400);
                    Tuple<string, int> detail;
                    try { detail = Stable(() => ItemDetail(type), "材料详情"); }
                    finally { CloseDetail(); }
                    if (detail.Item2 != quantity.Item2 || found.Any(goal => goal.Name == detail.Item1))
                        throw new InvalidOperationException("培养页数量和材料详情里的库存未核对一致。");
                    found.Add(new CultivationGoal { Type = type, Name = detail.Item1, Owned = detail.Item2,
                        Required = quantity.Item3, WeeklyLeft = observed.WeeklyLeft });
                }
            }
            foreach (CultivationGoal goal in found)
                RunLog.Info("培养目标", goal.Name + " 已拥有 " + goal.Owned + "/" + goal.Required +
                    (goal.WeeklyLeft.HasValue ? "，本周可领取 " + goal.WeeklyLeft.Value + " 次" : ""));
            return found;
        }

        private MaterialRow ReadMaterialRow(string type)
        {
            List<OcrLine> labels = world.Read(.36, .18, .64, .75).Where(line => OcrText.Compact(line.Text) == type).ToList();
            if (labels.Count != 1)
                return null;
            double y = (labels[0].Box.Top + labels[0].Box.Height / 2.0) / world.Frame.Height;
            var quantities = new List<Tuple<Rectangle, int, int>>();
            int? weekly = null;
            foreach (OcrLine line in world.Read(.36, y + .01, .82, y + .075))
            {
                Tuple<int, int> value = CultivationVision.ParseQuantity(line.Text);
                if (value != null)
                    quantities.Add(Tuple.Create(line.Box, value.Item1, value.Item2));
                else if (type == CultivationVision.Skills && !weekly.HasValue)
                {
                    Match match = Weekly.Match(OcrText.Compact(line.Text));
                    int count;
                    if (match.Success && int.TryParse(match.Groups[1].Value, out count)) weekly = count;
                }
            }
            return quantities.Count == 0 ? null : new MaterialRow { Type = type, WeeklyLeft = weekly,
                Quantities = quantities.OrderBy(entry => entry.Item1.Left).ToList() };
        }

        private Tuple<string, int> ItemDetail(params string[] acceptedTypes)
        {
            List<OcrLine> lines = world.Read(.20, .08, .80, .90);
            var types = lines.Where(line => acceptedTypes.Contains(OcrText.Compact(line.Text))).ToList();
            if (types.Count != 1)
                return null;
            Rectangle box = types[0].Box;
            var title = lines.Where(line => box.Top > line.Box.Top && box.Top - line.Box.Top < world.Frame.Height * .10 &&
                Math.Abs(box.Left - line.Box.Left) < world.Frame.Width * .04).OrderByDescending(line => line.Box.Top).FirstOrDefault();
            int? owned = ReadNumber(lines, Owned);
            return title != null && owned.HasValue ? Tuple.Create(OcrText.Compact(title.Text), owned.Value) : null;
        }

        private void CloseDetail()
        {
            if (!token.IsCancellationRequested && world.Click(.5, .94, 20))
                world.Sleep(300);
        }

        private string FarmMaterials()
        {
            int stamina = ReadStamina();
            if (stamina < Cost)
            {
                Require(domain.EnsureMain(30000), "体力不足，未确认返回大世界。");
                return "体力不足 " + Cost + " 点";
            }
            string reason = RunMaterialChallenges(stamina / Cost);
            LeaveChallenge();
            return reason;
        }

        private string RunMaterialChallenges(int limit)
        {
            string[] pending = CultivationVision.PendingTypes(goals).ToArray();
            foreach (string type in pending)
            {
                if (limit <= 0) return "已用完本次材料挑战的体力预算";
                EnterMaterial(type);
                while (true)
                {
                    if (!FightMaterial(type))
                        return "体力不足 " + Cost + " 点";
                    PickNearbyEcho();
                    int before = ClaimReward();
                    IDictionary<string, int> inventory = RewardInventory(type);
                    int stamina = Stable(() => ReadNumber(world.Read(.48, .87, .75, .97), Remaining), "结算剩余体力").Value;
                    CultivationVision.ApplyClaim(goals, type, inventory, before, stamina, Cost);
                    materialSpent += Cost;
                    limit--;
                    foreach (CultivationGoal target in goals.Where(goal => inventory.ContainsKey(goal.Name)))
                        Report(target.Name + " 已核对库存 " + target.Owned + "/" + target.Required);
                    if (goals.Where(goal => goal.Type == type).All(goal => goal.Owned >= goal.Required))
                    {
                        if (goals.All(goal => goal.Owned >= goal.Required)) return "培养材料已达到目标";
                        break;
                    }
                    if (stamina < Cost) return "体力不足 " + Cost + " 点";
                    if (limit <= 0) return "已用完本次材料挑战的体力预算";
                    if (!CultivationVision.PendingTypes(goals).Contains(type))
                        return "技能升级材料本周剩余可收取次数为 0";
                    RestartMaterial();
                }
                if (pending.Last() != type)
                {
                    LeaveChallenge();
                    Require(OpenPage(), "退出副本后未回到培养目标。");
                }
            }
            return null;
        }

        private void EnterMaterial(string type)
        {
            Require(ReadMaterialRow(type) != null, "未找到培养目标上的材料行。");
            Require(world.Click(.900, type == CultivationVision.Ascension ? .320 : .597, 20), "无法点击材料挑战。");
            world.Sleep(1000);
            if (!world.WaitFind("team_start_challenge", 10000, .75))
            {
                Require(world.Click(.800, .910, 20), "无法点击单人挑战。");
                world.Sleep(1000);
                Require(world.WaitFind("team_start_challenge", 20000, .75), "未识别到队伍挑战界面。");
            }
            Require(domain.StartTeamChallenge(), "未确认已进入培养材料挑战。");
        }

        private bool FightMaterial(string type)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                CombatOutcome outcome = CombatRunner.Engage(world, settings, token, 600000, 30000, seekTargets: true,
                    timeoutFromEntry: true);
                if (outcome == CombatOutcome.Finished)
                    return true;
                token.ThrowIfCancellationRequested();
                if (attempt == 1 || outcome == CombatOutcome.Timeout)
                    throw new InvalidOperationException("培养材料挑战未完成，战斗结果 " + outcome + "。");
                Require(domain.EnsureWorld() && OpenPage(), "战斗失败后未返回培养目标。");
                List<CultivationGoal> observed = ReadGoals();
                Require(observed.Select(GoalKey).SequenceEqual(goals.Select(GoalKey)), "重新进本前，培养目标或库存发生了变化。");
                if (ReadStamina() < Cost)
                {
                    Report("体力不足 " + Cost + " 点，结束材料挑战。");
                    return false;
                }
                EnterMaterial(type);
            }
            return false;
        }

        private static string GoalKey(CultivationGoal goal)
        {
            return goal.Type + "|" + goal.Name + "|" + goal.Owned + "/" + goal.Required + "|" + goal.WeeklyLeft;
        }

        private void PickNearbyEcho()
        {
            try
            {
                if (echoSearch == null)
                    echoSearch = new NightmareNestRunner(settings, new NightmareOptions { CaptureOnce = true }, token,
                        new StepProgress<NightmareReport>(), record, world);
                echoSearch.CollectEcho();
            }
            catch (Exception error)
            {
                if (token.IsCancellationRequested || error is InputElevationRequiredException)
                    throw;
                RunLog.Warn("培养目标", "声骸拾取未完成，继续核对材料奖励。 " + error.Message);
            }
        }

        private int ReadStamina()
        {
            return Stable(() =>
            {
                DomainRunner.StaminaReadout count;
                return domain.ReadStaminaHeader(out count) ? (int?)count.Current : null;
            }, "当前体力").Value;
        }

        private int ClaimReward()
        {
            for (int attempt = 0; attempt < 2 && !HasClaim(); attempt++)
            {
                Require(domain.WalkToTreasure(), "未找到培养材料奖励宝箱，请确认战斗已结束。");
                domain.PickF();
                world.WaitUntil(HasClaim, 3000);
            }
            Require(HasClaim(), "未确认培养材料的领奖弹窗。");
            int route = world.InputRouteVersion;
            int before = ReadStamina();
            if (before < Cost)
            {
                Require(world.Key((char)0x1B, route: route), "体力不足，取消领取的按键未发送。");
                world.Sleep(500);
                throw new InvalidOperationException("体力不足 60 点，已取消领取。");
            }
            world.Sleep(1000);
            Require(HasClaim() && world.InputRouteVersion == route, "领取前的培养领奖界面不再可确认。");
            int? cost = ReadCost();
            Require(!cost.HasValue || cost == Cost, "培养材料领取费用与 60 点体力不符。");
            Rectangle confirm;
            string button = cost.HasValue ? "confirm_btn" : "cancel_button";
            Require(world.FindNear(button + "_hcenter_vcenter", .80, out confirm) ||
                world.FindNear(button + "_highlight_hcenter_vcenter", .80, out confirm), "未找到培养材料单倍领取按钮。");
            Require(world.ClickOnRoute(confirm, route, 20), "领取点击未发送。");
            world.Sleep(2000);
            Require(world.WaitUntil(() => world.InputRouteVersion != route || ResultButtons() != null, 15000),
                "未确认培养奖励回执，已停止扣费流程。");
            Require(world.InputRouteVersion == route, "领取后游戏窗口或输入方式发生变化，无法核对培养奖励。");
            return before;
        }

        private bool HasClaim()
        {
            return world.Tick() && !world.TeamHud && (world.Ui != null && world.Ui.ClaimReward || ReadCost().HasValue);
        }

        private int? ReadCost()
        {
            foreach (OcrLine line in world.Read(.25, .40, .75, .55))
            {
                Match match = ClaimCost.Match(line.Text);
                if (!match.Success) continue;
                int value;
                return int.TryParse(match.Groups[1].Value, out value) ? value : -1;
            }
            return null;
        }

        private IDictionary<string, int> RewardInventory(string type)
        {
            string[] names = goals.Where(goal => goal.Type == type).Select(goal => goal.Name).ToArray();
            Require(names.Length == 1, "培养奖励目标材料数量未核对。");
            int slots = world.Read(.12, .40, .89, .68).Count(line => RewardSlot.IsMatch(OcrText.Compact(line.Text)));
            Require(slots >= 1 && slots <= 12, "未识别培养奖励的结算槽。");
            Require(world.Click(.5 + (4 - (slots - 1) / 2.0) * .0878, .526, 20), "无法打开培养奖励材料详情。");
            world.Sleep(400);
            Tuple<string, int> inventory;
            try { inventory = Stable(() => ItemDetail(CultivationVision.Ascension, CultivationVision.Skills), "领取后的材料库存"); }
            finally { CloseDetail(); }
            Require(inventory.Item1 == names[0], "结算奖励第 5 格与目标材料名称不符。");
            return new Dictionary<string, int> { { inventory.Item1, inventory.Item2 } };
        }

        private Tuple<Rectangle, Rectangle> ResultButtons()
        {
            List<OcrLine> lines = world.Read(.25, .78, .76, .90);
            var leave = lines.LastOrDefault(line => OcrText.Compact(line.Text) == "退出副本");
            var again = lines.LastOrDefault(line => OcrText.Compact(line.Text) == "重新挑战");
            return leave == null || again == null ? null : Tuple.Create(leave.Box, again.Box);
        }

        private void RestartMaterial()
        {
            var buttons = ResultButtons();
            Require(buttons != null && world.Click(buttons.Item2, 20), "未确认培养材料的重新挑战按钮。");
            world.Sleep(1000);
            domain.WaitForEntryDialog();
            Require(domain.WaitTeam(), "重新挑战后未确认进入副本。");
        }

        private void LeaveChallenge()
        {
            var buttons = ResultButtons();
            if (buttons != null)
            {
                Require(world.Click(buttons.Item1, 20), "退出培养挑战的点击未发送。");
                world.Sleep(1000);
                Require(world.WaitUntil(() => world.VerifiedWorld && !world.InRealm, 100000), "未确认退出培养挑战。");
            }
            else
                Require(domain.EnsureWorld(), "未确认返回大世界。");
        }

        private CultivationEchoRow FindEchoRow(bool tacet)
        {
            Require(OpenPage(), "无法打开培养目标。");
            for (int attempt = 0; attempt <= 3; attempt++)
            {
                var lines = world.Read(.35, .12, .99, .95);
                CultivationEchoRow row = CultivationVision.EchoRows(lines, new Size(world.Frame.Width, world.Frame.Height))
                    .FirstOrDefault(candidate => candidate.Tacet == tacet);
                if (row != null)
                    return row;
                if (attempt < 3)
                {
                    var size = new Size(world.Frame.Width, world.Frame.Height);
                    Require(world.Input.Scroll(-5, new Point((int)(size.Width * .68), (int)(size.Height * .60)),
                        size, world.Bounds, token), "培养目标页滚动未发送。");
                    world.Sleep(1000);
                }
            }
            return null;
        }

        private bool EnterEchoTacet()
        {
            CultivationEchoRow row = FindEchoRow(true);
            if (row == null || !world.Click(row.Button, 20)) return false;
            world.Sleep(1000);
            return world.WaitFind("team_start_challenge", 20000, .75);
        }

        private void FarmNightmare()
        {
            CultivationEchoRow row = FindEchoRow(false);
            if (row == null)
                return;
            if (row.Done) return;
            Report("挑战培养目标的梦魇巢穴 " + row.Name);
            using (var runner = new NightmareNestRunner(settings, NightmareOptions.Defaults(), token,
                new StepProgress<NightmareReport>(report => Report(report.State)), record, world))
            {
                if (!runner.RunSelectedNest(row.Name, row.Button))
                    throw new InvalidOperationException(runner.Failure ?? "未确认完成培养梦魇巢穴。");
            }
            Require(domain.EnsureMain(60000), "梦魇巢穴挑战后未确认返回大世界。");
            CultivationEchoRow after = FindEchoRow(false);
            if (after != null && after.Total.HasValue)
                Report("培养梦魇巢穴已击败残象 " + after.Defeated + "/" + after.Total);
            Require(domain.EnsureMain(30000), "查看巢穴进度后未确认返回大世界。");
        }

        private static int? ReadNumber(IEnumerable<OcrLine> lines, Regex pattern)
        {
            int? value = null;
            foreach (OcrLine line in lines)
            {
                Match match = pattern.Match(OcrText.Compact(line.Text));
                if (!match.Success)
                    continue;
                int observed = int.Parse(match.Groups[1].Value);
                if (value.HasValue)
                    return null;
                value = observed;
            }
            return value;
        }

        private T Stable<T>(Func<T> read, string label)
        {
            T earlier = default(T);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                T current = read();
                if ((object)current != null && EqualityComparer<T>.Default.Equals(current, earlier))
                    return current;
                earlier = current;
                world.Sleep(300);
            }
            throw new InvalidOperationException(label + "两次读数未核对一致。");
        }

        private void Require(bool condition, string message)
        {
            token.ThrowIfCancellationRequested();
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private void Report(string message, int tacetSpent = 0)
        {
            RunLog.Info("培养目标", message);
            progress.Report(new DomainReport { State = message, UsedStamina = materialSpent + tacetSpent,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty });
        }

        public void Dispose()
        {
            if (echoSearch != null)
                echoSearch.Dispose();
            domain.Dispose();
            if (ownsWorld) world.Dispose();
        }
    }
}
