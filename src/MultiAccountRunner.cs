using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace TideCombat
{
    public enum MultiAccountMode { Sequential, Config }

    internal sealed class MultiAccountReport
    {
        internal string State;
        internal int Completed;
        internal int? DailyPoints;
        internal Size Size;
        internal bool Finished;
        internal bool Paused;
        internal string Failure;
        internal string Notice;
    }

    // Switches between accounts through the login screen and runs the daily task
    // (sequential mode) or the configured task list (config mode).
    internal sealed class MultiAccountRunner : IDisposable
    {
        private readonly CombatSettings settings;
        private readonly MultiAccountMode mode;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<MultiAccountReport> progress;
        private readonly IProgress<EnhanceReport> enhanceProgress;
        private readonly ManualResetEventSlim enhanceGate;
        private readonly HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> offered = new HashSet<string>(StringComparer.Ordinal);
        private string targetAccountKey;
        private int? unverifiedContinuationRoute;
        private IntPtr accountWindow;
        private uint accountProcess;
        private bool accountWindowExpected;
        private int? dailyPoints;
        private string lastChildState;
        private readonly List<string> completed = new List<string>();
        private readonly AccountStore store;
        internal string Failure { get; private set; }

        internal MultiAccountRunner(CombatSettings settings, MultiAccountMode mode, CancellationToken token,
            IProgress<MultiAccountReport> progress, IProgress<EnhanceReport> enhanceProgress = null,
            ManualResetEventSlim enhanceGate = null)
        {
            this.settings = settings;
            this.mode = mode;
            this.token = token;
            this.progress = progress;
            this.enhanceProgress = enhanceProgress;
            this.enhanceGate = enhanceGate;
            world = new WorldSession(settings, token);
            store = new AccountStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs"));
        }

        internal void Run()
        {
            token.ThrowIfCancellationRequested();
            Failure = null;
            completed.Clear();
            done.Clear();
            offered.Clear();
            targetAccountKey = null;
            unverifiedContinuationRoute = null;
            accountWindow = IntPtr.Zero;
            accountProcess = 0;
            accountWindowExpected = false;
            ResetChildProgress();
            RunLog.Info("多账号", "开始 · 模式=" + mode);
            if (mode == MultiAccountMode.Config)
                RunConfigured();
            else
                RunSequential();
        }

        private void RunSequential()
        {
            try { RunSequentialCore(); }
            catch (Exception error)
            {
                token.ThrowIfCancellationRequested();
                if (error is InputElevationRequiredException || error is OperationCanceledException) throw;
                Fail(error.Message, "顺序执行异常 · " + error.GetType().Name);
            }
        }

        private void RunSequentialCore()
        {
            var failures = new List<string>();
            Report("执行当前账号日常");
            string firstFailure = RunDaily(settings, allowRecoveredFailure: true);
            ReturnToLogin("无法返回登录界面。", expectedRoute: unverifiedContinuationRoute);
            string firstAccount = DetectCurrentAccount();
            if (firstFailure != null)
            {
                if (string.IsNullOrEmpty(firstAccount))
                    throw new InvalidOperationException("首账号日常部分失败，且未能核验账号身份，停止以免重复执行。");
                Mark(firstAccount, completedSuccessfully: false);
                failures.Add("首账号：" + firstFailure);
                Report("当前账号日常部分失败，已确认返回登录，将继续后续账号");
            }
            else if (Mark(firstAccount)) Report("已完成当前账号日常");
            while (!token.IsCancellationRequested)
            {
                string next = SelectNextAccount();
                if (next == null)
                {
                    Report("没有剩余账号");
                    break;
                }
                ResetChildProgress();
                Report("登录 " + next, "尝试登录下一个账号");
                bool loggedIn = Login();
                token.ThrowIfCancellationRequested();
                if (!loggedIn)
                {
                    Fail("账号登录超时：" + next, "登录下一个账号超时");
                    return;
                }
                CaptureAccountWindow();
                string taskFailure = RunDaily(settings, allowRecoveredFailure: true);
                if (taskFailure != null)
                {
                    Mark(next, completedSuccessfully: false);
                    failures.Add("后续账号：" + taskFailure);
                    Report("当前账号日常部分失败，将核验返回登录后继续后续账号");
                }
                else if (Mark(next))
                    Report("已完成 " + next, "已完成当前账号日常");
                ReturnToLogin("无法确认返回登录界面，剩余账号未执行。", expectedRoute: unverifiedContinuationRoute);
            }
            token.ThrowIfCancellationRequested();
            if (failures.Count > 0) Fail("多账号日常结束，有 " + failures.Count + " 个账号部分失败：" + string.Join("；", failures));
            else Finish();
        }

        private void RunConfigured()
        {
            List<AccountEntry> accounts;
            List<Tuple<AccountEntry, string>> plan;
            try
            {
                accounts = store.Load();
                plan = accounts.Where(account => account.enabled)
                    .Select(account => Tuple.Create(account, AccountTasks.Resolve(account.task))).ToList();
            }
            catch (Exception error)
            {
                token.ThrowIfCancellationRequested();
                Fail(error.Message, "读取多账号配置失败 · " + error.GetType().Name);
                return;
            }
            token.ThrowIfCancellationRequested();
            if (accounts.Count == 0)
            {
                Fail("请先在多账号配置里添加账号。");
                return;
            }
            RunLog.Info("多账号", "已启用的账号数=" + plan.Count);
            if (plan.Count == 0)
            {
                Finish("没有启用的账号");
                return;
            }
            var recoveredFailures = new List<string>();
            for (int index = 0; index < plan.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                ResetChildProgress();
                AccountEntry entry = plan[index].Item1;
                string task = plan[index].Item2;
                int accountNumber = index + 1;
                RunLog.Info("多账号", string.Format("处理账号序号 {0}/{1}", accountNumber, plan.Count));
                string operation = "核对登录界面";
                try
                {
                    targetAccountKey = entry.account_key;
                    if (index == 0)
                        ReturnToLogin("无法返回登录界面。", loginPending: true);
                    operation = "在登录列表选择账号";
                    Report("选择账号 " + entry.account_key, "选择账号序号 " + accountNumber);
                    if (!SelectTargetAccount(entry.account_key))
                        throw new InvalidOperationException("登录列表找不到账号：" + entry.account_key);
                    operation = "登录账号";
                    if (!Login())
                        throw new InvalidOperationException("账号登录超时：" + entry.account_key);
                    CaptureAccountWindow();
                    RunLog.Info("多账号", "登录成功 · 账号序号=" + accountNumber);
                    operation = "执行账号任务";
                    string childFailure = RunConfiguredTask(entry, task);
                    token.ThrowIfCancellationRequested();
                    operation = "确认账号切换";
                    ReturnToLogin("账号任务已结束，但未确认返回登录界面。", expectedRoute: unverifiedContinuationRoute);
                    if (childFailure != null)
                    {
                        SaveResult(entry, "failed", childFailure);
                        recoveredFailures.Add("账号序号 " + accountNumber + "：" + childFailure);
                        Report("当前账号任务未核验或部分失败，已安全返回登录，将继续后续账号", "已记录失败账号序号 " + accountNumber);
                        continue;
                    }
                    SaveResult(entry, "success", string.Empty);
                    completed.Add(entry.account_key);
                    Report("已完成 " + entry.account_key, "已完成账号序号 " + accountNumber);
                }
                catch (Exception error)
                {
                    if (error is OperationCanceledException || token.IsCancellationRequested)
                    {
                        SaveResult(entry, "cancelled", "任务已停止");
                        token.ThrowIfCancellationRequested();
                        throw;
                    }
                    if (error is InputElevationRequiredException) throw;
                    SaveResult(entry, "failed", error.Message);
                    Fail(error.Message, string.Format("账号序号={0} · {1}失败 · {2}", accountNumber, operation,
                        error.GetType().Name));
                    return;
                }
            }
            token.ThrowIfCancellationRequested();
            if (recoveredFailures.Count > 0)
                Fail("多账号配置执行结束，有 " + recoveredFailures.Count + " 个账号部分失败：" + string.Join("；", recoveredFailures));
            else Finish();
        }

        private void SaveResult(AccountEntry entry, string status, string message)
        {
            try { store.RecordResult(entry, status, message); }
            catch (Exception error)
            {
                if (!(error is System.IO.IOException || error is System.IO.InvalidDataException ||
                    error is UnauthorizedAccessException))
                    throw;
                RunLog.Warn("多账号", "写入账号运行记录失败 · " + error.Message);
            }
        }

        private string RunConfiguredTask(AccountEntry entry, string task)
        {
            CombatSettings runtime = AccountConfig.ForRun(settings);
            AccountConfig.Apply(runtime, entry.config, task);
            runtime.Validate();
            if (task == "DailyTask") return RunDaily(runtime, allowRecoveredFailure: true);
            return RunTaskByReference(task, runtime);
        }

        private string RunTaskByReference(string name, CombatSettings settings)
        {
            switch (name)
            {
                case null:
                    Report("登录成功，任务为 None，跳过");
                    break;
                case "DailyTask":
                    return RunDaily(settings, allowRecoveredFailure: true);
                case "FarmEchoTask":
                {
                    FarmReport last = FarmLoop.Run(settings, settings.Farm,
                        new StepProgress<FarmReport>(value => ReportChild(value.State)), token, null);
                    if (last != null && last.Failure != null)
                        throw new InvalidOperationException(last.Failure);
                    break;
                }
                case "EnhanceEchoTask":
                    RunEnhance(settings);
                    break;
                case "TacetTask":
                    return RunDomain(settings, DomainKind.Tacet);
                case "ForgeryTask":
                    return RunDomain(settings, DomainKind.Forgery);
                case "SimulationTask":
                    return RunDomain(settings, DomainKind.Simulation);
                case "NightmareNestTask":
                    settings.Nightmare.Validate();
                    using (var runner = new NightmareNestRunner(settings, settings.Nightmare, token,
                        new StepProgress<NightmareReport>(value => ReportChild(value.State))))
                    {
                        runner.Run();
                        token.ThrowIfCancellationRequested();
                        if (runner.Failed)
                            throw new InvalidOperationException(runner.Failure);
                        if (runner.CompletionIssue != null)
                        {
                            IntPtr handle;
                            uint process;
                            if (!runner.TryGetContinuationWindow(out handle, out process))
                                throw new InvalidOperationException("梦魇未核验结果未确认安全返回原账号世界。");
                            RequireUnverifiedContinuationWindow(handle, process);
                            return "梦魇扫描已正常返回，但未完成核验：" + runner.CompletionIssue;
                        }
                    }
                    break;
                case "GardenTask":
                    using (var runner = new GardenRunner(settings, token, new StepProgress<GardenReport>(value => ReportChild(value.State))))
                    {
                        runner.Run();
                        if (runner.Failed) throw new InvalidOperationException(runner.Failure);
                    }
                    break;
                case "MergeEchoTask":
                    using (var runner = new MergeRunner(settings, token,
                        new StepProgress<MergeReport>(value => ReportChild(value.State, notice: value.Notice))))
                    {
                        runner.Run();
                        if (runner.Failed) throw new InvalidOperationException(runner.Failure);
                    }
                    break;
                case "ChangeEchoTask":
                    settings.Change.Validate();
                    using (var runner = new ChangeRunner(settings, settings.Change, token,
                        new StepProgress<ChangeReport>(value => ReportChild(value.State))))
                    {
                        runner.Run();
                        if (runner.Failed) throw new InvalidOperationException(runner.Failure);
                    }
                    break;
                case "CultivationTask":
                    using (var runner = new CultivationRunner(settings, token,
                        new StepProgress<DomainReport>(report => Report(report.State))))
                        runner.Run();
                    break;
                case "RahaiBlocksTask":
                    if (settings.Source != GameSource.AutoLocal && settings.Source != GameSource.Foreground)
                        throw new InvalidOperationException("拉海洛方块需要本地客户端自动或前台模式。");
                    WaitForActivity(settings, "请打开「拉海洛方块·逆转」关卡列表，然后点击继续");
                    using (var runner = new RahaiRunner(settings, token, new StepProgress<string>(state => Report(state))))
                        runner.Run();
                    break;
                case "GalGameTask":
                    WaitForActivity(settings, "请打开「第二索拉：未名之梦」活动首页或正在进行的旅程，然后点击继续");
                    using (var runner = new SecondSolaRunner(settings, token, new StepProgress<string>(state => Report(state))))
                        runner.Run();
                    break;
                default:
                    throw new InvalidOperationException("未注册的账号任务 " + name + "。");
            }
            return null;
        }

        private void WaitForActivity(CombatSettings settings, string instructions)
        {
            if (enhanceGate == null)
                throw new InvalidOperationException("活动任务需要可继续的任务窗口。");
            enhanceGate.Reset();
            Report(instructions + "，再切回游戏", paused: true);
            enhanceGate.Wait(token);
            enhanceGate.Reset();
            Report("开始执行当前账号的活动");
            if (settings.Source == GameSource.Foreground || settings.Source == GameSource.Cloud)
            {
                using (var session = new WorldSession(settings, token))
                    if (!session.WaitUntil(() => session.Captured, 15000))
                    {
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("等待活动画面超时。");
                    }
            }
        }

        private void RunEnhance(CombatSettings settings)
        {
            settings.Enhance.Validate();
            if (enhanceGate == null || enhanceProgress == null)
                throw new InvalidOperationException("账号强化需要可继续的任务窗口。");
            using (var session = new WorldSession(settings, token))
                if (session.Tick() && session.VerifiedWorld)
                    session.Key(settings.BagKey[0]);
            Report("请为当前账号筛选声骸并按等级升序，完成后点击继续");
            enhanceGate.Reset();
            enhanceProgress.Report(new EnhanceReport
            {
                State = "请为当前账号筛选声骸并按等级升序，完成后点击继续",
                Paused = true
            });
            while (!token.IsCancellationRequested && !enhanceGate.Wait(100)) { }
            token.ThrowIfCancellationRequested();
            enhanceGate.Reset();
            EnhanceReport result = EnhanceLoop.Run(settings, settings.Enhance, enhanceProgress, token, enhanceGate,
                openEvidenceFolder: false);
            token.ThrowIfCancellationRequested();
            if (result.Failure != null || !result.Finished)
                throw new InvalidOperationException(result.Failure ?? "未确认当前账号的批量强化已经完成。");
        }

        private string RunDaily(CombatSettings settings, bool allowRecoveredFailure = false)
        {
            using (var runner = new DailyRunner(settings, settings.Daily, token,
                new StepProgress<DailyReport>(value => ReportChild(value.State, value.Points, value.Notice))))
            {
                runner.Run();
                token.ThrowIfCancellationRequested();
                if (runner.Failed && !(allowRecoveredFailure && runner.RecoverableFailuresOnly))
                    throw new InvalidOperationException(runner.Failure);
                if (runner.Failed && runner.HasNormalUnverifiedFailure)
                {
                    IntPtr handle;
                    uint process;
                    if (!runner.TryGetContinuationWindow(out handle, out process))
                        throw new InvalidOperationException("日常未核验结果未确认安全返回原账号世界：" + runner.Failure);
                    RequireUnverifiedContinuationWindow(handle, process);
                }
                return runner.Failure;
            }
        }

        private string RunDomain(CombatSettings settings, DomainKind kind)
        {
            DomainOptions options = (settings.Domain ?? DomainOptions.Defaults()).ForKind(kind);
            using (var runner = new DomainRunner(settings, options, token,
                new StepProgress<DomainReport>(value => ReportChild(value.State))))
            {
                runner.Run();
                token.ThrowIfCancellationRequested();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
                if (!runner.CompletionVerified)
                {
                    IntPtr handle;
                    uint process;
                    if (!runner.TryGetContinuationWindow(out handle, out process))
                        throw new InvalidOperationException("体力本未核验结果未确认安全返回原账号世界。");
                    RequireUnverifiedContinuationWindow(handle, process);
                    return "体力本已正常返回，但未确认当前账号消费或完成结果。";
                }
                return null;
            }
        }

        private void CaptureAccountWindow()
        {
            unverifiedContinuationRoute = null;
            accountWindow = IntPtr.Zero;
            accountProcess = 0;
            accountWindowExpected = true;
            if (world.Tick() && TaskContinuation.IsSafeWorld(world))
            {
                accountWindow = world.Window.Handle;
                accountProcess = world.Window.ProcessId;
            }
        }

        private void RequireUnverifiedContinuationWindow(IntPtr handle, uint process)
        {
            if (accountWindowExpected && !TaskContinuation.SameWindow(accountWindow, accountProcess, handle, process) ||
                !world.Tick() || !TaskContinuation.IsSafeWorld(world) ||
                !TaskContinuation.SameWindow(handle, process, world.Window.Handle, world.Window.ProcessId))
                throw new InvalidOperationException("子任务未核验且未确认同一账号游戏窗口，停止后续账号。");
            unverifiedContinuationRoute = world.InputRouteVersion;
        }

        private void ReturnToLogin(string failure, bool loginPending = false, int? expectedRoute = null)
        {
            token.ThrowIfCancellationRequested();
            bool verified = expectedRoute.HasValue ? world.Tick() && world.InputRouteVersion == expectedRoute.Value &&
                TaskContinuation.IsSafeWorld(world) : EnsureWorld(loginPending);
            bool returned = verified && SwitchToLogin(expectedRoute);
            token.ThrowIfCancellationRequested();
            if (!returned)
                throw new InvalidOperationException(failure);
        }

        private bool SwitchToLogin(int? expectedRoute = null)
        {
            Report("返回登录界面");
            if (!world.Key((char)27, route: expectedRoute) || !LoginDelay(1500, expectedRoute)) return false;
            Rectangle setting = Rectangle.Empty;
            if (!world.WaitUntil(() => RouteStillMatches(expectedRoute) && world.FindNear("esc_setting", .80, out setting), 10000)) return false;
            int route = expectedRoute.GetValueOrDefault(world.InputRouteVersion);
            if (!world.Captured || !world.ClickMenuButton(new Point((int)(world.Frame.Width * .04),
                (int)(world.Frame.Height * .96)), route: route)) return false;
            if (!LoginDelay(1000, expectedRoute)) return false;
            Rectangle confirm = Rectangle.Empty;
            if (world.WaitUntil(() => RouteStillMatches(expectedRoute) &&
                (world.FindNear("confirm_btn_hcenter_vcenter", .60, out confirm) ||
                world.FindNear("confirm_btn_highlight_hcenter_vcenter", .60, out confirm)), 10000) &&
                !world.ClickMenuButton(confirm, route: expectedRoute.GetValueOrDefault(world.InputRouteVersion)))
                return false;
            bool visible = world.WaitStable(() => RouteStillMatches(expectedRoute) && (FindCombo() != null || Dropdown() != null), 60000, 2000);
            token.ThrowIfCancellationRequested();
            return visible && RouteStillMatches(expectedRoute);
        }

        private bool RouteStillMatches(int? expectedRoute) { return !expectedRoute.HasValue || expectedRoute.Value == world.InputRouteVersion; }

        private bool LoginDelay(int milliseconds, int? expectedRoute)
        {
            if (!expectedRoute.HasValue) { world.Sleep(milliseconds); return !token.IsCancellationRequested; }
            return !token.WaitHandle.WaitOne(milliseconds) && world.Tick() && RouteStillMatches(expectedRoute);
        }

        internal static OcrLine FindDropdown(IList<OcrLine> lines, bool configured, string target)
        {
            if (!lines.Any(line => line.Text != null && LoginScreen.LoginLabel(line.Text))) return null;
            OcrLine account = null;
            foreach (OcrLine line in lines)
            {
                string text = line.Text;
                if (string.IsNullOrEmpty(text)) continue;
                bool matches = configured ? text.Contains("*") || text.Contains("@") ||
                    (!string.IsNullOrEmpty(target) && AccountKeys.FromOcr(text) == target) : text.Contains("****");
                if (!matches) continue;
                if (account != null) return null;
                account = line;
            }
            return account;
        }

        private OcrLine Dropdown()
        {
            return FindDropdown(world.Read(0, 0, 1, 1), mode == MultiAccountMode.Config, targetAccountKey);
        }

        private string DetectCurrentAccount()
        {
            LoginCombo combo = FindCombo();
            if (combo != null)
            {
                string selected = combo.Selected();
                if (!string.IsNullOrEmpty(selected)) return selected;
            }
            OcrLine row = world.Read(0, 0, 1, 1).FirstOrDefault(line => line.Text != null && line.Text.Contains("****"));
            return row == null ? null : row.Text;
        }

        private bool OpenAccountDropdown()
        {
            OcrLine dropdown = null;
            if (!world.WaitStable(() => (dropdown = Dropdown()) != null, 60000, 2000))
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("等待账号下拉框超时。");
            }
            if (!world.ClickMenuButton(dropdown.Box, route: world.InputRouteVersion)) return false;
            world.Sleep(2000);
            token.ThrowIfCancellationRequested();
            return Dropdown() == null;
        }

        private string SelectNextAccount()
        {
            token.ThrowIfCancellationRequested();
            LoginCombo combo = FindCombo();
            if (combo != null)
            {
                List<string> items = combo.Items();
                foreach (string item in items) offered.Add(AccountKeys.Normalize(item));
                for (int index = 0; index < items.Count; index++)
                {
                    if (done.Contains(AccountKeys.Normalize(items[index]))) continue;
                    if (!combo.Select(index)) throw new InvalidOperationException("原生账号列表没有切换到所选条目。");
                    return items[index];
                }
                return null;
            }
            for (int attempt = 0; attempt < 5; attempt++)
            {
                world.Sleep(1000);
                token.ThrowIfCancellationRequested();
                if (!OpenAccountDropdown()) continue;
                string selected = null;
                if (!world.WaitUntil(() =>
                {
                    OcrLine next = null;
                    foreach (OcrLine line in world.Read(0, 0, 1, 1))
                    {
                        if (line.Text == null || !line.Text.Contains("****")) continue;
                        string key = AccountKeys.Normalize(line.Text);
                        offered.Add(key);
                        if (next == null && !done.Contains(key)) next = line;
                    }
                    if (next == null || !world.ClickMenuButton(next.Box, route: world.InputRouteVersion)) return false;
                    selected = next.Text;
                    world.Sleep(2000);
                    return true;
                }, 10000))
                {
                    token.ThrowIfCancellationRequested();
                    if (offered.Count > 0 && offered.All(done.Contains)) return null;
                    throw new InvalidOperationException("登录账号列表未识别到有效条目。");
                }
                world.Sleep(1000);
                if (SameAccount(selected, DetectCurrentAccount())) return selected;
            }
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("五次尝试后仍无法确认已选中的登录账号。");
        }

        internal static OcrLine FindTargetRow(IEnumerable<OcrLine> lines, string target)
        {
            return lines.Where(line => AccountKeys.FromOcr(line.Text) == target)
                .OrderByDescending(line => 2L * line.Box.Y + line.Box.Height).FirstOrDefault();
        }

        private bool SelectTargetAccount(string key)
        {
            targetAccountKey = key;
            world.Sleep(1000);
            token.ThrowIfCancellationRequested();
            try
            {
                LoginCombo combo = FindCombo();
                if (combo != null)
                {
                    int index = AccountKeys.FindSelection(combo.Items(), key);
                    if (index >= 0 && combo.Select(index))
                    {
                        world.Sleep(1000);
                        token.ThrowIfCancellationRequested();
                        if (AccountKeys.FromOcr(combo.Selected()) == key) return true;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (InputElevationRequiredException) { throw; }
            catch (Exception error) { RunLog.Warn("多账号", "原生账号列表不可用，改从画面确认 · " + error.GetType().Name); }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                world.Sleep(1000);
                token.ThrowIfCancellationRequested();
                if (!OpenAccountDropdown()) continue;
                if (!world.WaitUntil(() =>
                {
                    OcrLine row = FindTargetRow(world.Read(0, 0, 1, 1), key);
                    if (row == null || !world.ClickMenuButton(row.Box, route: world.InputRouteVersion)) return false;
                    world.Sleep(2000);
                    return true;
                }, 10000))
                {
                    token.ThrowIfCancellationRequested();
                    return false;
                }
                world.Sleep(1000);
                token.ThrowIfCancellationRequested();
                OcrLine current = Dropdown();
                if (current != null && AccountKeys.FromOcr(current.Text) == key) return true;
            }
            return false;
        }

        private bool Login()
        {
            bool previous = world.Input.HoverLoginControls;
            try
            {
                world.Input.HoverLoginControls = mode == MultiAccountMode.Config;
                world.Sleep(4000);
                token.ThrowIfCancellationRequested();
                var lines = world.Read(0, 0, 1, 1);
                if (!world.Captured) return false;
                Point button = LoginScreen.AccountLoginPoint(new Size(world.Frame.Width, world.Frame.Height), lines);
                if (!world.ClickMenuButton(button, route: world.InputRouteVersion))
                {
                    token.ThrowIfCancellationRequested();
                    return false;
                }
                world.Sleep(3000);
                return EnsureWorld(loginPending: true);
            }
            finally { world.Input.HoverLoginControls = previous; }
        }

        private LoginCombo FindCombo()
        {
            token.ThrowIfCancellationRequested();
            return LoginCombo.Find(world.Window.Handle, world.Window.ProcessId);
        }

        private bool Mark(string account, bool completedSuccessfully = true)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(account) || !done.Add(AccountKeys.Normalize(account)))
                return false;
            if (completedSuccessfully) completed.Add(account);
            return true;
        }

        private static bool SameAccount(string left, string right)
        {
            return string.Equals(AccountKeys.Normalize(left), AccountKeys.Normalize(right), StringComparison.Ordinal);
        }

        private bool EnsureWorld(bool loginPending = false)
        {
            bool ready = new WorldRecovery(world, message => Report(message)).EnsureMain(loginPending ? 180000 : 100000, loginPending);
            token.ThrowIfCancellationRequested();
            return ready;
        }

        private void ResetChildProgress()
        {
            lastChildState = null;
            dailyPoints = null;
        }

        private void ReportChild(string state, int? points = null, string notice = null)
        {
            if (string.IsNullOrEmpty(state) || notice == null && state == lastChildState && (!points.HasValue || points == dailyPoints)) return;
            lastChildState = state;
            if (points.HasValue) dailyPoints = points;
            Report(state, notice: notice);
        }

        private void Report(string state, string logMessage = null, bool paused = false, string notice = null)
        {
            RunLog.Info("多账号", (logMessage ?? state) + " · 已完成账号数=" + completed.Count);
            progress.Report(new MultiAccountReport
            {
                State = state, Completed = completed.Count, DailyPoints = dailyPoints, Paused = paused, Notice = notice,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Finish(string state = "多账号任务完成")
        {
            token.ThrowIfCancellationRequested();
            RunLog.Info("多账号", string.Format("结束 · 已完成账号数={0} · 已取消={1}", completed.Count,
                token.IsCancellationRequested));
            progress.Report(new MultiAccountReport
            {
                State = state, Completed = completed.Count, DailyPoints = dailyPoints, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message, string logMessage = null)
        {
            Failure = message;
            RunLog.Warn("多账号", (logMessage ?? message) + " · 已完成账号数=" + completed.Count);
            progress.Report(new MultiAccountReport
            {
                State = message, Failure = message, Finished = true, Completed = completed.Count, DailyPoints = dailyPoints,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }
}
