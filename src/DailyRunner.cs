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
        // Signals an ordinary child return with incomplete evidence, never a
        // successful result. Only known child types may recover and advance.
        private sealed class UnverifiedChildResultException : InvalidOperationException
        {
            internal readonly int Route;
            internal UnverifiedChildResultException(string message, int route) : base(message) { Route = route; }
        }

        private sealed class NightmareCombatUnresolvedException : InvalidOperationException
        {
            internal NightmareCombatUnresolvedException(string message) : base(message) { }
        }

        private sealed class StepBoundaryException : InvalidOperationException
        {
            internal StepBoundaryException(string message) : base(message) { }
        }

        private const string LogArea = "一条龙";
        private readonly CombatSettings settings;
        private readonly DailyOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<DailyReport> progress;
        private readonly DailyEvidence evidence;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        internal bool RecoverableFailuresOnly { get; private set; }
        internal bool HasNormalUnverifiedFailure { get; private set; }
        private bool retryingStep, stepActionUnverified, stepResultVerified;
        private string nightmarePickupNotice;
        private int farmSettledRounds, farmKills, farmEchoes;
        private int mergeSettledBatches;

        internal bool TryGetContinuationWindow(out IntPtr handle, out uint processId)
        {
            return TaskContinuation.TryGetCurrentWindow(world, out handle, out processId);
        }
        private int usedStamina, points;
        private bool cultivationIncludesNightmare, cultivationSpentStamina;
        private bool? initialActivityPage, initialRewardReady;

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
            RecoverableFailuresOnly = false;
            HasNormalUnverifiedFailure = false;
            nightmarePickupNotice = null;
            options.Validate();
            DailyTask[] tasks = (DailyTask[])options.GetTasks().Clone();
            if (Array.IndexOf(tasks, DailyTask.Farm4C) >= 0 && (settings.Farm == null || !settings.Farm.Teleport))
                throw new InvalidOperationException("一条龙已选择刷 4C，请先在刷 4C 设置里开启传送。");
            if (Array.IndexOf(tasks, DailyTask.Nightmare) >= 0 && options.GetNightmareMode() == DailyNightmareMode.All)
                CreateNightmareOptions(settings.Nightmare, DailyNightmareMode.All);
            RunLog.Info(LogArea, "任务计划 · " + string.Join(" → ", tasks.Select(DailyPlan.Label)));
            string unselected = string.Join("、", Enum.GetValues(typeof(DailyTask)).Cast<DailyTask>()
                .Except(tasks).Select(DailyPlan.Label));
            if (!string.IsNullOrEmpty(unselected))
                RunLog.Info(LogArea, "未选择项目 · " + unselected);
            token.ThrowIfCancellationRequested();
            EnsureInitialScreen(tasks[0], tasks.Contains(DailyTask.DailyRewards));
            if (options.IsReferencePlan)
            {
                // OKWW snapshots both prerequisites once. A successful tap on
                // nightmare rewards must not change whether stamina was needed
                // at the start of the daily task.
                bool activityPage;
                bool rewardReady = ReadDailyProgress(out activityPage, continueToDomain: true, keepGuidebook: true);
                initialActivityPage = activityPage;
                initialRewardReady = rewardReady;
                if (!activityPage)
                    throw new InvalidOperationException("未确认日常活跃度页面，不能安全判断体力消耗或按需梦魇。");
                tasks = options.GetReferenceRunTasks(rewardReady, usedStamina);
                RunLog.Info(LogArea, "初始进度决定的实际项目 · " + string.Join(" → ", tasks.Select(DailyPlan.Label)));
            }

            DailyPlan.RunOrderedTasks(tasks, token, (task, index) =>
            {
                RunStep(task, index + 1, tasks.Length);
                if (options.Tasks == null && options.Farm == DomainKind.Cultivation && task == DailyTask.Stamina)
                    DailyPlan.OrderCultivationRewardsAndNightmare(tasks, cultivationSpentStamina);
            }, index =>
            {
                if (index + 1 < tasks.Length)
                    RunLog.Warn(LogArea, "本次未执行的已选项目 · " +
                        string.Join("、", tasks.Skip(index + 1).Select(DailyPlan.Label)));
            });
            string final = nightmarePickupNotice == null ? "一条龙完成" : "一条龙完成 · 部分梦魇声骸拾取未核实";
            Report(final);
            progress.Report(new DailyReport
            {
                State = final, UsedStamina = usedStamina, Points = points, Finished = true, Notice = nightmarePickupNotice,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void RunStep(DailyTask task, int index, int count)
        {
            string step = string.Format("{0}/{1} {2}", index, count, DailyPlan.Label(task));
            Report("开始 " + step);
            var watch = Stopwatch.StartNew();
            try
            {
                token.ThrowIfCancellationRequested();
                retryingStep = stepActionUnverified = stepResultVerified = false;
                if (task == DailyTask.Farm4C) farmSettledRounds = farmKills = farmEchoes = 0;
                if (task == DailyTask.Merge) mergeSettledBatches = 0;
                PixelFrame before = ReadEvidenceFrame();
                IntPtr game = world.Window.Handle;
                uint process = world.Window.ProcessId;
                evidence.Begin(task, index, before);
                string completion = null, skipReason = null;
                DailyPlan.ExecuteWithOneRetry(retry =>
                {
                    retryingStep = retry;
                    if (!retry && before.Width <= 0)
                        throw new InvalidOperationException("未捕获项目起始游戏画面。");
                    if (!retry || !stepResultVerified)
                        skipReason = ExecuteStep(task, out completion);
                    else if (skipReason == null && completion == null)
                        completion = "已确认项目结果和结束画面";
                    if (stepActionUnverified)
                        throw new InvalidOperationException("项目结束前仍有操作结果待核实。");
                    stepResultVerified = true;
                    token.ThrowIfCancellationRequested();
                    PixelFrame end = VerifyStepEnd(task, game, process);
                    if (end.Width <= 0)
                        throw new StepBoundaryException("项目结果已核对，但未确认项目结束后的游戏画面。");
                    evidence.Save(skipReason == null ? "after-confirmed" : "after-skipped", end);
                }, error => PrepareStepRetry(task, step, game, process, error));
                string duration = options.IsReferencePlan ? "" : " · 耗时=" + watch.ElapsedMilliseconds + "ms";
                string status = skipReason == null ? "完成 " : "跳过 ";
                string detail = skipReason ?? completion;
                Report(status + step + (detail == null ? "" : " · " + detail) + duration);
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
                var unverified = error as UnverifiedChildResultException;
                HasNormalUnverifiedFailure = unverified != null;
                RecoverableFailuresOnly = !stepActionUnverified && !stepResultVerified &&
                    TaskContinuation.IsSafeWorld(world) && (unverified == null || unverified.Route == world.InputRouteVersion) &&
                    DailyPlan.CanAttemptRecovery(task, options.IsReferencePlan, options.Farm == DomainKind.Cultivation,
                        HasNormalUnverifiedFailure, nightmareCombatUnresolved: IsUnresolvedCombat(error));
                RunLog.Error(LogArea, "失败并停止 " + step + duration, error);
                throw new InvalidOperationException(DailyPlan.Label(task) + "失败：" + error.Message, error);
            }
        }

        private bool IsSameStepWindow(IntPtr game, uint process)
        {
            return TaskContinuation.SameWindow(game, process, world.Window.Handle, world.Window.ProcessId);
        }

        private PixelFrame VerifyStepEnd(DailyTask task, IntPtr game, uint process)
        {
            int stable = 0;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 5000 && !token.IsCancellationRequested)
            {
                bool captured = world.Tick();
                if (captured && !IsSameStepWindow(game, process)) break;
                stable = captured && IsStepEndScreen(task) ? stable + 1 : 0;
                if (stable >= 2) return world.Frame;
                token.WaitHandle.WaitOne(120);
            }
            return default(PixelFrame);
        }

        private bool IsStepEndScreen(DailyTask task)
        {
            FarmUi ui = world.Ui;
            if (ui == null || ui.Death || ui.Claim || ui.Challenge || ui.MonthlyCard ||
                ui.SkipDialog || ui.Teleport || !ui.ConfirmBox.IsEmpty)
                return false;
            if (TaskContinuation.IsSafeWorld(world)) return true;
            if (world.InRealm)
                return task == DailyTask.Farm4C && world.VerifiedWorld && world.TeamHud &&
                    !world.Hud.Locked && !world.Hud.EnemyHealth && !world.Hud.Dead;
            if (world.Terminal) return task == DailyTask.BattlePass;
            if (world.TeamHud) return false;
            return task == DailyTask.Nightmare && GuidebookVisible() ||
                task == DailyTask.Garden && world.Vision.IsGuidebookQuestSelected(world.Frame);
        }

        private bool PrepareStepRetry(DailyTask task, string step, IntPtr game, uint process, InvalidOperationException error)
        {
            if (token.IsCancellationRequested)
                return false;
            if (stepActionUnverified)
            {
                RunLog.Warn(LogArea, "已提交操作的结果仍待核实，停止重跑 " + step);
                return false;
            }
            PixelFrame frame = ReadEvidenceFrame();
            evidence.Save(stepResultVerified ? "after-first-exit-unverified" : "after-first-unfinished", frame);
            if (IsUnresolvedCombat(error))
            {
                RunLog.Warn(LogArea, "战斗结果未核实，保留当前挑战画面 " + step);
                return false;
            }
            if (frame.Width <= 0 || !IsSameStepWindow(game, process) ||
                !(stepResultVerified ? RestoreVerifiedStepEnd(task) : RestoreRetryScreen(task)))
            {
                RunLog.Warn(LogArea, "未识别到可安全重试的原游戏画面 " + step);
                return false;
            }
            frame = ReadEvidenceFrame();
            if (frame.Width <= 0 || !IsSameStepWindow(game, process) || token.IsCancellationRequested)
                return false;
            evidence.Save("after-first-before-retry", frame);
            Report((stepResultVerified ? "项目结果已确认，重试结束界面核验 " :
                "项目未完成，重试一次 ") + step + " · " + error.Message);
            return true;
        }

        private static bool IsUnresolvedCombat(Exception error)
        {
            return error is NightmareCombatUnresolvedException || error is UnresolvedCombatException;
        }

        private bool RestoreVerifiedStepEnd(DailyTask task)
        {
            if (IsStepEndScreen(task) || TryFinishRealmExit(task)) return true;
            return world.Ui != null && !world.InRealm && !world.Ui.Claim && !world.Ui.Death &&
                !world.Ui.Challenge && world.Ui.ConfirmBox.IsEmpty && !world.TeamHud &&
                (world.Terminal || GuidebookVisible() || task == DailyTask.Mail && ReadMailHeading(world.Frame) ||
                    task == DailyTask.BattlePass && ReadBattlePass().Page != BattlePassPage.Unknown) &&
                EnsureWorld();
        }

        private bool TryFinishRealmExit(DailyTask task)
        {
            if (task != DailyTask.Nightmare && task != DailyTask.Stamina &&
                task != DailyTask.Farm4C && task != DailyTask.Garden)
                return false;
            int route = world.InputRouteVersion;
            Func<Rectangle> exit = () => FarmVision.ReadRealmExitDialog(world.Frame, world.Ocr).Exit;
            return !exit().IsEmpty &&
                world.ClickMenuTransition(route, exit, () => TaskContinuation.IsSafeWorld(world), 6000, 500);
        }

        private bool RestoreRetryScreen(DailyTask task)
        {
            if (world.Ui == null || world.Ui.Death || world.TeamHud && (world.Hud.Locked || world.Hud.EnemyHealth))
                return false;
            if (TaskContinuation.IsSafeWorld(world) || task == DailyTask.MonthlyCard && world.Ui.MonthlyCard)
                return true;
            if (task == DailyTask.Merge && mergeSettledBatches > 0 && MergeRunner.CanResumeSweep(world))
                return true;
            if (TryFinishRealmExit(task)) return true;
            if (!world.InRealm && !world.Ui.Claim && world.Ui.ConfirmBox.IsEmpty)
            {
                if (task == DailyTask.Mail && ReadMailHeading(world.Frame)) return true;
                if (task == DailyTask.BattlePass)
                {
                    BattlePassReadout page = ReadBattlePass();
                    if (page.Page != BattlePassPage.Unknown && page.Receipt.IsEmpty) return true;
                }
                if (task != DailyTask.Farm4C && task != DailyTask.Mail && task != DailyTask.BattlePass &&
                    CanKeepInitialScreen(task)) return true;
            }
            if (world.InRealm && world.VerifiedWorld && world.TeamHud &&
                (task == DailyTask.Stamina || task == DailyTask.Farm4C || task == DailyTask.Nightmare))
                return EnsureWorld();
            return TryResumeAfterFailure(task);
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

        private void EnsureInitialScreen(DailyTask firstTask, bool includesDailyRewards)
        {
            if (world.Tick())
            {
                if (includesDailyRewards && !world.TeamHud)
                {
                    int route = world.InputRouteVersion;
                    Func<Rectangle> findReceipt = () => DailyClaimVision.ReadObtainedReceiptDismissal(world.Frame, world.Ocr);
                    if (!findReceipt().IsEmpty)
                    {
                        RunLog.Info(LogArea, "启动时检测到已有的获得回执，确认关闭后重新读取任务状态");
                        world.ClickMenuTransition(route, findReceipt, () =>
                            (IsDailyActivityPage(world.Frame) || world.VerifiedWorld || world.Terminal) &&
                            findReceipt().IsEmpty, 2200);
                    }
                }
                if (world.VerifiedWorld && !world.InRealm ||
                    !options.IsReferencePlan && options.Tasks != null && CanKeepInitialScreen(firstTask))
                {
                    if (options.IsReferencePlan) world.Sleep(500);
                    return;
                }
            }

            var recovery = new WorldRecovery(world, Report);
            bool ready = recovery.EnsureMain(180000, loginPending: true);
            token.ThrowIfCancellationRequested();
            if (!ready)
                throw new InvalidOperationException("一条龙开始前未确认进入游戏世界。");
        }

        private bool CanKeepInitialScreen(DailyTask task)
        {
            if (task == DailyTask.Nightmare && world.VerifiedWorld && world.InRealm)
                return true;
            if (world.InRealm)
                return false;
            switch (task)
            {
                case DailyTask.Mail:
                    return world.Terminal || ReadMailHeading(world.Frame);
                case DailyTask.BattlePass:
                    return world.Terminal || ReadBattlePass().Page != BattlePassPage.Unknown;
                case DailyTask.DailyRewards:
                case DailyTask.Stamina:
                case DailyTask.Nightmare:
                case DailyTask.Garden:
                    return world.Terminal || GuidebookVisible();
                case DailyTask.MonthlyCard:
                    return world.Ui != null && world.Ui.MonthlyCard;
                default:
                    return false;
            }
        }

        private bool TryResumeAfterFailure(DailyTask task)
        {
            if (world.WaitUntil(CanResumeAfterFailure, 5000))
                return RecoverKnownTaskWorld();
            if ((task != DailyTask.Garden && task != DailyTask.Merge) || !world.Tick() ||
                world.InRealm || world.TeamHud)
                return false;

            int route = world.InputRouteVersion;
            string page = task == DailyTask.Garden ? "garden_start_game" : "echo_select_all";
            Rectangle ignored;
            if (!world.FindNear(page, .82, out ignored) || !world.Key((char)0x1B, route: route))
                return false;
            return world.WaitUntil(() => world.InputRouteVersion == route && CanResumeAfterFailure(), 5000) &&
                RecoverKnownTaskWorld();
        }

        private bool RecoverKnownTaskWorld()
        {
            return options.IsReferencePlan ? new WorldRecovery(world, Report).EnsureMain(180000, loginPending: false) &&
                world.VerifiedWorld && !world.InRealm : EnsureWorld();
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
                case DailyTask.BattlePass: return ClaimBattlePass(out completion);
                case DailyTask.Garden: return RunGarden(out completion);
                case DailyTask.Merge:
                    if (!options.IsReferencePlan && !retryingStep) RequireWorld();
                    return RunMerge(out completion);
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
                bool activityPage, rewardReady;
                if (!retryingStep && options.Tasks == null && initialActivityPage.HasValue)
                {
                    activityPage = initialActivityPage.Value;
                    rewardReady = initialRewardReady.GetValueOrDefault();
                }
                else rewardReady = ReadDailyProgress(out activityPage);
                if (rewardReady)
                    return "日常积分已满";
                if (!activityPage)
                    throw new InvalidOperationException("未确认日常积分，无法判断是否需要补捡声骸。");
            }
            NightmareOptions nightmare = CreateNightmareOptions(settings.Nightmare, mode);
            token.ThrowIfCancellationRequested();
            using (var runner = new NightmareNestRunner(settings, nightmare, token,
                new StepProgress<NightmareReport>(), RecordEvidence, world))
            {
                try { runner.Run(fromDailyReference: options.IsReferencePlan); }
                finally
                {
                    if (nightmare.CaptureOnce)
                    {
                        stepResultVerified = runner.CaptureConfirmed;
                        stepActionUnverified = runner.HasUnverifiedPickup && !runner.CaptureConfirmed;
                    }
                    else
                    {
                        stepResultVerified = runner.ScanResultVerified;
                        if (!string.IsNullOrEmpty(runner.PickupIssue))
                        {
                            string notice = "梦魇巢穴：" + runner.PickupIssue;
                            RunLog.Warn(LogArea, notice);
                            if (nightmarePickupNotice == null)
                                nightmarePickupNotice = notice;
                            else if (!string.Equals(nightmarePickupNotice, notice, StringComparison.Ordinal))
                                nightmarePickupNotice += "；复核时：" + runner.PickupIssue;
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                if (runner.CombatUnresolved)
                    throw new NightmareCombatUnresolvedException(runner.Failure ?? runner.CompletionIssue ??
                        "未确认梦魇巢穴战斗结果，停止后续任务。");
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (runner.CompletionIssue != null)
                    throw new UnverifiedChildResultException("梦魇扫描正常返回，但未完成核验：" + runner.CompletionIssue,
                        world.InputRouteVersion);
                completion = nightmare.CaptureOnce ? "已确认捕获声骸" : "已核对所选巢穴清理结果";
                if (!nightmare.CaptureOnce && nightmarePickupNotice != null)
                    completion += "，部分声骸拾取未核实";
            }
            return null;
        }

        internal static NightmareOptions CreateNightmareOptions(NightmareOptions configured, DailyNightmareMode mode)
        {
            configured = configured ?? NightmareOptions.Defaults();
            bool captureOnce = mode != DailyNightmareMode.All;
            if (!captureOnce && !configured.TacetNest && !configured.Purification)
                throw new System.IO.InvalidDataException("全部梦魇巢穴至少选择一种巢穴类型。");
            bool useBoth = captureOnce && !configured.TacetNest && !configured.Purification;
            var nightmare = new NightmareOptions
            {
                TacetNest = configured.TacetNest || useBoth,
                Purification = configured.Purification || useBoth,
                CaptureOnce = captureOnce
            };
            nightmare.Validate();
            return nightmare;
        }

        internal static bool ShouldCheckNightmareProgress(DailyOptions options)
        {
            return options.Farm != DomainKind.Tacet || !options.GetTasks().Contains(DailyTask.Stamina);
        }

        private string RunStamina(out string completion)
        {
            completion = null;
            bool activityPage, rewardReady;
            if (!retryingStep && options.IsReferencePlan && initialActivityPage.HasValue)
            {
                activityPage = initialActivityPage.Value;
                rewardReady = initialRewardReady.GetValueOrDefault();
            }
            else rewardReady = ReadDailyProgress(out activityPage, continueToDomain: true);
            token.ThrowIfCancellationRequested();
            if (options.Tasks == null && options.Farm == DomainKind.Cultivation)
            {
                initialActivityPage = activityPage;
                initialRewardReady = rewardReady;
            }
            if (!DailyPlan.NeedsStamina(rewardReady, usedStamina))
                return rewardReady ? "日常积分已满" : "今日已消耗 180 体力";
            if (!activityPage)
                throw new InvalidOperationException("未确认日常活跃度页面，无法核验剩余体力本额度。");

            int budget = DailyPlan.StaminaGoal - usedStamina;
            if (options.Farm == DomainKind.Cultivation)
            {
                int previous = usedStamina;
                var cultivationProgress = new StepProgress<DomainReport>(report =>
                    usedStamina = previous + Math.Max(0, report.UsedStamina));
                using (var runner = new CultivationRunner(settings, token, cultivationProgress, RecordEvidence, world))
                {
                    try
                    {
                        int spent = runner.Run(budget);
                        token.ThrowIfCancellationRequested();
                        completion = "培养目标确认消耗 " + spent + " 点体力";
                    }
                    finally
                    {
                        usedStamina = Math.Max(usedStamina, previous + runner.UsedStamina);
                        cultivationSpentStamina |= usedStamina > previous;
                        cultivationIncludesNightmare |= runner.NightmareCompleted;
                        stepActionUnverified |= runner.HasUnverifiedConsumption;
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
                try { runner.Run(); }
                finally
                {
                    usedStamina = Math.Max(usedStamina, before + runner.UsedStamina);
                    stepActionUnverified |= runner.HasUnverifiedConsumption;
                    stepResultVerified |= runner.CompletionVerified;
                }
                token.ThrowIfCancellationRequested();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (!runner.CompletionVerified)
                    throw new UnverifiedChildResultException("体力本已正常返回，但未确认领奖或可用体力。",
                        world.InputRouteVersion);
                if (runner.NoStamina && usedStamina == before)
                {
                    if (options.IsReferencePlan) world.Sleep(4000);
                    return "核对画面后确认体力不足";
                }
                completion = "确认消耗 " + (usedStamina - before) + " 点体力";
            }
            if (options.IsReferencePlan) world.Sleep(4000);
            return null;
        }

        private string RunGarden(out string completion)
        {
            completion = null;
            using (var runner = new GardenRunner(settings, token, new StepProgress<GardenReport>(), RecordEvidence, world))
            {
                try { runner.Run(); }
                finally
                {
                    stepActionUnverified |= runner.HasUnverifiedConsumption;
                    stepResultVerified |= runner.CompletionVerified;
                }
                token.ThrowIfCancellationRequested();
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
            using (var runner = new MergeRunner(settings, token, new StepProgress<MergeReport>(),
                RecordEvidence, notifyIfInsufficient: false, session: world))
            {
                try { runner.Run(resumeSweep: mergeSettledBatches > 0); }
                finally
                {
                    mergeSettledBatches += runner.Batches;
                    stepActionUnverified |= runner.HasUnverifiedConsumption;
                    stepResultVerified |= runner.ResultVerified;
                }
                token.ThrowIfCancellationRequested();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (!runner.CompletionVerified)
                    throw new InvalidOperationException("未确认声骸合成结果。");
                if (runner.NeedsMoreDiscarded && mergeSettledBatches == 0)
                    return "弃置声骸不足 1000 个";
                completion = "确认合成 " + mergeSettledBatches + " 批，已核对剩余可合成数量";
            }
            return null;
        }

        private string RunFarm4C()
        {
            if (settings.Farm == null || !settings.Farm.Teleport)
                throw new InvalidOperationException("请先在刷 4C 设置里开启自动传送。");
            settings.Farm.Validate();
            int target = settings.Farm.RepeatCount;
            int remaining = Math.Max(0, target - farmSettledRounds);
            FarmReport latest = null, last = null;
            Report("开始传送并刷 4C 声骸", "开始传送并刷 4C 声骸");
            try
            {
                var remainingOptions = settings.Farm.WithRepeatCount(remaining);
                last = FarmLoop.Run(settings, remainingOptions, new StepProgress<FarmReport>(r => latest = r),
                    token, null, RecordEvidence, world.Window);
            }
            finally
            {
                FarmReport observed = last ?? latest;
                if (observed != null)
                {
                    farmSettledRounds += Math.Min(remaining, observed.SettledRounds);
                    farmKills += observed.Kills;
                    farmEchoes += observed.Echoes;
                }
                stepResultVerified = farmSettledRounds >= target;
            }
            token.ThrowIfCancellationRequested();
            if (last != null && last.Failure != null)
                throw new InvalidOperationException(last.Failure);
            if (last == null || !last.Finished || farmSettledRounds < target)
                throw new InvalidOperationException("已确认结算 " + farmSettledRounds + "/" + target + " 轮刷 4C。");
            return "已确认结算 " + farmSettledRounds + " 轮，确认击杀 " + farmKills + " 次，确认吸收 " + farmEchoes + " 个";
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

        private bool ReadDailyProgress(out bool activityPage, bool continueToDomain = false, bool keepGuidebook = false)
        {
            OpenDailyActivity();
            activityPage = world.WaitUntil(() => IsDailyActivityPage(world.Frame), 5000);
            if (activityPage)
            {
                int used, currentPoints;
                bool uncertainStamina, uncertainPoints;
                bool readStamina = DailyPlan.TryReadStamina(world.Read(.10, .10, .50, .75), out used, out uncertainStamina);
                if (!readStamina && !uncertainStamina)
                {
                    int route = world.InputRouteVersion;
                    bool paged = TryPageDailyActivity(route);
                    readStamina = DailyPlan.TryReadStamina(world.Read(.10, .10, .50, .75), out used, out uncertainStamina);
                    if (world.InputRouteVersion != route || !readStamina && !paged)
                    {
                        readStamina = false;
                        uncertainStamina = true;
                        RunLog.Warn(LogArea, "未确认日常列表翻页，无法核对波片消耗进度。");
                    }
                }
                bool readPoints = DailyPlan.TryReadPoints(world.Read(.19, .80, .30, .93), out currentPoints, out uncertainPoints);
                activityPage = world.Tick() && IsDailyActivityPage(world.Frame);
                if (activityPage && (!readStamina && uncertainStamina || !readPoints && uncertainPoints))
                {
                    // Absence after a confirmed page can be treated as zero,
                    // but conflicting/inconclusive numeric evidence cannot.
                    activityPage = false;
                    RunLog.Warn(LogArea, "日常数值有冲突或低置信候选，不以 0 代替并消耗体力。");
                }
                if (activityPage)
                {
                    RecordEvidence("daily-progress-read");
                    if (!readStamina) RunLog.Warn(LogArea, "翻页后仍缺少日常消耗计数，本次按 0 计算");
                    if (!readPoints) RunLog.Warn(LogArea, "活跃度页面缺少积分读数，本次按 0 计算");
                    usedStamina = options.IsReferencePlan && !retryingStep ? used : Math.Max(usedStamina, used);
                    points = options.IsReferencePlan ? currentPoints : Math.Max(points, currentPoints);
                    Report(readStamina && readPoints ? "日常进度读取完成" : "日常进度已按缺失项取 0");
                    bool rewardReady = DailyPlan.RewardReady(currentPoints);
                    if (!keepGuidebook && (!continueToDomain || !DailyPlan.NeedsStamina(rewardReady, usedStamina))) RequireWorld();
                    return rewardReady;
                }
            }
            RecordEvidence("daily-progress-page-unverified");
            Report("未确认日常活跃度页面");
            RequireWorld();
            return false;
        }

        private bool TryPageDailyActivity(int route)
        {
            if (!world.Tick() || world.InputRouteVersion != route || !IsDailyActivityPage(world.Frame))
                return false;
            var size = new Size(world.Frame.Width, world.Frame.Height);
            Rectangle original = FindDailyActivityScrollThumb(world.Frame);
            if (original.IsEmpty) return false;
            Rectangle track = GuidebookList.Area(size, .971, .184, .978, .806);
            int targetY = GuidebookList.PointAt(size, .974, .6).Y;
            if (targetY >= original.Top && targetY < original.Bottom)
            {
                int margin = Math.Max(8, size.Height / 40);
                targetY = original.Bottom + margin < track.Bottom ? original.Bottom + margin :
                    Math.Max(track.Top, original.Top - margin);
            }
            if (targetY >= original.Top && targetY < original.Bottom) return false;

            Func<Rectangle> currentThumb = () => world.Frame.Width == size.Width &&
                world.Frame.Height == size.Height && IsDailyActivityPage(world.Frame) ?
                FindDailyActivityScrollThumb(world.Frame) : Rectangle.Empty;
            return world.ClickMenuTransition(route, () =>
            {
                Rectangle thumb = currentThumb();
                return !thumb.IsEmpty && Math.Abs(thumb.X - original.X) <= 2 &&
                    Math.Abs(thumb.Top - original.Top) <= 3 ?
                    new Rectangle(thumb.X - 2, targetY, 5, 1) : Rectangle.Empty;
            }, () =>
            {
                Rectangle thumb = currentThumb();
                return !thumb.IsEmpty && Math.Abs(thumb.X - original.X) <= 2 &&
                    Math.Abs(thumb.Top - original.Top) >= Math.Max(4, size.Height / 180);
            }, 1500, settleMs: 1000);
        }

        internal static Rectangle FindDailyActivityScrollThumb(PixelFrame frame)
        {
            Rectangle track = GuidebookList.Area(new Size(frame.Width, frame.Height), .971, .184, .978, .806);
            return GuidebookList.FindVerticalThumb(frame, track, Math.Max(24, frame.Height / 12), 168, 70);
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
            if (options.IsReferencePlan)
            {
                // The reference reopens the F2 guide from main rather than
                // reusing a previously selected quest page.
                RequireWorld();
            }
            var guide = new GuidebookNavigation(world, Report, GuideTab.Quest, referenceSequence: options.IsReferencePlan);
            if (!guide.Open(() => new WorldRecovery(world, Report).EnsureMain(180000, loginPending: false)))
                throw new InvalidOperationException(guide.Failure ?? "未识别到任务图鉴。");
            if (world.Tick() && IsDailyActivityPage(world.Frame))
                return;

            int route = world.InputRouteVersion;
            if (!world.WaitUntil(() => world.InputRouteVersion != route || !FindDailyActivityTab().IsEmpty, 5000) ||
                world.InputRouteVersion != route)
            {
                RecordEvidence("daily-tab-unverified");
                throw new InvalidOperationException("任务图鉴已打开，但没有识别到日常页签。");
            }
            RecordEvidence("daily-tab");
            if (!world.ClickMenuTransition(route, FindDailyActivityTab, () => IsDailyActivityPage(world.Frame),
                5000, settleMs: options.IsReferencePlan ? 1000 : 0,
                firstPoint: tab => VerifiedControlPoint(tab, .17, .12)))
                throw new InvalidOperationException("日常页签已识别，但未确认打开日常活跃度页面。");
        }

        private Rectangle FindDailyActivityTab()
        {
            return world.Vision.IsGuidebookQuestSelected(world.Frame) ?
                DailyClaimVision.UniqueButton(ReadClaimRegion(world.Frame, .07, .06, .34, .21),
                    DailyClaimVision.DailyHeadings) : Rectangle.Empty;
        }

        private string ClaimMail(out string completion)
        {
            completion = null;
            if (options.IsReferencePlan) RequireWorld();
            if (!world.Tick() || !ReadMailHeading(world.Frame))
            {
                if (!world.Captured || !world.Vision.IsTerminal(world.Frame))
                {
                    RequireWorld();
                    RequireInput(world.Key((char)0x1B), "打开终端");
                    if (options.IsReferencePlan) world.Sleep(1500);
                }
                int route = world.InputRouteVersion;
                if (!world.WaitUntil(() => world.InputRouteVersion != route || !FindMailEntry().IsEmpty, 5000) ||
                    world.InputRouteVersion != route)
                {
                    RecordEvidence("mail-entry-unverified");
                    throw new InvalidOperationException("未识别到终端的邮件入口。");
                }
                RecordEvidence("mail-entry");
                if (!world.ClickMenuTransition(route, FindMailEntry, () => ReadMailHeading(world.Frame),
                    5000, settleMs: options.IsReferencePlan ? 1000 : 0,
                    firstPoint: mail => VerifiedControlPoint(mail, .64, .95)))
                    throw new InvalidOperationException("邮件入口已识别，但未确认进入邮件页面。");
            }
            string result = ClaimVisibleRewards(true, out completion);
            if (options.IsReferencePlan) world.Sleep(1000);
            return result;
        }

        private Rectangle FindMailEntry()
        {
            Rectangle entry;
            return world.Terminal && world.Vision.FindTerminalMail(world.Frame, out entry) ? entry : Rectangle.Empty;
        }

        private enum ClaimStatus { Unknown, Open, Available, TrackReward, Empty, Receipt }

        private string ClaimVisibleRewards(bool mail, out string completion)
        {
            completion = null;
            string label = mail ? "邮件附件" : "日常奖励";
            string evidenceStage = mail ? "mail" : "daily";
            Rectangle button;
            ClaimStatus before = WaitForClaimStatus(mail, false, 6000, out button);
            if (!mail && before == ClaimStatus.Receipt &&
                !DailyClaimVision.ReadObtainedReceiptDismissal(world.Frame, world.Ocr).IsEmpty)
            {
                RecordEvidence("daily-existing-receipt");
                CloseClaimReceipt(mail: false);
                before = WaitForClaimStatus(false, false, 6000, out button);
            }
            RecordEvidence(evidenceStage + "-claim-before");
            if (before == ClaimStatus.Empty)
            {
                stepResultVerified = true;
                RecordEvidence("after-" + evidenceStage + "-nothing-to-claim");
                RequireWorld();
                return label + "已领完或当前没有可领取内容";
            }
            if (before != ClaimStatus.Available && before != ClaimStatus.TrackReward)
                throw new InvalidOperationException("未确认" + label + "领取按钮或已领取状态。");

            int[] beforeButton = null;
            ClaimStatus after = before == ClaimStatus.TrackReward ? ClaimDailyTrackRewards() :
                SendClaimAndCheck(mail, button, label, out beforeButton);
            RecordEvidence(evidenceStage + "-claim-after");
            if (after == ClaimStatus.Receipt)
            {
                CloseClaimReceipt(mail);
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
                            CloseClaimReceipt(true);
                            after = WaitForClaimStatus(true, false, 4500, out pendingButton, receiptSeen: true);
                        }
                    }
                }
                else
                    after = WaitForClaimStatus(false, true, 4500, out pendingButton, button, beforeButton,
                        receiptSeen: true);
                RecordEvidence(evidenceStage + "-final-state");
            }
            if (after == ClaimStatus.TrackReward)
                after = ClaimDailyTrackRewards();
            if (after != ClaimStatus.Empty)
                throw new InvalidOperationException("无法确认" + label + "已经领完。");

            completion = "已确认" + label + "无剩余可领取内容";
            stepResultVerified = true;
            RecordEvidence("after-" + evidenceStage + "-claim-verified");
            RunLog.Info(LogArea, completion);
            RequireWorld();
            return null;
        }

        private ClaimStatus ClaimDailyTrackRewards()
        {
            int route = world.InputRouteVersion;
            int verifiedChests = 0;
            for (int claim = 0; claim <= DailyRewardTrack.ChestCount; claim++)
            {
                DailyRewardTrack track;
                if (!WaitForDailyRewardTrack(route, state => state.Pending != 0 || state.AllEligibleObserved, 4500,
                    out track))
                    throw new InvalidOperationException("无法核验活跃度宝箱和剩余可领取状态。");
                points = track.Points;
                int slot = track.FirstPending;
                if (slot < 0) return ClaimStatus.Empty;
                if ((track.Pending & verifiedChests) != 0 || claim == DailyRewardTrack.ChestCount)
                {
                    stepActionUnverified = true;
                    throw new InvalidOperationException("活跃度宝箱仍显示待领取，停止重复操作。");
                }

                Rectangle control = DailyRewardTrack.Control(world.Frame, slot);
                RecordEvidence("daily-track-reward-before");
                stepActionUnverified = true;
                bool sent = world.ClickMenuButton(control, route);
                if (!sent)
                    RunLog.Warn(LogArea, "活跃度宝箱输入未报告送达，继续核验当前画面");
                bool receipt = false;
                DailyRewardTrack cleared;
                Func<DailyRewardTrack, bool> rewardCleared = state => state.Points >= track.Points && state.IsCleared(slot);
                Func<bool> receiptVisible = () => receipt = HasClaimReceipt(world.Frame);
                bool confirmed = WaitForDailyRewardTrack(route, rewardCleared, 5000, out cleared, receiptVisible);
                bool unchanged = !confirmed && !receipt && WaitForDailyRewardTrack(route,
                    state => state.Points == track.Points && state.Observed == track.Observed &&
                        state.Pending == track.Pending, 2200, out cleared, receiptVisible);
                if (receipt && world.InputRouteVersion == route)
                {
                    RecordEvidence("daily-track-reward-receipt");
                    CloseClaimReceipt(mail: false, expectedRoute: route);
                    RecordEvidence("daily-track-reward-returned");
                    confirmed = WaitForDailyRewardTrack(route, rewardCleared, 4500, out cleared);
                }
                if (!confirmed)
                {
                    if (unchanged && world.InputRouteVersion == route)
                        stepActionUnverified = false;
                    throw new InvalidOperationException("无法核验本次活跃度宝箱领取结果。");
                }
                verifiedChests |= 1 << slot;
                stepActionUnverified = false;
                points = cleared.Points;
                RunLog.Info(LogArea, string.Format("活跃度宝箱结果已核验 · 当前活跃度={0} · 仍可领取={1}",
                    cleared.Points, cleared.Pending != 0));
                RecordEvidence("daily-track-reward-confirmed");
            }
            throw new InvalidOperationException("无法核验剩余活跃度奖励。");
        }

        private bool WaitForDailyRewardTrack(int route, Func<DailyRewardTrack, bool> accepted, int timeout,
            out DailyRewardTrack track, Func<bool> interrupt = null)
        {
            DailyRewardTrack last = default(DailyRewardTrack);
            int stable = 0;
            long observedFrame = 0;
            bool found = world.WaitUntil(() =>
            {
                if (world.InputRouteVersion != route || interrupt != null && interrupt()) return true;
                if (world.Frame.CaptureId == observedFrame) return false;
                observedFrame = world.Frame.CaptureId;
                DailyRewardTrack current;
                if (!IsDailyActivityPage(world.Frame) || !TryReadDailyRewardTrack(world.Frame, out current) ||
                    !accepted(current))
                {
                    stable = 0;
                    return false;
                }
                stable = stable > 0 && current.Points == last.Points && current.Observed == last.Observed &&
                    current.Pending == last.Pending ? stable + 1 : 1;
                last = current;
                return stable >= 2;
            }, timeout);
            track = last;
            return found && world.InputRouteVersion == route && stable >= 2;
        }

        private bool TryReadDailyRewardTrack(PixelFrame frame, out DailyRewardTrack track)
        {
            return DailyRewardTrack.TryRead(frame, ReadClaimRegion(frame, .19, .80, .30, .93),
                ReadClaimRegion(frame, .335, .89, .922, .95), out track);
        }

        private ClaimStatus SendClaimAndCheck(bool mail, Rectangle button, string label, out int[] beforeButton)
        {
            beforeButton = BattlePassVision.ButtonPatch(world.Frame, button);
            PixelFrame beforeNotice = mail ? DailyClaimVision.CaptureMailNoticeBackground(world.Frame) : default(PixelFrame);
            Size frameSize = new Size(world.Frame.Width, world.Frame.Height);
            int route = world.InputRouteVersion;
            stepActionUnverified = true;
            if (!ClickVerifiedControl(button, mail ? .14 : .93, mail ? .90 : .882, route: route))
                RunLog.Warn(LogArea, label + "领取输入未获确认，先等待原画面的回执或未变状态");
            token.ThrowIfCancellationRequested();
            long earliest = world.Now + (options.IsReferencePlan ? 1000 : 0);
            if (mail && world.WaitUntil(() => world.InputRouteVersion == route &&
                DailyClaimVision.HasMailClaimedNotice(world.Frame, beforeNotice, world.Ocr), 2200))
            {
                stepActionUnverified = false;
                RecordEvidence("mail-attachments-cleared-notice");
                WaitUntilTime(earliest);
                RunLog.Info(LogArea, "已识别「全部邮件的附件已经领取完毕」");
                return ClaimStatus.Empty;
            }
            Rectangle unused;
            ClaimStatus status = WaitForClaimStatus(mail, true, mail ? 4800 : 7000, out unused, button, beforeButton);
            if (world.InputRouteVersion != route) return ClaimStatus.Unknown;
            if (status == ClaimStatus.Receipt || status == ClaimStatus.Empty || status == ClaimStatus.TrackReward)
                stepActionUnverified = false;
            else
            {
                Rectangle pending;
                if (WaitForClaimStatus(mail, false, 2200, out pending) == ClaimStatus.Available &&
                    IsSamePendingOffer(pending, button, beforeButton, frameSize, route))
                    stepActionUnverified = false;
            }
            if (status != ClaimStatus.Receipt) WaitUntilTime(earliest);
            return status;
        }

        private bool IsSamePendingOffer(Rectangle pending, Rectangle initial, int[] initialPatch, Size frameSize, int route)
        {
            return world.InputRouteVersion == route && initialPatch.Length > 0 && pending.IntersectsWith(initial) &&
                new Size(world.Frame.Width, world.Frame.Height) == frameSize &&
                !BattlePassVision.ButtonChanged(world.Frame, initial, initialPatch);
        }

        private bool ClickVerifiedControl(Rectangle control, double referenceX, double referenceY, bool centered = false,
            int? route = null)
        {
            if (!world.Captured || control.IsEmpty) return false;
            return world.ClickMenuButton(VerifiedControlPoint(control, referenceX, referenceY, centered),
                retry: true, route: route ?? world.InputRouteVersion);
        }

        private Point VerifiedControlPoint(Rectangle control, double referenceX, double referenceY, bool centered = false)
        {
            return options.IsReferencePlan ? DailyPlan.ReferenceControlPoint(control,
                new Size(world.Frame.Width, world.Frame.Height), referenceX, referenceY, centered) :
                new Point(control.Left + control.Width / 2, control.Top + control.Height / 2);
        }

        private void WaitUntilTime(long earliest)
        {
            int remaining = (int)Math.Max(0, earliest - world.Now);
            if (remaining > 0) world.Sleep(remaining);
            token.ThrowIfCancellationRequested();
        }

        private void CloseClaimReceipt(bool mail, int? expectedRoute = null)
        {
            int route = expectedRoute ?? world.InputRouteVersion;
            Func<bool> receiptVisible = () => HasClaimReceipt(world.Frame);
            Func<Rectangle> findClose = () =>
            {
                Rectangle close = DailyClaimVision.ReadObtainedReceiptDismissal(world.Frame, world.Ocr);
                if (!close.IsEmpty) return close;
                return HasStandardClaimReceipt(world.Frame) ?
                    DailyClaimVision.UniqueButton(ReadClaimRegion(world.Frame, .23, .64, .77, .98),
                        DailyClaimVision.ReceiptCloseButtons) : Rectangle.Empty;
            };
            Func<bool> receiptClosed = () => (mail ? ReadMailHeading(world.Frame) : IsDailyActivityPage(world.Frame)) &&
                !receiptVisible();
            bool closed = !findClose().IsEmpty && world.ClickMenuTransition(route, findClose, receiptClosed, 2200);
            if (!closed && world.Tick() && world.InputRouteVersion == route)
            {
                if (receiptVisible() && !world.Key((char)0x1B, route: route))
                    RunLog.Warn(LogArea, "关闭获取回执的输入未报告送达，继续核验原领取页面");
                closed = world.WaitUntil(() => world.InputRouteVersion != route || receiptClosed(), 4500) &&
                    world.InputRouteVersion == route;
            }
            if (!closed)
                throw new InvalidOperationException("未确认获取回执关闭并返回原领取页面。");
        }

        private ClaimStatus WaitForClaimStatus(bool mail, bool afterClick, int timeout, out Rectangle action,
            Rectangle priorButton = default(Rectangle), int[] priorImage = null, bool receiptSeen = false)
        {
            ClaimStatus state = ClaimStatus.Unknown;
            Rectangle target = Rectangle.Empty;
            int stableSamples = 0;
            int route = world.InputRouteVersion;
            long observedFrame = 0;
            bool found = world.WaitUntil(() =>
            {
                if (world.InputRouteVersion != route) return true;
                if (world.Frame.CaptureId == observedFrame) return false;
                observedFrame = world.Frame.CaptureId;
                Rectangle candidate;
                ClaimStatus observed = ObserveClaimStatus(world.Frame, mail, out candidate);
                if (mail && afterClick && observed == ClaimStatus.Available &&
                    DailyClaimVision.HasMailClaimedNotice(world.Frame, default(PixelFrame), world.Ocr))
                {
                    state = ClaimStatus.Empty;
                    target = Rectangle.Empty;
                    stableSamples = 2;
                    return true;
                }
                if (observed == ClaimStatus.Unknown || observed == ClaimStatus.Open ||
                    afterClick && (observed == ClaimStatus.Available || observed == ClaimStatus.TrackReward &&
                    !receiptSeen && !BattlePassVision.ButtonChanged(world.Frame, priorButton, priorImage)) ||
                    receiptSeen && observed == ClaimStatus.Receipt)
                {
                    stableSamples = 0;
                    state = observed;
                    return false;
                }
                if (observed != state || ((observed == ClaimStatus.Available || observed == ClaimStatus.TrackReward) &&
                    !Rectangle.Inflate(target, 12, 8).IntersectsWith(candidate)))
                    stableSamples = 0;
                state = observed;
                target = candidate;
                stableSamples++;
                return stableSamples >= 2;
            }, timeout);
            bool confirmed = found && world.InputRouteVersion == route && stableSamples >= 2;
            action = confirmed ? target : Rectangle.Empty;
            return confirmed ? state : ClaimStatus.Unknown;
        }

        private ClaimStatus ObserveClaimStatus(PixelFrame frame, bool mail, out Rectangle action)
        {
            action = Rectangle.Empty;
            if (HasClaimReceipt(frame))
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
                ClaimStatus footerStatus = ClassifyClaimFooter(action, footer, DailyClaimVision.DailyClaimButtons);
                DailyRewardTrack track;
                if (footerStatus != ClaimStatus.Open || !TryReadDailyRewardTrack(frame, out track))
                    return footerStatus;
                if (track.FirstPending < 0)
                    return track.AllEligibleObserved ? ClaimStatus.Empty : ClaimStatus.Open;
                action = DailyRewardTrack.Control(frame, track.FirstPending);
                return ClaimStatus.TrackReward;
            }
        }

        private bool HasClaimReceipt(PixelFrame frame)
        {
            return HasStandardClaimReceipt(frame) || !DailyClaimVision.ReadObtainedReceiptDismissal(frame, world.Ocr).IsEmpty;
        }

        private bool HasStandardClaimReceipt(PixelFrame frame)
        {
            return DailyClaimVision.Contains(ReadClaimRegion(frame, .24, .13, .76, .43), DailyClaimVision.Receipts);
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

        private enum BattlePassEntryResult { Unconfirmed, Opened, Unavailable }

        private string ClaimBattlePass(out string completion)
        {
            completion = null;
            int entryRoute;
            BattlePassEntryResult entry = OpenBattlePass(out entryRoute);
            if (entry == BattlePassEntryResult.Unavailable)
            {
                if (!world.Tick() || world.InputRouteVersion != entryRoute)
                    throw new InvalidOperationException("确认战令状态后输入路径已变化。");
                // The original logs a missing battle pass and continues. Only
                // explicit, stable season-closed evidence warrants that skip;
                // an OCR miss or failed input remains a real failure.
                stepResultVerified = true;
                RecordEvidence("after-battle-pass-season-unavailable");
                RequireWorld();
                if (world.InputRouteVersion != entryRoute)
                    throw new InvalidOperationException("战令未开放，返回世界时输入路径已变化。");
                return "已确认先约电台本期未开放或已结束";
            }
            if (entry != BattlePassEntryResult.Opened)
            {
                RecordEvidence("battle-pass-entry-unconfirmed");
                throw new InvalidOperationException("未确认进入先约电台。");
            }
            if (!world.Tick() || world.InputRouteVersion != entryRoute)
                throw new InvalidOperationException("进入先约电台后输入路径已变化。");

            string tasks;
            try { tasks = ClaimBattlePassPage(BattlePassPage.Tasks, "tasks", "电台任务"); }
            catch (InvalidOperationException error)
            {
                token.ThrowIfCancellationRequested();
                if (stepActionUnverified || ReadBattlePass().Page == BattlePassPage.Unknown)
                    throw;
                RunLog.Warn(LogArea, "电台任务未确认，保留证据后继续核验等级奖励。");
                string rewardsAfterFailure = ClaimBattlePassPage(BattlePassPage.Rewards, "rewards", "等级奖励");
                throw new InvalidOperationException(error.Message + rewardsAfterFailure + "。", error);
            }
            string rewards = ClaimBattlePassPage(BattlePassPage.Rewards, "rewards", "等级奖励");
            stepResultVerified = true;

            RequireInput(world.Key((char)0x1B), "关闭已确认的先约电台");
            if (!world.WaitUntil(() => world.VerifiedWorld || world.Vision.IsTerminal(world.Frame), 5000))
                throw new InvalidOperationException("战令奖励状态已记录，退出电台后的界面未确认。");
            if (options.IsReferencePlan) RequireWorld();
            completion = tasks + "，" + rewards;
            return null;
        }

        private BattlePassEntryResult OpenBattlePass(out int entryRoute)
        {
            entryRoute = -1;
            if (!world.Tick())
                return BattlePassEntryResult.Unconfirmed;
            if (options.IsReferencePlan)
            {
                // Reference opens the in-world HUD directly with Alt. Retain
                // terminal recognition only as a fallback from a known screen.
                RequireWorld();
                int route = world.InputRouteVersion;
                BattlePassEntryResult result = TryOpenBattlePassHud(route);
                if (result != BattlePassEntryResult.Unconfirmed) { entryRoute = route; return result; }
                if (!world.Tick() || world.InputRouteVersion != route || !world.VerifiedWorld && !world.Terminal)
                    return BattlePassEntryResult.Unconfirmed;
            }
            if (!world.VerifiedWorld && !world.Vision.IsTerminal(world.Frame) &&
                ReadBattlePass().Page != BattlePassPage.Unknown)
            { entryRoute = world.InputRouteVersion; return BattlePassEntryResult.Opened; }

            if (world.VerifiedWorld && world.Key((char)0x1B))
                world.WaitUntil(() => world.Vision.IsTerminal(world.Frame), 4000);
            int terminalRoute = world.InputRouteVersion;
            BattlePassEntryResult terminalResult = OpenBattlePassFromTerminal(terminalRoute);
            if (terminalResult != BattlePassEntryResult.Unconfirmed) { entryRoute = terminalRoute; return terminalResult; }

            if (world.InputRouteVersion != terminalRoute || world.Window.Mode != GameSource.Background ||
                !world.TryRecoverForegroundCapture())
                return BattlePassEntryResult.Unconfirmed;
            if (!world.VerifiedWorld && !world.Vision.IsTerminal(world.Frame) &&
                ReadBattlePass().Page != BattlePassPage.Unknown)
            { entryRoute = world.InputRouteVersion; return BattlePassEntryResult.Opened; }
            if (world.VerifiedWorld && world.Key((char)0x1B, requireForeground: true))
                world.WaitUntil(() => world.Vision.IsTerminal(world.Frame), 4000);
            terminalRoute = world.InputRouteVersion;
            BattlePassEntryResult fallback = OpenBattlePassFromTerminal(terminalRoute, requireForeground: true);
            if (fallback != BattlePassEntryResult.Unconfirmed) entryRoute = terminalRoute;
            return fallback;
        }

        private BattlePassEntryResult TryOpenBattlePassHud(int route)
        {
            if (!world.Tick() || world.InputRouteVersion != route || !world.VerifiedWorld || world.InRealm)
                return BattlePassEntryResult.Unconfirmed;
            const char alt = '\u0012';
            if (!world.KeyDown(alt, route)) return BattlePassEntryResult.Unconfirmed;
            bool clicked = false;
            try
            {
                if (token.WaitHandle.WaitOne(50) || !world.Tick() || route != world.InputRouteVersion ||
                    !world.VerifiedWorld || world.InRealm) return BattlePassEntryResult.Unconfirmed;
                var reference = new Point((int)(world.Frame.Width * .86), (int)(world.Frame.Height * .05));
                clicked = world.ClickMenuButton(reference, retry: true, route: route);
            }
            finally { world.KeyUp(alt); }
            return clicked ? WaitForBattlePassEntry(route, 7000) : BattlePassEntryResult.Unconfirmed;
        }

        private BattlePassEntryResult WaitForBattlePassEntry(int route, int timeoutMs)
        {
            BattlePassEntryResult state = BattlePassEntryResult.Unconfirmed;
            long since = -1;
            bool ready = world.WaitUntil(() => world.InputRouteVersion != route ||
                ObserveBattlePassEntry(route, ref state, ref since), timeoutMs);
            return ready && world.InputRouteVersion == route ? state : BattlePassEntryResult.Unconfirmed;
        }

        private bool ObserveBattlePassEntry(int route, ref BattlePassEntryResult state, ref long since)
        {
            if (world.InputRouteVersion != route || !world.Captured) return false;
            var lines = world.Read(.00, .00, .98, .98);
            if (world.InputRouteVersion != route || !world.Captured) return false;
            var size = new Size(world.Frame.Width, world.Frame.Height);
            BattlePassEntryResult observed = !world.VerifiedWorld && !world.Terminal &&
                BattlePassVision.Inspect(lines, size).Page != BattlePassPage.Unknown ? BattlePassEntryResult.Opened :
                BattlePassVision.IsSeasonUnavailable(lines, size) ? BattlePassEntryResult.Unavailable : BattlePassEntryResult.Unconfirmed;
            if (observed == BattlePassEntryResult.Unconfirmed)
            {
                state = observed;
                since = -1;
                return false;
            }
            if (observed != state)
            {
                state = observed;
                since = world.Now;
                return false;
            }
            // Active pages settle for one second. A short unavailable banner
            // still needs repeated evidence from the same route.
            return since >= 0 && world.Now - since >= (state == BattlePassEntryResult.Opened ? 1000 : 150);
        }

        private BattlePassEntryResult OpenBattlePassFromTerminal(int route, bool requireForeground = false)
        {
            if (!world.Tick() || !world.Terminal || requireForeground && world.Window.Mode != GameSource.Foreground)
                return BattlePassEntryResult.Unconfirmed;
            RecordEvidence("battle-pass-terminal-entry");
            BattlePassEntryResult state = BattlePassEntryResult.Unconfirmed;
            long since = -1;
            return world.ClickMenuTransition(route, () =>
            {
                if (!world.Terminal || state != BattlePassEntryResult.Unconfirmed)
                    return Rectangle.Empty;
                var lines = world.Read(.30, .13, .95, .80);
                return world.Terminal ? BattlePassVision.FindTerminalEntry(lines,
                    new Size(world.Frame.Width, world.Frame.Height)) : Rectangle.Empty;
            }, () => ObserveBattlePassEntry(route, ref state, ref since), 7000) ?
                state : BattlePassEntryResult.Unconfirmed;
        }

        private string ClaimBattlePassPage(BattlePassPage page, string evidenceName, string label)
        {
            BattlePassReadout before = ReadBattlePass();
            if (before.Page != page)
            {
                int route = world.InputRouteVersion;
                bool navigated = world.ClickMenuTransition(route, () =>
                {
                    BattlePassReadout current = ReadBattlePass();
                    Point tab;
                    return current.Page != BattlePassPage.Unknown && current.Page != page && current.Receipt.IsEmpty &&
                        BattlePassVision.FindTab(world.Frame, page, out tab) ? new Rectangle(tab, new Size(1, 1)) : Rectangle.Empty;
                }, () =>
                {
                    BattlePassReadout current = ReadBattlePass();
                    return current.Page == page && current.Receipt.IsEmpty;
                }, 6000, settleMs: options.IsReferencePlan ? (page == BattlePassPage.Tasks ? 1000 : 2000) : 0);
                if (!navigated)
                {
                    RecordEvidence("battle-pass-" + evidenceName + "-navigation-unconfirmed");
                    throw new InvalidOperationException("未确认进入" + label + "页。");
                }
                before = ReadBattlePass();
            }
            string evidence = "battle-pass-" + evidenceName;
            RecordEvidence(evidence + "-before");
            if (!before.Receipt.IsEmpty)
                throw new InvalidOperationException(label + "页仍显示获取回执，未发送领取点击。");
            if (before.Page == page && before.NoPendingRewards)
            {
                RecordEvidence("after-" + evidence + "-nothing-to-claim");
                return label + "已确认无待领取";
            }
            if (before.Page != page || before.ClaimAll.IsEmpty)
            {
                RecordEvidence(evidence + "-button-unconfirmed");
                throw new InvalidOperationException(label + "页未确认领取按钮或无待领取状态。");
            }

            int[] original = BattlePassVision.ButtonPatch(world.Frame, before.ClaimAll);
            var frameSize = new Size(world.Frame.Width, world.Frame.Height);
            int claimRoute = world.InputRouteVersion;
            stepActionUnverified = true;
            if (!ClickVerifiedControl(before.ClaimAll, .68, .91, centered: true, route: claimRoute))
                RunLog.Warn(LogArea, label + "领取输入未获确认，先核对该电台页的领取结果");
            token.ThrowIfCancellationRequested();
            long earliest = world.Now + (options.IsReferencePlan ? (page == BattlePassPage.Tasks ? 3000 : 2000) : 0);
            bool receiptSeen;
            bool completed = WaitForBattlePassClaim(page, evidence, before.ClaimAll, frameSize, original, claimRoute, out receiptSeen);
            WaitUntilTime(earliest);
            if (completed)
                stepActionUnverified = false;
            else if (!receiptSeen && !token.IsCancellationRequested)
            {
                int unchanged = 0;
                world.WaitUntil(() =>
                {
                    if (world.InputRouteVersion != claimRoute) return true;
                    BattlePassReadout pending = ReadBattlePass();
                    bool sameOffer = pending.Page == page && pending.Receipt.IsEmpty && !pending.NoPendingRewards &&
                        IsSamePendingOffer(pending.ClaimAll, before.ClaimAll, original, frameSize, claimRoute);
                    unchanged = sameOffer ? unchanged + 1 : 0;
                    return unchanged >= 2;
                }, 2200);
                if (unchanged >= 2 && world.InputRouteVersion == claimRoute)
                    stepActionUnverified = false;
            }
            RecordEvidence("after-" + evidence + (completed ? "-confirmed" : "-result-unconfirmed"));
            if (!completed)
                throw new InvalidOperationException(label + "点击已投递，未确认领取结果。");
            return label + "已确认领取";
        }

        private bool WaitForBattlePassClaim(BattlePassPage page, string evidence, Rectangle button, Size frameSize,
            int[] original, int route, out bool receiptSeen)
        {
            int stable = 0;
            bool sawReceipt = false;
            bool completed = world.WaitUntil(delegate
            {
                if (world.InputRouteVersion != route) return true;
                BattlePassReadout current = ReadBattlePass();
                if (world.InputRouteVersion != route) return true;
                if (!current.Receipt.IsEmpty)
                {
                    if (!sawReceipt)
                    {
                        sawReceipt = true;
                        RecordEvidence(evidence + "-receipt");
                        if (!current.ReceiptDismiss.IsEmpty)
                            world.ClickMenuTransition(route, () =>
                            {
                                BattlePassReadout receipt = ReadBattlePass();
                                return !receipt.Receipt.IsEmpty ? receipt.ReceiptDismiss : Rectangle.Empty;
                            }, () =>
                            {
                                BattlePassReadout dismissed = ReadBattlePass();
                                return dismissed.Page == page && dismissed.Receipt.IsEmpty;
                            }, 1800);
                        else if (current.Page == BattlePassPage.Unknown)
                            world.Key((char)0x1B, route: route);
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
                    sawReceipt && actionChanged && current.ClaimAll.IsEmpty);
                stable = confirmed ? stable + 1 : 0;
                return stable >= 2;
            }, 8000);
            receiptSeen = sawReceipt;
            return completed && world.InputRouteVersion == route;
        }

        private BattlePassReadout ReadBattlePass()
        {
            if (!world.Tick() || world.Vision.IsTerminal(world.Frame) || world.VerifiedWorld)
                return new BattlePassReadout();
            var lines = world.Read(.00, .00, .98, .98);
            return BattlePassVision.Inspect(lines, new Size(world.Frame.Width, world.Frame.Height));
        }

        private void RequireWorld()
        {
            token.ThrowIfCancellationRequested();
            // Default reference calls use a shared ensure-main recovery on
            // the existing session. Ordinary return paths must not enable a
            // fresh login state machine; the explicit startup path owns it.
            bool ready = options.IsReferencePlan ? new WorldRecovery(world, Report).EnsureMain(10000, loginPending: false) &&
                world.VerifiedWorld && !world.InRealm : EnsureWorld();
            if (!ready)
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
                    return new WorldRecovery(world, Report).EnsureWorld();
                if (world.VerifiedWorld)
                    return true;
                // Reuse the existing route-verified cloud-disconnect recovery on
                // a recognized disconnect dialog, without enabling the ordinary
                // login state machine for arbitrary screens during a task.
                if ((world.Window.Mode == GameSource.Cloud || world.Window.Mode == GameSource.ManagedCloud) &&
                    !world.TeamHud && !world.Terminal && !GuidebookVisible())
                {
                    bool disconnected;
                    Rectangle exit = WorldAssist.CloudDisconnectButton(world.Read(.20, .25, .80, .75, centered: true), out disconnected);
                    if (disconnected && !exit.IsEmpty)
                    {
                        var recovery = new WorldRecovery(world, Report);
                        return recovery.EnsureMain(10000, loginPending: false) &&
                            world.VerifiedWorld && !world.InRealm;
                    }
                }
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
                Notice = nightmarePickupNotice,
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
