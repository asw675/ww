using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class AccountConfigForm : Form
    {
        private static readonly string[] TaskClasses =
        {
            "None", "DailyTask", "FarmEchoTask", "EnhanceEchoTask", "TacetTask", "ForgeryTask",
            "SimulationTask", "NightmareNestTask", "GardenTask", "MergeEchoTask", "ChangeEchoTask"
        };

        private readonly AccountStore store;
        private readonly ListView list = new ListView
        {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill,
            Width = 400, Height = 220
        };
        private readonly TextBox key = new TextBox { Width = 200, Margin = new Padding(0, 2, 8, 6) };
        private readonly ComboBox task = Theme.Select(TaskClasses);
        private readonly Label hint = Theme.Text("账号 ID 使用登录列表去掉 * 后的文本；任务 None 表示只登录。", Theme.Muted);

        internal AccountConfigForm(AccountStore store)
        {
            this.store = store;
            Text = "多账号配置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(470, 430);

            list.Columns.Add("账号", 190);
            list.Columns.Add("任务", 150);
            list.SelectedIndexChanged += delegate { LoadSelected(); };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 14, 16, 10),
                BackColor = Theme.Background
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            layout.Controls.Add(list, 0, 0);

            var editor = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
            var keyLabel = Theme.Text("账号", Theme.Muted);
            keyLabel.Margin = new Padding(0, 6, 6, 0);
            editor.Controls.Add(keyLabel);
            editor.Controls.Add(key);
            task.Width = 130;
            editor.Controls.Add(task);
            var add = Theme.Button("添加");
            var update = Theme.Button("更新");
            var remove = Theme.Button("删除");
            add.Click += delegate { list.Items.Add(new ListViewItem(new[] { key.Text, task.SelectedItem.ToString() }) { Checked = true }); };
            update.Click += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                list.SelectedItems[0].Text = key.Text;
                list.SelectedItems[0].SubItems[1].Text = task.SelectedItem.ToString();
            };
            remove.Click += delegate { if (list.SelectedItems.Count > 0) list.Items.Remove(list.SelectedItems[0]); };
            editor.Controls.Add(add);
            editor.Controls.Add(update);
            editor.Controls.Add(remove);
            layout.Controls.Add(editor, 0, 1);
            hint.AutoSize = false;
            hint.Height = 30;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 2);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true,
                Padding = new Padding(16, 0, 16, 12)
            };
            var cancel = Theme.Button("取消");
            var save = Theme.Button("保存并开始", true);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            save.Click += delegate { SaveAccounts(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(save);
            Controls.Add(actions);

            try { Populate(store.Load()); }
            catch (Exception error) { hint.Text = error.Message; hint.ForeColor = Theme.Warning; }
        }

        private void Populate(List<AccountEntry> accounts)
        {
            list.Items.Clear();
            foreach (AccountEntry account in accounts)
            {
                string taskName = account.task == null ? "None" : account.task.@class;
                list.Items.Add(new ListViewItem(new[] { account.account_key, taskName }) { Checked = account.enabled });
            }
        }

        private void LoadSelected()
        {
            if (list.SelectedItems.Count == 0)
                return;
            key.Text = list.SelectedItems[0].Text;
            string value = list.SelectedItems[0].SubItems[1].Text;
            int index = Array.IndexOf(TaskClasses, value);
            task.SelectedIndex = index >= 0 ? index : 0;
        }

        private void SaveAccounts()
        {
            var existing = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            try
            {
                foreach (AccountEntry account in store.Load())
                    existing[account.account_key] = account.config;
            }
            catch (Exception) { }
            var accounts = new List<AccountEntry>();
            foreach (ListViewItem item in list.Items)
            {
                string keyText = item.Text.Trim();
                string taskName = item.SubItems[1].Text;
                Dictionary<string, object> config;
                if (!existing.TryGetValue(keyText, out config) || config == null)
                    config = new Dictionary<string, object>();
                accounts.Add(new AccountEntry
                {
                    account_key = keyText,
                    enabled = item.Checked,
                    task = taskName == "None" ? null : new AccountTaskReference { module = "TideCombat", @class = taskName },
                    config = config
                });
            }
            try
            {
                store.Save(accounts);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception error)
            {
                hint.Text = error.Message;
                hint.ForeColor = Theme.Warning;
            }
        }
    }
}
