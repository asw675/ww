using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class AccountEditorRow
    {
        internal readonly AccountEntry Account;
        private readonly Dictionary<Tuple<string, string>, Dictionary<string, object>> drafts =
            new Dictionary<Tuple<string, string>, Dictionary<string, object>>();

        internal AccountEditorRow(AccountEntry account) { Account = account; }

        internal void SelectTask(AccountTaskReference next)
        {
            if (Account.task != null)
                drafts[Tuple.Create(Account.task.module, Account.task.@class)] = Account.config;
            Account.task = next;
            Dictionary<string, object> saved;
            Account.config = next != null && drafts.TryGetValue(Tuple.Create(next.module, next.@class), out saved)
                ? saved : new Dictionary<string, object>();
        }
    }

    internal sealed class AccountConfigForm : Form
    {
        private readonly AccountStore store;
        private readonly CombatSettings settings;
        private ListViewItem editing;
        private readonly ListView list = new ListView
        {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill,
            Width = 400, Height = 220, ShowItemToolTips = true
        };
        private readonly TextBox key = new TextBox { Width = 200, Margin = new Padding(0, 2, 8, 6) };
        private readonly ComboBox task = Theme.Select(AccountTasks.Choices);
        private readonly Label hint = Theme.Text("账号 ID 使用登录列表去掉 * 后的文本；任务 None 表示只登录。", Theme.Muted);
        private readonly Button options = Theme.Button("任务设置");
        private readonly TextBox historyDetail = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "上次运行详情", TabStop = false,
            Margin = new Padding(0, 4, 0, 4), BackColor = Theme.White, ForeColor = Theme.Ink
        };
        private Dictionary<string, object> history = new Dictionary<string, object>();

        internal AccountConfigForm(AccountStore store, CombatSettings settings)
        {
            this.store = store;
            this.settings = settings;
            Text = "多账号配置";
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(622, 480);
            MinimumSize = SizeFromClientSize(new Size(560, 420));

            list.Columns.Add("账号", 190);
            list.Columns.Add("任务", 150);
            list.Columns.Add("上次运行", 205);
            list.SelectedIndexChanged += delegate { LoadSelected(); UpdateOptions(); };
            task.SelectionChangeCommitted += delegate { UpdateSelectedTask(); UpdateOptions(); };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16, 14, 16, 10),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            Controls.Add(layout);
            layout.Controls.Add(list, 0, 0);

            var editor = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
            var keyLabel = Theme.Text("账号", Theme.Muted);
            keyLabel.Margin = new Padding(0, 6, 6, 0);
            editor.Controls.Add(keyLabel);
            editor.Controls.Add(key);
            task.Width = 130;
            editor.Controls.Add(task);
            var add = Theme.Button("添加");
            var update = Theme.Button("更新");
            var remove = Theme.Button("删除");
            var up = Theme.Button("上移");
            var down = Theme.Button("下移");
            add.Click += delegate
            {
                string newKey = editing == null ? key.Text : string.Empty;
                var reference = editing == null && task.SelectedIndex > 0 ? SelectedTask() : null;
                var item = new ListViewItem(new[] { newKey, reference == null ? "None" : reference.@class })
                {
                    Checked = true, Tag = new AccountEditorRow(new AccountEntry { task = reference, config = new Dictionary<string, object>() })
                };
                list.Items.Add(item);
                ShowHistory(item);
                item.Selected = true;
                item.EnsureVisible();
                key.Focus();
            };
            update.Click += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                ListViewItem item = list.SelectedItems[0];
                item.Text = key.Text;
                ShowHistory(item);
                UpdateSelectedTask();
                UpdateOptions();
            };
            remove.Click += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                var item = list.SelectedItems[0];
                list.Items.Remove(item);
            };
            up.Click += delegate { MoveItem(-1); };
            down.Click += delegate { MoveItem(1); };
            editor.Controls.Add(add);
            editor.Controls.Add(update);
            editor.Controls.Add(remove);
            editor.Controls.Add(up);
            editor.Controls.Add(down);
            layout.Controls.Add(editor, 0, 1);
            hint.AutoSize = false;
            hint.Height = 30;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 2);
            layout.Controls.Add(historyDetail, 0, 3);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true,
                Padding = new Padding(16, 0, 16, 12)
            };
            var cancel = Theme.Button("取消");
            var save = Theme.Button("保存并开始", true);
            var saveOnly = Theme.Button("仅保存");
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            save.Click += delegate { SaveAccounts(true); };
            saveOnly.Click += delegate { SaveAccounts(false); };
            options.Click += delegate { EditOptions(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(save);
            actions.Controls.Add(saveOnly);
            actions.Controls.Add(options);
            Controls.Add(actions);

            try { history = store.LoadResults(); }
            catch (Exception error) { hint.Text = error.Message; hint.ForeColor = Theme.Warning; }
            try { Populate(store.Load()); }
            catch (Exception error) { hint.Text = error.Message; hint.ForeColor = Theme.Warning; }
            UpdateOptions();
        }

        private void Populate(List<AccountEntry> accounts)
        {
            list.Items.Clear();
            foreach (AccountEntry account in accounts)
            {
                string taskName = account.task == null ? "None" : ChoiceIndex(account.task) > 0
                    ? account.task.@class : "不可用 · " + account.task.module + "." + account.task.@class;
                var item = new ListViewItem(new[] { account.account_key, taskName })
                {
                    Checked = account.enabled, Tag = new AccountEditorRow(account)
                };
                list.Items.Add(item);
                ShowHistory(item);
            }
        }

        private void ShowHistory(ListViewItem item)
        {
            string summary, detail;
            HistoryText(history, item.Text, out summary, out detail);
            if (item.SubItems.Count == 2) item.SubItems.Add(summary);
            else item.SubItems[2].Text = summary;
            item.ToolTipText = detail;
            if (item == editing) historyDetail.Text = detail;
        }

        internal static void HistoryText(Dictionary<string, object> history, string key, out string summary, out string detail)
        {
            object value;
            if (!history.TryGetValue(key, out value))
            {
                summary = "未运行";
                detail = "该账号尚无运行记录";
                return;
            }
            var record = (Dictionary<string, object>)value;
            string status = (string)record["status"];
            string label = status == "success" ? "成功" : status == "cancelled" ? "已停止" : "失败";
            DateTimeOffset finished;
            string time = DateTimeOffset.TryParse(record["finished_at"] as string, out finished)
                ? finished.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "时间未知";
            summary = label + " · " + time;
            object message;
            detail = record.TryGetValue("message", out message) && !string.IsNullOrEmpty(message as string)
                ? summary + Environment.NewLine + message : summary;
        }

        private void MoveItem(int delta)
        {
            if (list.SelectedItems.Count == 0)
                return;
            ListViewItem item = list.SelectedItems[0];
            int index = item.Index;
            int target = index + delta;
            if (target < 0 || target >= list.Items.Count)
                return;
            list.Items.RemoveAt(index);
            list.Items.Insert(target, item);
            item.Selected = true;
            item.Focused = true;
        }

        private void LoadSelected()
        {
            if (editing != null)
            {
                editing.Text = key.Text;
                ShowHistory(editing);
            }
            editing = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0];
            key.Text = editing == null ? string.Empty : editing.Text;
            task.SelectedIndex = editing == null ? 0 : ChoiceIndex(((AccountEditorRow)editing.Tag).Account.task);
            historyDetail.Text = editing == null ? string.Empty : editing.ToolTipText;
        }

        internal bool StartRequested { get; private set; }

        internal static int ChoiceIndex(AccountTaskReference reference)
        {
            if (reference == null) return 0;
            int index = Array.IndexOf(AccountTasks.Choices, reference.@class);
            return index > 0 && reference.module == "src.task." + reference.@class ? index : -1;
        }

        private void UpdateSelectedTask()
        {
            if (list.SelectedItems.Count == 0 || task.SelectedIndex < 0) return;
            var item = list.SelectedItems[0];
            var row = (AccountEditorRow)item.Tag;
            if (task.SelectedIndex == ChoiceIndex(row.Account.task)) return;
            row.SelectTask(SelectedTask());
            item.SubItems[1].Text = task.SelectedItem.ToString();
        }

        private AccountTaskReference SelectedTask()
        {
            string name = task.SelectedItem.ToString();
            return name == "None" ? null : new AccountTaskReference { module = "src.task." + name, @class = name };
        }

        private void SaveAccounts(bool start)
        {
            var accounts = new List<AccountEntry>();
            foreach (ListViewItem item in list.Items)
            {
                string keyText = item.Selected ? key.Text : item.Text;
                var saved = ((AccountEditorRow)item.Tag).Account;
                accounts.Add(new AccountEntry
                {
                    account_key = keyText,
                    enabled = item.Checked,
                    task = saved.task,
                    config = saved.task == null ? new Dictionary<string, object>() : saved.config
                });
            }
            try
            {
                foreach (AccountEntry account in accounts)
                    if (account.enabled)
                        AccountTasks.Resolve(account.task);
                store.Save(accounts);
                StartRequested = start;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception error)
            {
                hint.Text = error.Message;
                hint.ForeColor = Theme.Warning;
            }
        }

        private static bool HasOptions(string name)
        {
            switch (name)
            {
                case "DailyTask": case "FarmEchoTask": case "EnhanceEchoTask": case "TacetTask":
                case "ForgeryTask": case "SimulationTask": case "NightmareNestTask":
                case "CultivationTask": case "ChangeEchoTask": return true;
                default: return false;
            }
        }

        private void UpdateOptions()
        {
            options.Enabled = list.SelectedItems.Count == 1 && task.SelectedIndex > 0 && HasOptions(task.SelectedItem.ToString());
        }

        private void EditOptions()
        {
            if (!options.Enabled) return;
            var account = ((AccountEditorRow)list.SelectedItems[0].Tag).Account;
            try
            {
                string name = AccountTasks.Resolve(account.task);
                CombatSettings local = AccountConfig.ForRun(settings);
                AccountConfig.Apply(local, account.config, name);
                switch (name)
                {
                    case "DailyTask":
                        SaveDialog(new DailyForm(local.Daily, true), f => f.Result, account, "Daily");
                        break;
                    case "FarmEchoTask":
                        SaveDialog(new FarmForm(local.Farm, true), f => f.Result, account, "Farm");
                        break;
                    case "EnhanceEchoTask":
                        SaveDialog(new EnhanceForm(local.Enhance, true), f => f.Result, account, "Enhance");
                        break;
                    case "ChangeEchoTask":
                        SaveDialog(new ChangeForm(local.Change, true), f => f.Result, account, "Change");
                        break;
                    case "NightmareNestTask":
                        SaveDialog(new NightmareForm(local.Nightmare, true), f => f.Result, account, "Nightmare");
                        break;
                    case "CultivationTask":
                        SaveDialog(new CultivationForm(local.Cultivation, true), f => f.Result, account, "Cultivation");
                        break;
                    case "TacetTask": case "ForgeryTask": case "SimulationTask":
                        DomainKind kind = name == "TacetTask" ? DomainKind.Tacet :
                            name == "ForgeryTask" ? DomainKind.Forgery : DomainKind.Simulation;
                        DomainOptions domain = local.Domain.ForKind(kind);
                        SaveDialog(new DomainForm(domain, true), f =>
                        {
                            f.Result.MaxStamina = domain.MaxStamina;
                            return f.Result;
                        }, account, "Domain");
                        break;
                }
            }
            catch (Exception error)
            {
                hint.Text = error.Message;
                hint.ForeColor = Theme.Warning;
            }
        }

        private void SaveDialog<T>(T form, Func<T, object> result, AccountEntry account, string section) where T : Form
        {
            using (form)
                if (form.ShowDialog(this) == DialogResult.OK)
                    AccountConfig.WriteOptions(account.config, section, result(form));
        }
    }
}
