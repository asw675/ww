using System;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class FarmForm : Form
    {
        private readonly ComboBox teleport = Theme.Select("不使用（手动站到 Boss 前）", "Boss 挑战",
            "周常挑战", "特异梦魇");
        private readonly NumericUpDown bossIndex = Number(1, 20, 1);
        private readonly ComboBox level = Theme.Select("50", "60", "70", "80", "90");
        private readonly NumericUpDown repeat = Number(1, 100000, 10000);
        private readonly NumericUpDown wait = Number(0, 60, 0);
        private readonly ComboBox pickup = Theme.Select("向前走", "绕圈", "YOLO 检测");
        private readonly CheckBox healer = new CheckBox { Text = "战斗前后切换到治疗", AutoSize = true };
        private readonly Label hint = Theme.Text("自动传送会打开 F2 图鉴，需要游戏窗口可见。", Theme.Muted);

        internal FarmForm(FarmOptions options)
        {
            Text = "刷 4C 声骸";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(438, 372);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 16, 18, 12),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            Add(layout, "传送方式", teleport, 0);
            Add(layout, "Boss 序号", bossIndex, 1);
            Add(layout, "挑战等级", level, 2);
            Add(layout, "刷取次数", repeat, 3);
            Add(layout, "开打前等待（秒）", wait, 4);
            Add(layout, "拾取方式", pickup, 5);
            Add(layout, "战斗设置", healer, 6);
            hint.AutoSize = false;
            hint.Height = 34;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 1, 7);

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
            layout.Controls.Add(actions, 0, 8);
            layout.SetColumnSpan(actions, 2);

            teleport.SelectedIndex = options.Teleport ? Math.Max(1, (int)options.TeleportMode) : 0;
            bossIndex.Value = Math.Max(1, Math.Min(20, options.BossIndex));
            level.SelectedIndex = Math.Max(0, Math.Min(4, (options.BossLevel - 50) / 10));
            repeat.Value = Math.Max(1, Math.Min(100000, options.RepeatCount));
            wait.Value = Math.Max(0, Math.Min(60, options.WaitSeconds));
            pickup.SelectedIndex = (int)options.Pickup;
            healer.Checked = options.SwitchHealer;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal FarmOptions Result { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (DialogResult == DialogResult.OK)
            {
                Result = new FarmOptions
                {
                    Teleport = teleport.SelectedIndex > 0,
                    TeleportMode = (FarmTeleportMode)teleport.SelectedIndex,
                    BossIndex = (int)bossIndex.Value,
                    BossLevel = 50 + level.SelectedIndex * 10,
                    RepeatCount = (int)repeat.Value,
                    WaitSeconds = (int)wait.Value,
                    Pickup = (FarmPickupMode)pickup.SelectedIndex,
                    SwitchHealer = healer.Checked
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

        private static void Add(TableLayoutPanel layout, string name, Control control, int row)
        {
            Label label = Theme.Text(name, Theme.Muted);
            label.Margin = new Padding(0, 6, 8, 0);
            layout.Controls.Add(label, 0, row);
            control.Margin = new Padding(0, 2, 0, 6);
            layout.Controls.Add(control, 1, row);
        }
    }
}
