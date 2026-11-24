using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace TideCombat
{
    internal sealed class DailyReport
    {
        internal string State;
        internal int UsedStamina;
        internal int Points;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
        internal string Notice;
    }

    internal sealed class DailyRunner : IDisposable
    {
        private const string LogArea = "一条龙";
        private readonly CombatSettings settings;
        private readonly DailyOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<DailyReport> progress;
        private readonly DailyEvidence evidence;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        private int usedStamina, points;
        private bool cultivationIncludesNightmare, claimedTacetStamina;
        private bool? initialRewardReady;

        internal DailyRunner(CombatSettings settings, DailyOptions options, CancellationToken token,
            IProgress<DailyReport> progress)
        {
            this.settings = settings;
            this.options = options;
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
            evidence = new DailyEvidence(RunLog.DirectoryPath);
        }

        internal void Run()
        {
            try { RunCore(); }
            catch (InputElevationRequiredException) { throw; }
            catch (Exception error)
            {
                if (token.IsCancellationRequested)
                {
                    RunLog.Info(LogArea, "任务已取消");
                    return;
                }
                RunLog.Error(LogArea, "日常任务异常", error);
                Fail(error.Message);
            }
        }

        private void RunCore()
        {
            options.Validate();
            DailyTask[] tasks = (DailyTask[])options.GetTasks().Clone();
            if (Array.IndexOf(tasks, DailyTask.Farm4C) >= 0 && (settings.Farm == null || !settings.Farm.Teleport))
                throw new InvalidOperationException("一条龙已选择刷 4C，请先在刷 4C 设置里开启传送。");
            if (Array.IndexOf(tasks, DailyTask.Nightmare) >= 0 && options.GetNightmareMode() == DailyNightmareMode.All)
                (settings.Nightmare ?? NightmareOptions.Defaults()).Validate();
            RunLog.Info(LogArea, "任务计划 · " + string.Join(" → ", tasks.Select(DailyPlan.Label)));
            string unselected = string.Join("、", Enum.GetValues(typeof(DailyTask)).Cast<DailyTask>()
                .Except(tasks).Select(DailyPlan.Label));
            if (!string.IsNullOrEmpty(unselected))
                RunLog.Info(LogArea, "未选择项目 · " + unselected);
            var failed = new List<string>();
            for (int index = 0; index < tasks.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string reason;
                    if (!RunStep(tasks[index], index + 1, tasks.Length, out reason))
                        failed.Add(DailyPlan.Label(tasks[index]) + "（" + reason + "）");
                    if (options.Tasks == null && tasks[index] == DailyTask.Stamina)
                        DailyPlan.OrderRewardsAndNightmare(tasks, claimedTacetStamina);
                }
                catch (InvalidOperationException)
                {
                    if (index + 1 < tasks.Length)
                        RunLog.Warn(LogArea, "本次未执行的已选项目 · " +
                            string.Join("、", tasks.Skip(index + 1).Select(DailyPlan.Label)));
                    throw;
                }
            }
            token.ThrowIfCancellationRequested();
            if (failed.Count > 0)
            {
                Fail("一条龙结束，失败项目：" + string.Join("、", failed) + "。");
                return;
            }
            const string final = "一条龙完成";
            Report(final);
            progress.Report(new DailyReport
            {
                State = final, UsedStamina = usedStamina, Points = points, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private bool RunStep(DailyTask task, int index, int count, out string failureReason)
        {
            failureReason = null;
            string step = string.Format("{0}/{1} {2}", index, count, DailyPlan.Label(task));
            Report("开始 " + step);
            var watch = Stopwatch.StartNew();
            try
            {
                string before = evidence.Begin(task, index, ReadEvidenceFrame());
                string completion;
                string skipReason = ExecuteStep(task, out completion);
                token.ThrowIfCancellationRequested();
                string after = evidence.Save(before == null ? "after-missing-before" :
                    skipReason == null ? "after-confirmed" : "after-skipped", ReadEvidenceFrame());
                if (before == null || after == null)
                    RunLog.Warn(LogArea, "本步骤的起止截图未完整保存，游戏内结果见本步记录。");
                string duration = " · 耗时=" + watch.ElapsedMilliseconds + "ms";
                string status = skipReason == null ? "完成 " : "跳过 ";
                string detail = skipReason ?? completion;
                Report(status + step + (detail == null ? "" : " · " + detail) + duration);
                return true;
            }
            catch (Exception error)
            {
                if (token.IsCancellationRequested)
                    RecordEvidence("last-frame-before-cancel");
                else
                    evidence.Save("after-failed", ReadEvidenceFrame());
                string duration = " · 耗时=" + watch.ElapsedMilliseconds + "ms";
                if (token.IsCancellationRequested)
                {
                    RunLog.Info(LogArea, "取消 " + step + duration);
                    token.ThrowIfCancellationRequested();
                }
                if (error is InputElevationRequiredException)
                {
                    RunLog.Warn(LogArea, "中断 " + step + " · 输入权限不足" + duration);
                    throw;
                }
                bool critical = (task == DailyTask.Stamina && options.Farm != DomainKind.Cultivation) ||
                    task == DailyTask.Garden || task == DailyTask.Farm4C;
                if (!critical && index < count)
                {
                    RunLog.Warn(LogArea, "项目未确认，检查能否安全继续后续任务");
                    critical = !world.WaitUntil(CanResumeAfterFailure, 5000);
                    token.ThrowIfCancellationRequested();
                    RunLog.Info(LogArea, critical ? "未确认可继续的游戏界面" : "已确认游戏界面，将继续后续任务");
                }
                RunLog.Error(LogArea, "失败 " + step + " · 是否中止=" + critical + duration, error);
                if (critical)
                    throw new InvalidOperationException(DailyPlan.Label(task) + "失败：" + error.Message, error);
                Report("失败 " + step + " · " + error.Message);
                failureReason = error.Message;
                return false;
            }
        }

        private PixelFrame ReadEvidenceFrame()
        {
            try
            {
                if (!token.IsCancellationRequested && world.Tick())
                    return world.Frame;
            }
            catch (InvalidOperationException error)
            {
                RunLog.Error(LogArea, "读取截图画面失败", error);
            }
            catch (InputElevationRequiredException)
            {
                RunLog.Warn(LogArea, "无法为此步骤读取已验证的游戏画面");
            }
            return default(PixelFrame);
        }

        private void RecordEvidence(string stage)
        {
            evidence.Save(stage, world.Captured ? world.Frame : default(PixelFrame));
        }

        private void RecordEvidence(string stage, PixelFrame frame)
        {
            evidence.Save(stage, frame);
        }

        private bool CanResumeAfterFailure()
        {
            if (world.InRealm || world.TeamHud && (world.Hud.Locked || world.Hud.EnemyHealth))
                return false;
            if (world.VerifiedWorld || world.Vision.IsTerminal(world.Frame))
                return true;
            return GuidebookVisible();
        }

        private string ExecuteStep(DailyTask task, out string completion)
        {
            completion = null;
            switch (task)
            {
                case DailyTask.Nightmare: return RunNightmare(out completion);
                case DailyTask.Stamina: return RunStamina(out completion);
                case DailyTask.DailyRewards: return ClaimDaily(out completion);
                case DailyTask.Mail: return ClaimMail(out completion);
                case DailyTask.BattlePass: completion = ClaimBattlePass(); break;
                case DailyTask.Garden: return RunGarden(out completion);
                case DailyTask.Merge: RequireWorld(); return RunMerge(out completion);
                case DailyTask.Farm4C: RequireWorld(); completion = RunFarm4C(); break;
                case DailyTask.MonthlyCard: return ClaimMonthlyCard(out completion);
                default: throw new InvalidOperationException("未识别的一条龙任务。");
            }
            return null;
        }

        private string RunNightmare(out string completion)
        {
            completion = null;
            DailyNightmareMode mode = options.GetNightmareMode();
            if (mode == DailyNightmareMode.AsNeeded)
            {
                if (cultivationIncludesNightmare)
                    return "本次培养目标已启用梦魇巢穴";
                if (!ShouldCheckNightmareProgress(options))
                    return "刷无音区时无需额外补日常声骸";
                bool activityPage = true;
                if (options.Tasks == null && initialRewardReady.HasValue ? initialRewardReady.Value :
                    ReadDailyProgress(out activityPage))
                    return "日常积分已满";
                if (!activityPage)
                    return "未确认日常积分，跳过按需补捡";
            }
            NightmareOptions configured = settings.Nightmare ?? NightmareOptions.Defaults();
            var nightmare = new NightmareOptions
            {
                TacetNest = mode != DailyNightmareMode.All || configured.TacetNest,
                Purification = mode != DailyNightmareMode.All || configured.Purification,
                CaptureOnce = mode != DailyNightmareMode.All
            };
            nightmare.Validate();
            using (var runner = new NightmareNestRunner(settings, nightmare, token,
                new StepProgress<NightmareReport>(), RecordEvidence))
            {
                runner.Run();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (runner.CompletionIssue != null)
                    throw new InvalidOperationException(runner.CompletionIssue);
                completion = nightmare.CaptureOnce ? "已确认捕获声骸" : "已核对所选巢穴页面和处理结果";
            }
            return null;
        }

        internal static bool ShouldCheckNightmareProgress(DailyOptions options)
        {
            return options.Farm != DomainKind.Tacet || !options.GetTasks().Contains(DailyTask.Stamina);
        }

        private string RunStamina(out string completion)
        {
            completion = null;
            bool activityPage;
            bool rewardReady = ReadDailyProgress(out activityPage, continueToDomain: true);
            if (activityPage) initialRewardReady = rewardReady;
            if (!DailyPlan.NeedsStamina(rewardReady, usedStamina))
                return rewardReady ? "日常积分已满" : "今日已消耗 180 体力";
            if (!activityPage)
                return "未确认日常活跃度页面，跳过体力消耗";

            int budget = DailyPlan.StaminaGoal - usedStamina;
            if (options.Farm == DomainKind.Cultivation)
            {
                int previous = usedStamina;
                cultivationIncludesNightmare = (settings.Cultivation ?? CultivationOptions.Defaults()).Nightmare;
                var cultivationProgress = new StepProgress<DomainReport>(report =>
                    usedStamina = previous + Math.Max(0, report.UsedStamina));
                using (var runner = new CultivationRunner(settings, token, cultivationProgress, RecordEvidence, world))
                {
                    try
                    {
                        int spent = runner.Run(budget);
                        claimedTacetStamina = spent > 0;
                        completion = "培养目标确认消耗 " + spent + " 点体力";
                    }
                    catch (Exception error)
                    {
                        if (token.IsCancellationRequested || error is InputElevationRequiredException) throw;
                        RunLog.Error(LogArea, "培养目标失败", error);
                        var recovery = new WorldRecovery(world, Report);
                        if (!recovery.EnsureMain(180000))
                            throw new InvalidOperationException("培养目标失败且未能返回大世界。", error);
                        return "培养目标失败，已返回大世界 · " + error.Message;
                    }
                }
                return null;
            }

            var domain = new DomainOptions
            {
                Kind = options.Farm,
                Index = options.Farm == DomainKind.Tacet ? options.TacetIndex :
                    options.Farm == DomainKind.Forgery ? options.ForgeryIndex : 1,
                Material = options.Material,
                MaxStamina = budget
            };
            int before = usedStamina;
            var recorder = new StepProgress<DomainReport>(report =>
                usedStamina = before + Math.Max(0, report.UsedStamina));
            using (var runner = new DomainRunner(settings, domain, token, recorder, RecordEvidence, world))
            {
                runner.Run();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (!runner.CompletionVerified)
                    throw new InvalidOperationException("未确认体力本的领奖或可用体力。");
                claimedTacetStamina = options.Farm == DomainKind.Tacet && runner.UsedStamina > 0;
                if (runner.NoStamina && usedStamina == before)
                    return "核对画面后确认体力不足";
                completion = "确认消耗 " + (usedStamina - before) + " 点体力";
            }
            return null;
        }

        private string RunGarden(out string completion)
        {
            completion = null;
            using (var runner = new GardenRunner(settings, token, new StepProgress<GardenReport>(), RecordEvidence, world))
            {
                runner.Run();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (!runner.CompletionVerified)
                    throw new InvalidOperationException("未确认周常乐园积分或奖励状态。");
                if (runner.AlreadyCompleted)
                    return "画面确认本周乐园积分已满";
                completion = "已核对本周乐园完成状态";
            }
            return null;
        }

        private string RunMerge(out string completion)
        {
            completion = null;
            int batches = 0;
            using (var runner = new MergeRunner(settings, token, new StepProgress<MergeReport>(r => batches = r.Batches),
                RecordEvidence, notifyIfInsufficient: false))
            {
                runner.Run();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (!runner.CompletionVerified)
                    throw new InvalidOperationException("未确认声骸合成结果。");
                if (runner.NeedsMoreDiscarded)
                    return "弃置声骸不足 1000 个";
                completion = "确认合成 " + batches + " 批，已核对剩余可合成数量";
            }
            return null;
        }

        private string RunFarm4C()
        {
            if (settings.Farm == null || !settings.Farm.Teleport)
                throw new InvalidOperationException("请先在刷 4C 设置里开启自动传送。");
            settings.Farm.Validate();
            Report("开始传送并刷 4C 声骸", "开始传送并刷 4C 声骸");
            FarmReport last = FarmLoop.Run(settings, settings.Farm, null, token, null, RecordEvidence);
            token.ThrowIfCancellationRequested();
            if (last != null && last.Failure != null)
                throw new InvalidOperationException(last.Failure);
            if (last == null || !last.Finished || last.Kills < settings.Farm.RepeatCount)
                throw new InvalidOperationException("未确认达到刷 4C 的设定轮数。");
            return "确认完成 " + last.Kills + " 轮，确认吸收 " + last.Echoes + " 个";
        }

        private string ClaimMonthlyCard(out string completion)
        {
            completion = null;
            if (!world.Tick() || world.Ui == null)
                throw new InvalidOperationException("没有可核验的月卡画面。");
            if (!world.Ui.MonthlyCard)
            {
                if (world.VerifiedWorld)
                    return "已核对大世界，本次未出现月卡领取弹窗";
                throw new InvalidOperationException("当前界面无法判断是否出现月卡弹窗。");
            }
            RecordEvidence("monthly-card-before");
            if (world.DismissMonthlyCard(requireTeam: true))
            {
                RecordEvidence("monthly-card-confirmed");
                completion = "已处理月卡弹窗，画面确认返回队伍";
                return null;
            }
            token.ThrowIfCancellationRequested();
            RecordEvidence("monthly-card-unconfirmed");
            throw new InvalidOperationException("已识别月卡弹窗，未确认弹窗消失并返回大世界。");
        }

        private bool ReadDailyProgress(out bool activityPage, bool continueToDomain = false)
        {
            OpenDailyActivity();
            activityPage = world.WaitUntil(() => IsDailyActivityPage(world.Frame), 5000);
            if (activityPage)
            {
                int used, currentPoints;
                bool readStamina = DailyPlan.TryReadStamina(world.Read(.10, .10, .50, .75), out used);
                if (!readStamina)
                {
                    world.Click(.974, .6);
                    world.Sleep(1000);
                    readStamina = DailyPlan.TryReadStamina(world.Read(.10, .10, .50, .75), out used);
                }
                bool readPoints = DailyPlan.TryReadPoints(world.Read(.19, .80, .30, .93), out currentPoints);
                activityPage = world.Tick() && IsDailyActivityPage(world.Frame);
                if (activityPage)
                {
                    RecordEvidence("daily-progress-read");
                    if (!readStamina) RunLog.Warn(LogArea, "翻页后仍缺少日常消耗计数，本次按 0 计算");
                    if (!readPoints) RunLog.Warn(LogArea, "活跃度页面缺少积分读数，本次按 0 计算");
                    usedStamina = Math.Max(usedStamina, used);
                    points = Math.Max(points, currentPoints);
                    Report(readStamina && readPoints ? "日常进度读取完成" : "日常进度已按缺失项取 0");
                    bool rewardReady = DailyPlan.RewardReady(currentPoints);
                    if (!continueToDomain || !DailyPlan.NeedsStamina(rewardReady, usedStamina)) RequireWorld();
                    return rewardReady;
                }
            }
            RecordEvidence("daily-progress-page-unverified");
            Report("未确认日常活跃度页面");
            RequireWorld();
            return false;
        }

        private bool GuidebookVisible()
        {
            Rectangle boss;
            bool selected;
            return world.Captured && world.Vision.InspectGuidebook(world.Frame, out boss, out selected);
        }

        private string ClaimDaily(out string completion)
        {
            OpenDailyActivity();
            return ClaimVisibleRewards(false, out completion);
        }

        private void OpenDailyActivity()
        {
            var guide = new GuidebookNavigation(world, Report, GuideTab.Quest);
            if (!guide.Open(() => new WorldRecovery(world, Report).EnsureMain(180000, loginPending: true)))
                throw new InvalidOperationException(guide.Failure ?? "未识别到任务图鉴。");
            if (world.Tick() && IsDailyActivityPage(world.Frame))
                return;

            Rectangle tab = Rectangle.Empty;
            if (!world.WaitUntil(() =>
            {
                if (!GuidebookVisible())
                    return false;
                tab = DailyClaimVision.UniqueButton(ReadClaimRegion(world.Frame, .07, .06, .34, .21),
                    DailyClaimVision.DailyHeadings);
                return !tab.IsEmpty;
            }, 5000))
            {
                RecordEvidence("daily-tab-unverified");
                throw new InvalidOperationException("任务图鉴已打开，但没有识别到日常页签。");
            }
            RecordEvidence("daily-tab");
            RequireInput(world.Click(tab), "打开已识别的日常页");
        }

        private string ClaimMail(out string completion)
        {
            completion = null;
            if (!world.Tick() || !ReadMailHeading(world.Frame))
            {
                if (!world.Captured || !world.Vision.IsTerminal(world.Frame))
                {
                    RequireWorld();
                    RequireInput(world.Key((char)0x1B), "打开终端");
                }
                Rectangle mail = Rectangle.Empty;
                if (!world.WaitUntil(() => world.Vision.FindTerminalMail(world.Frame, out mail), 5000))
                {
                    RecordEvidence("mail-entry-unverified");
                    throw new InvalidOperationException("未识别到终端的邮件入口。");
                }
                RecordEvidence("mail-entry");
                RequireInput(world.Click(mail), "打开已识别的邮件入口");
            }
            return ClaimVisibleRewards(true, out completion);
        }

        private enum ClaimStatus { Unknown, Open, Available, Empty, Receipt }

        private string ClaimVisibleRewards(bool mail, out string completion)
        {
            completion = null;
            string label = mail ? "邮件附件" : "日常奖励";
            string evidenceStage = mail ? "mail" : "daily";
            Rectangle button;
            ClaimStatus before = WaitForClaimStatus(mail, false, 6000, out button);
            RecordEvidence(evidenceStage + "-claim-before");
            if (before == ClaimStatus.Empty)
            {
                RequireWorld();
                return label + "已领完或当前没有可领取内容";
            }
            if (before != ClaimStatus.Available)
                throw new InvalidOperationException("未确认" + label + "领取按钮或已领取状态。");

            int[] beforeButton;
            ClaimStatus after = SendClaimAndCheck(mail, button, label, out beforeButton);
            RecordEvidence(evidenceStage + "-claim-after");
            if (after == ClaimStatus.Receipt)
            {
                CloseClaimReceipt();
                Rectangle pendingButton;
                if (mail)
                {
                    after = WaitForClaimStatus(true, false, 4500, out pendingButton, receiptSeen: true);
                    if (after == ClaimStatus.Available)
                    {
                        RunLog.Info(LogArea, "邮箱仍显示全部领取，复核是否还有附件");
                        after = SendClaimAndCheck(true, pendingButton, label, out beforeButton);
                        if (after == ClaimStatus.Receipt)
                        {
                            CloseClaimReceipt();
                            after = WaitForClaimStatus(true, false, 4500, out pendingButton, receiptSeen: true);
                        }
                    }
                }
                else
                    after = WaitForClaimStatus(false, true, 4500, out pendingButton, button, beforeButton,
                        receiptSeen: true);
                RecordEvidence(evidenceStage + "-final-state");
            }
            if (after != ClaimStatus.Empty)
                throw new InvalidOperationException("无法确认" + label + "已经领完。");

            completion = "已确认" + label + "无剩余可领取内容";
            RunLog.Info(LogArea, completion);
            RequireWorld();
            return null;
        }

        private ClaimStatus SendClaimAndCheck(bool mail, Rectangle button, string label, out int[] beforeButton)
        {
            beforeButton = BattlePassVision.ButtonPatch(world.Frame, button);
            PixelFrame beforeNotice = mail ? DailyClaimVision.CaptureMailNoticeBackground(world.Frame) : default(PixelFrame);
            RequireInput(world.Click(button), "领取已识别的" + label);
            if (mail && world.WaitUntil(() => DailyClaimVision.HasMailClaimedNotice(world.Frame, beforeNotice, world.Ocr), 2200))
            {
                RecordEvidence("mail-attachments-cleared-notice");
                RunLog.Info(LogArea, "已识别「全部邮件的附件已经领取完毕」");
                return ClaimStatus.Empty;
            }
            Rectangle unused;
            return WaitForClaimStatus(mail, true, mail ? 4800 : 7000, out unused, button, beforeButton);
        }

        private void CloseClaimReceipt()
        {
            var close = DailyClaimVision.UniqueButton(ReadClaimRegion(world.Frame, .23, .64, .77, .98),
                DailyClaimVision.ReceiptCloseButtons);
            RequireInput(close.IsEmpty ? world.Key((char)0x1B) : world.Click(close), "关闭已识别的获取回执");
        }

        private ClaimStatus WaitForClaimStatus(bool mail, bool afterClick, int timeout, out Rectangle action,
            Rectangle priorButton = default(Rectangle), int[] priorImage = null, bool receiptSeen = false)
        {
            ClaimStatus state = ClaimStatus.Unknown;
            Rectangle target = Rectangle.Empty;
            int stableSamples = 0;
            bool found = world.WaitUntil(() =>
            {
                Rectangle candidate;
                ClaimStatus observed = ObserveClaimStatus(world.Frame, mail, out candidate);
                bool eligibleChange = observed == ClaimStatus.Open;
                if (afterClick && eligibleChange && BattlePassVision.ButtonChanged(world.Frame, priorButton, priorImage))
                    observed = ClaimStatus.Empty;
                if (observed == ClaimStatus.Unknown || observed == ClaimStatus.Open ||
                    afterClick && observed == ClaimStatus.Available || receiptSeen && observed == ClaimStatus.Receipt)
                {
                    stableSamples = 0;
                    state = observed;
                    return false;
                }
                if (observed != state || (observed == ClaimStatus.Available &&
                    !Rectangle.Inflate(target, 12, 8).IntersectsWith(candidate)))
                    stableSamples = 0;
                state = observed;
                target = candidate;
                stableSamples++;
                return stableSamples >= 2;
            }, timeout);
            action = found ? target : Rectangle.Empty;
            return found ? state : ClaimStatus.Unknown;
        }

        private ClaimStatus ObserveClaimStatus(PixelFrame frame, bool mail, out Rectangle action)
        {
            action = Rectangle.Empty;
            if (DailyClaimVision.Contains(ReadClaimRegion(frame, .24, .13, .76, .43),
                DailyClaimVision.Receipts))
                return ClaimStatus.Receipt;

            if (mail)
            {
                if (!ReadMailHeading(frame))
                    return ClaimStatus.Unknown;
                var footer = ReadClaimRegion(frame, .03, .81, .42, .97);
                if (DailyClaimVision.Contains(ReadClaimRegion(frame, .05, .26, .82, .78),
                    DailyClaimVision.EmptyMail))
                    return ClaimStatus.Empty;
                action = DailyClaimVision.UniqueButton(footer, DailyClaimVision.MailClaimButtons);
                return ClassifyClaimFooter(action, footer, DailyClaimVision.MailClaimButtons);
            }
            else
            {
                if (!IsDailyActivityPage(frame))
                    return ClaimStatus.Unknown;
                var footer = ReadClaimRegion(frame, .63, .78, .98, .94);
                action = DailyClaimVision.UniqueButton(footer, DailyClaimVision.DailyClaimButtons);
                if (!action.IsEmpty)
                    return ClaimStatus.Available;
                // Clicking a verified claim in the Activity pane collects the currently claimable rewards on that pane.
                action = DailyClaimVision.FirstClaimRow(ReadClaimRegion(frame, .76, .17, .97, .75));
                return ClassifyClaimFooter(action, footer, DailyClaimVision.DailyClaimButtons);
            }
        }

        private static ClaimStatus ClassifyClaimFooter(Rectangle action, List<OcrLine> footer, string[] buttons)
        {
            if (!action.IsEmpty)
                return ClaimStatus.Available;
            if (DailyClaimVision.Contains(footer, buttons))
                return ClaimStatus.Unknown;
            return DailyClaimVision.Contains(footer, DailyClaimVision.DepletedClaims) ?
                ClaimStatus.Empty : ClaimStatus.Open;
        }

        private bool ReadMailHeading(PixelFrame frame)
        {
            return DailyClaimVision.Contains(ReadClaimRegion(frame, .01, .025, .32, .21),
                DailyClaimVision.MailHeadings);
        }

        private bool IsDailyActivityPage(PixelFrame frame)
        {
            if (!world.Vision.IsGuidebookQuestSelected(frame)) return false;
            if (DailyClaimVision.Contains(ReadClaimRegion(frame, .065, .77, .38, .96), DailyClaimVision.DailyHeadings))
                return true;
            Rectangle tab = DailyClaimVision.UniqueButton(ReadClaimRegion(frame, .07, .06, .34, .21),
                DailyClaimVision.DailyHeadings);
            return DomainMath.IsHighlightedBookKind(frame, tab);
        }

        private List<OcrLine> ReadClaimRegion(PixelFrame frame, double left, double top, double right, double bottom)
        {
            int x = (int)(frame.Width * left), y = (int)(frame.Height * top);
            return world.Ocr.Read(frame,
                Rectangle.FromLTRB(x, y, (int)(frame.Width * right), (int)(frame.Height * bottom)));
        }

        private string ClaimBattlePass()
        {
            if (!OpenBattlePass())
            {
                RecordEvidence("battle-pass-entry-unconfirmed");
                throw new InvalidOperationException("未确认进入先约电台。");
            }

            string tasks;
            try { tasks = ClaimBattlePassPage(BattlePassPage.Tasks, "tasks", "电台任务"); }
            catch (InvalidOperationException error)
            {
                token.ThrowIfCancellationRequested();
                if (ReadBattlePass().Page == BattlePassPage.Unknown)
                    throw;
                RunLog.Warn(LogArea, "电台任务未确认，保留证据后继续核验等级奖励。");
                string rewardsAfterFailure = ClaimBattlePassPage(BattlePassPage.Rewards, "rewards", "等级奖励");
                throw new InvalidOperationException(error.Message + rewardsAfterFailure + "。", error);
            }
            string rewards = ClaimBattlePassPage(BattlePassPage.Rewards, "rewards", "等级奖励");

            RequireInput(world.Key((char)0x1B), "关闭已确认的先约电台");
            if (!world.WaitUntil(() => world.VerifiedWorld || world.Vision.IsTerminal(world.Frame), 5000))
                throw new InvalidOperationException("战令奖励状态已记录，退出电台后的界面未确认。");
            return tasks + "，" + rewards;
        }

        private bool OpenBattlePass()
        {
            if (!world.Tick())
                return false;
            if (!world.VerifiedWorld && !world.Vision.IsTerminal(world.Frame) &&
                ReadBattlePass().Page != BattlePassPage.Unknown)
                return true;

            if (world.VerifiedWorld && world.Key((char)0x1B))
                world.WaitUntil(() => world.Vision.IsTerminal(world.Frame), 4000);
            if (ClickBattlePassEntry() && WaitForBattlePassPage(BattlePassPage.Unknown, 7000))
                return true;

            if (world.Window.Mode != GameSource.Background || !world.TryRecoverForegroundCapture())
                return false;
            if (!world.VerifiedWorld && !world.Vision.IsTerminal(world.Frame) &&
                ReadBattlePass().Page != BattlePassPage.Unknown)
                return true;
            if (world.VerifiedWorld && world.Key((char)0x1B, requireForeground: true))
                world.WaitUntil(() => world.Vision.IsTerminal(world.Frame), 4000);
            return ClickBattlePassEntry(requireForeground: true) &&
                WaitForBattlePassPage(BattlePassPage.Unknown, 7000);
        }

        private bool ClickBattlePassEntry(bool requireForeground = false)
        {
            if (!world.Tick() || !world.Vision.IsTerminal(world.Frame))
                return false;
            int route = world.InputRouteVersion;
            var lines = world.Read(.30, .13, .95, .80);
            Rectangle entry = BattlePassVision.FindTerminalEntry(lines, new Size(world.Frame.Width, world.Frame.Height));
            if (entry.IsEmpty || !world.Tick() || route != world.InputRouteVersion ||
                !world.Vision.IsTerminal(world.Frame))
                return false;

            RecordEvidence("battle-pass-terminal-entry");
            var center = new Point(entry.X + entry.Width / 2, entry.Y + entry.Height / 2);
            return world.ClickMenuButton(center, retry: true, requireForeground: requireForeground);
        }

        private string ClaimBattlePassPage(BattlePassPage page, string evidenceName, string label)
        {
            BattlePassReadout before = ReadBattlePass();
            if (before.Page != page)
            {
                Point tab;
                if (before.Page == BattlePassPage.Unknown || !BattlePassVision.FindTab(world.Frame, page, out tab) ||
                    !world.ClickMenuButton(tab, retry: true) || !WaitForBattlePassPage(page, 6000))
                {
                    RecordEvidence("battle-pass-" + evidenceName + "-navigation-unconfirmed");
                    throw new InvalidOperationException("未确认进入" + label + "页。");
                }
                before = ReadBattlePass();
            }
            string evidence = "battle-pass-" + evidenceName;
            RecordEvidence(evidence + "-before");
            if (before.Page == page && before.NoPendingRewards)
            {
                RecordEvidence(evidence + "-nothing-to-claim");
                return label + "已确认无待领取";
            }
            if (before.Page != page || before.ClaimAll.IsEmpty)
            {
                RecordEvidence(evidence + "-button-unconfirmed");
                throw new InvalidOperationException(label + "页未确认领取按钮或无待领取状态。");
            }

            int[] original = BattlePassVision.ButtonPatch(world.Frame, before.ClaimAll);
            var frameSize = new Size(world.Frame.Width, world.Frame.Height);
            RequireInput(world.Click(before.ClaimAll), "点击" + label + "一键领取");
            bool receiptSeen;
            bool completed = WaitForBattlePassClaim(page, evidence, before.ClaimAll, frameSize, original, out receiptSeen);
            if (!completed && !receiptSeen && !token.IsCancellationRequested)
            {
                BattlePassReadout retry = ReadBattlePass();
                if (retry.Page == page && !retry.ClaimAll.IsEmpty && retry.ClaimAll.IntersectsWith(before.ClaimAll) &&
                    new Size(world.Frame.Width, world.Frame.Height) == frameSize &&
                    !BattlePassVision.ButtonChanged(world.Frame, before.ClaimAll, original))
                {
                    RecordEvidence(evidence + "-retry-before");
                    RunLog.Warn(LogArea, label + "的一键领取按钮仍在原处且画面未变，重试一次。");
                    RequireInput(world.Click(retry.ClaimAll), "重试" + label + "一键领取");
                    completed = WaitForBattlePassClaim(page, evidence, before.ClaimAll, frameSize, original, out receiptSeen);
                }
            }
            RecordEvidence(evidence + (completed ? "-confirmed" : "-result-unconfirmed"));
            if (!completed)
                throw new InvalidOperationException(label + "点击已投递，未确认领取结果。");
            return label + "已确认领取";
        }

        private bool WaitForBattlePassClaim(BattlePassPage page, string evidence, Rectangle button, Size frameSize,
            int[] original, out bool receiptSeen)
        {
            int stable = 0;
            bool sawReceipt = false;
            bool completed = world.WaitUntil(delegate
            {
                BattlePassReadout current = ReadBattlePass();
                if (!current.Receipt.IsEmpty)
                {
                    if (!sawReceipt)
                    {
                        sawReceipt = true;
                        RecordEvidence(evidence + "-receipt");
                        if (!current.ReceiptDismiss.IsEmpty)
                            world.Click(current.ReceiptDismiss);
                        else if (current.Page == BattlePassPage.Unknown)
                            world.Key((char)0x1B);
                    }
                    if (current.Page == BattlePassPage.Unknown || !current.ReceiptDismiss.IsEmpty)
                    {
                        stable = 0;
                        return false;
                    }
                }
                bool actionChanged = new Size(world.Frame.Width, world.Frame.Height) == frameSize &&
                    BattlePassVision.ButtonChanged(world.Frame, button, original);
                bool confirmed = current.Page == page && (current.NoPendingRewards ||
                    actionChanged && current.ClaimAll.IsEmpty);
                stable = confirmed ? stable + 1 : 0;
                return stable >= 2;
            }, 8000);
            receiptSeen = sawReceipt;
            return completed;
        }

        private BattlePassReadout ReadBattlePass()
        {
            if (!world.Tick() || world.Vision.IsTerminal(world.Frame) || world.VerifiedWorld)
                return new BattlePassReadout();
            var lines = world.Read(.00, .00, .98, .98);
            return BattlePassVision.Inspect(lines, new Size(world.Frame.Width, world.Frame.Height));
        }

        private bool WaitForBattlePassPage(BattlePassPage target, int timeoutMs)
        {
            return world.WaitUntil(delegate
            {
                BattlePassPage current = ReadBattlePass().Page;
                return current != BattlePassPage.Unknown && (target == BattlePassPage.Unknown || current == target);
            }, timeoutMs);
        }

        private void RequireWorld()
        {
            token.ThrowIfCancellationRequested();
            if (!EnsureWorld())
                throw new InvalidOperationException("无法回到游戏世界。");
        }

        private void RequireInput(bool sent, string action)
        {
            token.ThrowIfCancellationRequested();
            if (!sent)
                throw new InvalidOperationException(action + "的输入未发出。");
        }

        private bool EnsureWorld()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (token.IsCancellationRequested)
                    return false;
                if (!world.Tick())
                    return false;
                if (world.Ui != null && world.Ui.MonthlyCard)
                {
                    if (!world.DismissMonthlyCard(requireTeam: true)) return false;
                    continue;
                }
                if (world.InRealm)
                {
                    world.Key((char)0x1B);
                    world.Sleep(800);
                    Rectangle confirm;
                    if (world.FindNear("gray_confirm_exit_button", .74, out confirm))
                    {
                        world.Click(confirm);
                        world.Sleep(1200);
                    }
                    continue;
                }
                if (world.VerifiedWorld)
                    return true;
                world.Key((char)0x1B);
                world.Sleep(800);
            }
            return world.VerifiedWorld && !world.InRealm;
        }

        private void Report(string state) { Report(state, null); }

        private void Report(string state, string notice)
        {
            RunLog.Info(LogArea, string.Format("{0} · 体力={1}/180 · 积分={2}", state, usedStamina, points));
            progress.Report(new DailyReport
            {
                State = state, UsedStamina = usedStamina, Points = points, Notice = notice,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            RunLog.Warn(LogArea, "任务停止 · " + message);
            Failed = true;
            Failure = message;
            progress.Report(new DailyReport
            {
                State = message, Failure = message, Finished = true, UsedStamina = usedStamina, Points = points,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }

    internal sealed class StepProgress<T> : IProgress<T>
    {
        private readonly Action<T> callback;

        internal StepProgress(Action<T> callback = null) { this.callback = callback; }

        public void Report(T value)
        {
            if (callback != null)
                callback(value);
        }
    }
}
