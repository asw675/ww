using System;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class EnhanceForm : Form
    {
        private readonly CheckBox bothCrit = Option("必须有双爆", true);
        private readonly CheckBox validBeforeCrit = Option("双爆出现之前必须全有效词条", true);
        private readonly NumericUpDown critTotal = Number(0, 60, 13.8m, 1);
        private readonly NumericUpDown firstCrit = Number(0, 30, 6.9m, 1);
        private readonly NumericUpDown validCount = Number(0, 5, 3, 0);
        private readonly CheckBox firstValid = Option("第一条必须为有效词条", true);
        private readonly CheckedListBox stats = new CheckedListBox
        {
            CheckOnClick = true, Width = 250, Height = 132, IntegralHeight = false,
            Margin = new Padding(0, 2, 0, 6)
        };
        private readonly CheckBox pause = Option("成功后暂停任务", true);
        private readonly ToolTip help = new ToolTip();
        private readonly Label hint = Theme.Text(
            "在背包筛选声骸并按等级升序，开启游戏内的「阶段放入」和「同时调谐」。支持简体和繁体中文。",
            Theme.Muted);

        internal EnhanceForm(EnhanceOptions options, bool saveOnly = false)
        {
            Text = "批量强化声骸";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(500, 486);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 16, 18, 12),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            Add(layout, "必须有双爆", bothCrit, 0);
            Add(layout, "双爆前必须全有效", validBeforeCrit, 1);
            Add(layout, "双爆总计 >=", critTotal, 2);
            Add(layout, "首条双爆 >=", firstCrit, 3);
            Add(layout, "满级有效词条 >=", validCount, 4);
            Add(layout, "第一条必须有效", firstValid, 5);
            Add(layout, "有效词条", stats, 6);
            Add(layout, "成功处理", pause, 7);
            help.SetToolTip(bothCrit, "剩余孔位无法凑齐暴击和暴伤时，提前弃置。");
            help.SetToolTip(validBeforeCrit, "暴击和暴伤均选为有效时，检查第一条暴击或暴伤之前的词条。");
            help.SetToolTip(critTotal, "同时出现暴击和暴伤后，检查 暴击 + 暴伤 ÷ 2。");
            help.SetToolTip(firstCrit, "只检查第一条属于有效词条的暴击或暴伤，暴伤除以 2。");
            help.SetToolTip(validCount, "现有有效词条加上剩余孔位达不到该数量时，提前弃置。");
            help.SetToolTip(firstValid, "只有一条副词条时，这一条必须在有效词条列表中。");
            help.SetToolTip(stats, "勾选要计入有效词条数量的属性。");
            help.SetToolTip(pause, "确认锁定后暂停。可在主界面点击继续。");
            hint.AutoSize = false;
            hint.Height = 48;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 8);
            layout.SetColumnSpan(hint, 2);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true,
                Margin = new Padding(0, 8, 0, 0), WrapContents = false
            };
            var cancel = Theme.Button("取消");
            var ok = Theme.Button(saveOnly ? "保存" : "开始强化", true);
            var defaults = Theme.Button("恢复默认");
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ok.Click += delegate { Commit(!saveOnly); };
            defaults.Click += delegate { LoadOptions(EnhanceOptions.Defaults()); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            if (!saveOnly)
            {
                var save = Theme.Button("仅保存");
                save.Click += delegate { Commit(false); };
                actions.Controls.Add(save);
            }
            actions.Controls.Add(defaults);
            layout.Controls.Add(actions, 0, 9);
            layout.SetColumnSpan(actions, 2);

            stats.Items.AddRange(EnhanceOptions.AllStats);
            LoadOptions(options);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal EnhanceOptions Result { get; private set; }
        internal bool StartRequested { get; private set; }

        private void LoadOptions(EnhanceOptions options)
        {
            bothCrit.Checked = options.RequireBothCrit;
            validBeforeCrit.Checked = options.RequireValidBeforeCrit;
            critTotal.Value = Clamp(critTotal, (decimal)options.CritTotalMinimum);
            firstCrit.Value = Clamp(firstCrit, (decimal)options.FirstCritMinimum);
            validCount.Value = Math.Max(0, Math.Min(5, options.ValidCountMinimum));
            firstValid.Checked = options.FirstMustBeValid;
            pause.Checked = options.PauseAfterSuccess;
            for (int index = 0; index < stats.Items.Count; index++)
                stats.SetItemChecked(index, options.ValidStats != null &&
                    Array.IndexOf(options.ValidStats, stats.Items[index].ToString()) >= 0);
        }

        private void Commit(bool start)
        {
            var selected = new System.Collections.Generic.List<string>();
            foreach (object item in stats.CheckedItems)
                selected.Add(item.ToString());
            var value = new EnhanceOptions
            {
                RequireBothCrit = bothCrit.Checked,
                RequireValidBeforeCrit = validBeforeCrit.Checked,
                CritTotalMinimum = (double)critTotal.Value,
                FirstCritMinimum = (double)firstCrit.Value,
                ValidCountMinimum = (int)validCount.Value,
                FirstMustBeValid = firstValid.Checked,
                ValidStats = selected.ToArray(),
                PauseAfterSuccess = pause.Checked
            };
            try { value.Validate(); }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = value;
            StartRequested = start;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                help.Dispose();
            base.Dispose(disposing);
        }

        private static decimal Clamp(NumericUpDown control, decimal value)
        {
            return Math.Max(control.Minimum, Math.Min(control.Maximum, value));
        }

        private static NumericUpDown Number(int minimum, int maximum, decimal value, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = minimum, Maximum = maximum, Value = Math.Max(minimum, Math.Min(maximum, value)),
                DecimalPlaces = decimals, Increment = decimals > 0 ? 0.1m : 1m, Width = 96,
                TextAlign = HorizontalAlignment.Left, Margin = new Padding(0, 2, 0, 6)
            };
        }

        private static CheckBox Option(string name, bool value)
        {
            return new CheckBox { Text = name, AutoSize = true, Checked = value, Margin = new Padding(0, 2, 0, 6) };
        }

        private static void Add(TableLayoutPanel layout, string name, Control control, int row)
        {
            if (control is CheckBox)
            {
                layout.Controls.Add(control, 0, row);
                layout.SetColumnSpan(control, 2);
                return;
            }
            Label label = Theme.Text(name, Theme.Muted);
            label.Margin = new Padding(0, 6, 8, 0);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }
}
