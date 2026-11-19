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
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Switches between accounts through the login screen and runs the daily task
    // (sequential mode) or the configured task list (config mode).
    internal sealed class MultiAccountRunner : IDisposable
    {
        private static readonly string[] LoginTexts = { "登录", "登入", "Log", "Click to Connect", "点击连接" };
        private readonly CombatSettings settings;
        private readonly MultiAccountMode mode;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<MultiAccountReport> progress;
        private readonly HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> completed = new List<string>();
        private readonly AccountStore store;
        private AccountEntry current;

        internal MultiAccountRunner(CombatSettings settings, MultiAccountMode mode, CancellationToken token,
            IProgress<MultiAccountReport> progress)
        {
            this.settings = settings;
            this.mode = mode;
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
            store = new AccountStore(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs"));
        }

        internal void Run()
        {
            if (!EnsureWorld())
            {
                Fail("无法回到游戏世界。");
                return;
            }
            if (mode == MultiAccountMode.Config)
                RunConfigured();
            else
                RunSequential();
        }

        private void RunSequential()
        {
            try { RunSequentialCore(); }
            catch (InputElevationRequiredException) { throw; }
            catch (Exception error) { Fail(error.Message); }
        }

        private void RunSequentialCore()
        {
            Report("执行当前账号日常");
            RunDaily();
            EnsureWorld();
            if (!SwitchToLogin())
            {
                Fail("无法返回登录界面。");
                return;
            }
            Mark(DetectCurrentAccount());
            while (!token.IsCancellationRequested)
            {
                string next = SelectNextAccount();
                if (next == null)
                {
                    Report("没有剩余账号");
                    break;
                }
                Report("登录 " + next);
                if (!Login())
                {
                    Fail("账号登录超时：" + next);
                    return;
                }
                RunDaily();
                Mark(next);
                EnsureWorld();
                if (!SwitchToLogin())
                    break;
            }
            Finish();
        }

        private void RunConfigured()
        {
            List<AccountEntry> accounts;
            try { accounts = store.Load(); }
            catch (Exception error)
            {
                Fail(error.Message);
                return;
            }
            if (accounts.Count == 0)
            {
                Fail("请先在多账号配置里添加账号。");
                return;
            }
            List<AccountEntry> plan = accounts.Where(account => account.enabled).ToList();
            if (plan.Count == 0)
            {
                Report("没有启用的账号");
                Finish();
                return;
            }
            for (int index = 0; index < plan.Count; index++)
            {
                AccountEntry entry = plan[index];
                current = entry;
                try
                {
                    EnsureWorld();
                    if (index == 0 && !SwitchToLogin())
                        throw new InvalidOperationException("无法返回登录界面。");
                    if (!SelectTargetAccount(entry.account_key))
                        throw new InvalidOperationException("登录列表找不到账号：" + entry.account_key);
                    if (!Login())
                        throw new InvalidOperationException("账号登录超时：" + entry.account_key);
                    RunConfiguredTask(entry);
                    EnsureWorld();
                    store.RecordResult(entry, "success", string.Empty);
                    completed.Add(entry.account_key);
                    Report("已完成 " + entry.account_key);
                    if (index < plan.Count - 1)
                        SwitchToLogin();
                }
                catch (Exception error)
                {
                    try { store.RecordResult(entry, "failed", error.Message); } catch (Exception) { }
                    Fail(error.Message);
                    return;
                }
            }
            Finish();
        }

        private void RunConfiguredTask(AccountEntry entry)
        {
            AccountSnapshot snapshot = AccountConfig.Snapshot(settings);
            try
            {
                AccountConfig.Apply(settings, entry.config);
                RunTaskByReference(entry.task);
            }
            finally
            {
                AccountConfig.Restore(settings, snapshot);
            }
        }

        private void RunTaskByReference(AccountTaskReference reference)
        {
            string name = reference == null ? null : reference.@class;
            switch (name)
            {
                case null:
                    Report("登录成功，任务为 None，跳过");
                    break;
                case "DailyTask":
                    RunDaily();
                    break;
                case "FarmEchoTask":
                {
                    FarmReport last = null;
                    FarmLoop.Run(settings, settings.Farm, new Progress<FarmReport>(delegate (FarmReport report)
                    {
                        last = report;
                    }), token, null);
                    if (last != null && last.Failure != null)
                        throw new InvalidOperationException(last.Failure);
                    break;
                }
                case "EnhanceEchoTask":
                {
                    EnhanceReport last = null;
                    EnhanceLoop.Run(settings, settings.Enhance, new Progress<EnhanceReport>(
                        delegate (EnhanceReport report) { last = report; }), token);
                    if (last != null && last.Failure != null)
                        throw new InvalidOperationException(last.Failure);
                    break;
                }
                case "TacetTask":
                    RunDomain(DomainKind.Tacet);
                    break;
                case "ForgeryTask":
                    RunDomain(DomainKind.Forgery);
                    break;
                case "SimulationTask":
                    RunDomain(DomainKind.Simulation);
                    break;
                case "NightmareNestTask":
                    using (var runner = new NightmareNestRunner(settings, settings.Nightmare, token,
                        new Progress<NightmareReport>(delegate { })))
                    {
                        runner.Run();
                        if (runner.Failed) throw new InvalidOperationException(runner.Failure);
                    }
                    break;
                case "GardenTask":
                    using (var runner = new GardenRunner(settings, token, new Progress<GardenReport>(delegate { })))
                    {
                        runner.Run();
                        if (runner.Failed) throw new InvalidOperationException(runner.Failure);
                    }
                    break;
                case "MergeEchoTask":
                    using (var runner = new MergeRunner(settings, token, new Progress<MergeReport>(delegate { })))
                    {
                        runner.Run();
                        if (runner.Failed) throw new InvalidOperationException(runner.Failure);
                    }
                    break;
                case "ChangeEchoTask":
                    using (var runner = new ChangeRunner(settings, settings.Change, token,
                        new Progress<ChangeReport>(delegate { })))
                    {
                        runner.Run();
                        if (runner.Failed) throw new InvalidOperationException(runner.Failure);
                    }
                    break;
                default:
                    Report("暂不支持的任务类型：" + name);
                    break;
            }
        }

        private void RunDaily()
        {
            using (var runner = new DailyRunner(settings, settings.Daily, token,
                new Progress<DailyReport>(delegate { })))
            {
                runner.Run();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
            }
        }

        private void RunDomain(DomainKind kind)
        {
            DomainOptions options = settings.Domain ?? DomainOptions.Defaults();
            options.Kind = kind;
            using (var runner = new DomainRunner(settings, options, token,
                new Progress<DomainReport>(delegate { })))
            {
                runner.Run();
                if (runner.Failed)
                    throw new InvalidOperationException(runner.Failure);
            }
        }

        private bool SwitchToLogin()
        {
            Report("返回登录界面");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                world.Key((char)0x1B);
                world.Sleep(1500);
                if (world.WaitFind("esc_setting", 3000))
                    break;
            }
            world.Click(0.04, 0.96);
            world.Sleep(1200);
            Rectangle confirm;
            if (world.FindNear("confirm_btn_hcenter_vcenter", .80, out confirm))
                world.Click(confirm);
            return world.WaitUntil(HasLogin, 60000);
        }

        private bool HasLogin()
        {
            if (FindCombo() != null)
                return true;
            var lines = world.Read(0, 0, 1, 1);
            bool account = false, login = false;
            foreach (OcrLine line in lines)
            {
                string text = line.Text ?? string.Empty;
                if (text.Contains("*"))
                    account = true;
                if (LoginTexts.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0))
                    login = true;
            }
            return account && login;
        }

        private string DetectCurrentAccount()
        {
            LoginCombo combo = FindCombo();
            if (combo != null)
            {
                string selected = combo.Selected();
                if (!string.IsNullOrEmpty(selected))
                    return selected;
            }
            var lines = world.Read(0, 0, 1, 1);
            foreach (OcrLine line in lines)
            {
                string key = AccountKeys.FromOcr(line.Text);
                if (key != null && line.Text.Contains("*"))
                    return key;
            }
            return null;
        }

        private string SelectNextAccount()
        {
            LoginCombo combo = FindCombo();
            if (combo != null)
            {
                List<string> items = combo.Items();
                for (int index = 0; index < items.Count; index++)
                {
                    string name = AccountKeys.FromOcr(items[index]);
                    if (IsDone(name))
                        continue;
                    if (!combo.Select(index))
                        continue;
                    return items[index];
                }
                return null;
            }
            OpenAccountDropdown();
            var lines = world.Read(0, 0, 1, 1);
            foreach (OcrLine line in lines)
            {
                if (line.Text == null || !line.Text.Contains("*"))
                    continue;
                string name = AccountKeys.FromOcr(line.Text);
                if (IsDone(name))
                    continue;
                world.Click(line.Box);
                world.Sleep(2000);
                return name;
            }
            return null;
        }

        private bool SelectTargetAccount(string key)
        {
            LoginCombo combo = FindCombo();
            if (combo != null)
            {
                List<string> items = combo.Items();
                int match = -1;
                for (int index = 0; index < items.Count; index++)
                    if (SameAccount(AccountKeys.FromOcr(items[index]), key))
                        match = index;
                if (match < 0)
                    return false;
                if (!combo.Select(match))
                    return false;
                world.Sleep(1000);
                return SameAccount(AccountKeys.FromOcr(combo.Selected()), key);
            }
            OpenAccountDropdown();
            var lines = world.Read(0, 0, 1, 1);
            foreach (OcrLine line in lines)
            {
                string name = AccountKeys.FromOcr(line.Text);
                if (!SameAccount(name, key))
                    continue;
                world.Click(line.Box);
                world.Sleep(2000);
                return true;
            }
            return false;
        }

        private void OpenAccountDropdown()
        {
            var lines = world.Read(0, 0, 1, 1);
            OcrLine account = null;
            bool login = false;
            int count = 0;
            foreach (OcrLine line in lines)
            {
                string text = line.Text ?? string.Empty;
                if (text.Contains("*"))
                {
                    count++;
                    if (account == null)
                        account = line;
                }
                if (LoginTexts.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0))
                    login = true;
            }
            if (account == null || count != 1 || !login)
                return;
            world.Click(account.Box);
            world.Sleep(1500);
        }

        private bool Login()
        {
            world.Sleep(4000);
            var lines = world.Read(0, 0, 1, 1);
            OcrLine button = null;
            foreach (OcrLine line in lines)
            {
                string text = line.Text ?? string.Empty;
                if (LoginTexts.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    button = line;
                    break;
                }
            }
            if (button != null)
                world.Click(button.Box);
            else
                world.Click(0.5, 0.568);
            world.Sleep(3000);
            return world.WaitUntil(delegate { return world.TeamHud; }, 180000);
        }

        private LoginCombo FindCombo()
        {
            try { return LoginCombo.Find(world.Window.Handle, world.Window.ProcessId); }
            catch (Exception) { return null; }
        }

        private bool IsDone(string account)
        {
            return done.Contains(AccountKeys.Normalize(account) ?? string.Empty);
        }

        private void Mark(string account)
        {
            string key = AccountKeys.Normalize(account);
            if (!string.IsNullOrEmpty(key))
                done.Add(key);
        }

        private static bool SameAccount(string left, string right)
        {
            return string.Equals(AccountKeys.Normalize(left), AccountKeys.Normalize(right), StringComparison.Ordinal);
        }

        private bool EnsureWorld()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!world.Tick())
                    return false;
                if (world.TeamHud)
                    return true;
                if (world.Ui != null && world.Ui.InRealm)
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
                world.Key((char)0x1B);
                world.Sleep(800);
            }
            return world.TeamHud;
        }

        private void Report(string state)
        {
            progress.Report(new MultiAccountReport
            {
                State = state, Completed = completed.Count,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Finish()
        {
            progress.Report(new MultiAccountReport
            {
                State = "多账号任务完成", Completed = completed.Count, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            progress.Report(new MultiAccountReport
            {
                State = message, Failure = message, Finished = true, Completed = completed.Count,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }
}
