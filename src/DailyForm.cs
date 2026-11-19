using System;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class DailyForm : Form
    {
        private readonly ComboBox farm = Theme.Select("无音区", "材料本", "模拟领域");
        private readonly NumericUpDown tacetIndex = Number(1, 21, 1);
        private readonly NumericUpDown forgeryIndex = Number(1, 20, 1);
        private readonly ComboBox material = Theme.Select(DomainOptions.Materials);
        private readonly CheckBox farmNightmareEcho = Option("刷梦魇巢穴补日常声骸", true);
        private readonly CheckBox autoNightmare = Option("自动全刷梦魇巢穴", false);
        private readonly CheckBox garden = Option("检查并执行周常乐园", true);
        private readonly CheckBox merge = Option("弃置声骸超过 1000 时合并", false);
        private readonly CheckBox farm4C = Option("结束后刷 4C 声骸（需开启 4C 传送）", false);
        private readonly CheckBox exitAfter = Option("完成后退出游戏与程序", false);
        private readonly Label hint = Theme.Text(
            "一条龙：读取日常进度 → 补梦魇/体力 → 领日常/邮件/战令 → 可选附加任务。", Theme.Muted);

        internal DailyForm(DailyOptions options)
        {
            Text = "一条龙";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(470, 420);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 16, 18, 12),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            Add(layout, "刷取目标", farm, 0);
            Add(layout, "无音区序号", tacetIndex, 1);
            Add(layout, "材料本序号", forgeryIndex, 2);
            Add(layout, "模拟领域材料", material, 3);
            Add(layout, "附加", farmNightmareEcho, 4);
            layout.Controls.Add(autoNightmare, 1, 5);
            layout.Controls.Add(garden, 1, 6);
            layout.Controls.Add(merge, 1, 7);
            layout.Controls.Add(farm4C, 1, 8);
            layout.Controls.Add(exitAfter, 1, 9);
            hint.AutoSize = false;
            hint.Height = 38;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 1, 10);

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
            layout.Controls.Add(actions, 0, 11);
            layout.SetColumnSpan(actions, 2);

            farm.SelectedIndex = (int)options.Farm;
            tacetIndex.Value = Math.Max(1, Math.Min(21, options.TacetIndex));
            forgeryIndex.Value = Math.Max(1, Math.Min(20, options.ForgeryIndex));
            int materialIndex = Array.IndexOf(DomainOptions.Materials, options.Material);
            material.SelectedIndex = materialIndex >= 0 ? materialIndex : DomainOptions.Materials.Length - 1;
            farmNightmareEcho.Checked = options.FarmNightmareEcho;
            autoNightmare.Checked = options.AutoNightmare;
            garden.Checked = options.CheckGarden;
            merge.Checked = options.MergeIfNeeded;
            farm4C.Checked = options.Farm4C;
            exitAfter.Checked = options.ExitAfterTask;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal DailyOptions Result { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (DialogResult == DialogResult.OK)
            {
                Result = new DailyOptions
                {
                    Farm = (DomainKind)farm.SelectedIndex,
                    TacetIndex = (int)tacetIndex.Value,
                    ForgeryIndex = (int)forgeryIndex.Value,
                    Material = material.SelectedItem == null ? DomainOptions.Materials[2] :
                        material.SelectedItem.ToString(),
                    FarmNightmareEcho = farmNightmareEcho.Checked,
                    AutoNightmare = autoNightmare.Checked,
                    CheckGarden = garden.Checked,
                    MergeIfNeeded = merge.Checked,
                    Farm4C = farm4C.Checked,
                    ExitAfterTask = exitAfter.Checked
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

        private static NumericUpDown Number(int minimum, int maximum, int value)
        {
            return new NumericUpDown
            {
                Minimum = minimum, Maximum = maximum, Value = Math.Max(minimum, Math.Min(maximum, value)),
                Width = 96, TextAlign = HorizontalAlignment.Left, Margin = new Padding(0, 2, 0, 6)
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
