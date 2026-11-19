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
        private readonly NumericUpDown validCount = Number(1, 5, 3, 0);
        private readonly CheckBox firstValid = Option("第一条必须为有效词条", true);
        private readonly CheckedListBox stats = new CheckedListBox
        {
            CheckOnClick = true, Width = 250, Height = 132, IntegralHeight = false,
            Margin = new Padding(0, 2, 0, 6)
        };
        private readonly CheckBox pause = Option("成功后暂停任务", true);
        private readonly Label hint = Theme.Text(
            "游戏语言需为简体/繁体中文。开始前：打开背包声骸界面，筛选要强化的声骸并按等级从 0 排序；" +
            "强化设置中开启「阶段放入」和「同时调谐」。", Theme.Muted);

        internal EnhanceForm(EnhanceOptions options)
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
            ClientSize = new Size(470, 486);

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
            hint.AutoSize = false;
            hint.Height = 58;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 1, 8);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            var cancel = Theme.Button("取消");
            var ok = Theme.Button("开始", true);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            layout.Controls.Add(actions, 0, 9);
            layout.SetColumnSpan(actions, 2);

            stats.Items.AddRange(EnhanceOptions.AllStats);
            bothCrit.Checked = options.RequireBothCrit;
            validBeforeCrit.Checked = options.RequireValidBeforeCrit;
            critTotal.Value = Clamp(critTotal, (decimal)options.CritTotalMinimum);
            firstCrit.Value = Clamp(firstCrit, (decimal)options.FirstCritMinimum);
            validCount.Value = Math.Max(1, Math.Min(5, options.ValidCountMinimum));
            firstValid.Checked = options.FirstMustBeValid;
            pause.Checked = options.PauseAfterSuccess;
            if (options.ValidStats != null)
            {
                foreach (string stat in options.ValidStats)
                {
                    int index = stats.Items.IndexOf(stat);
                    if (index >= 0) stats.SetItemChecked(index, true);
                }
            }
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal EnhanceOptions Result { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (DialogResult == DialogResult.OK)
            {
                var selected = new System.Collections.Generic.List<string>();
                foreach (object item in stats.CheckedItems)
                    selected.Add(item.ToString());
                Result = new EnhanceOptions
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
                try { Result.Validate(); }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    args.Cancel = true;
                    DialogResult = DialogResult.None;
                }
            }
            base.OnFormClosing(args);
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
            Label label = Theme.Text(name, Theme.Muted);
            label.Margin = new Padding(0, 6, 8, 0);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }
}
