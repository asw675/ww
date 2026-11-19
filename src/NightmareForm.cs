using System;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class NightmareForm : Form
    {
        private readonly CheckBox tacetNest = new CheckBox
        {
            Text = "声骸巢穴（Tacet Discord Nest）", AutoSize = true, Margin = new Padding(0, 2, 0, 6)
        };
        private readonly CheckBox purification = new CheckBox
        {
            Text = "梦魇净化（Nightmare Purification）", AutoSize = true, Margin = new Padding(0, 2, 0, 6)
        };
        private readonly Label hint = Theme.Text(
            "需要已解锁 F2 图鉴中的对应页签。工具会逐个处理未完成的巢穴，死亡 3 次的目标本轮跳过。", Theme.Muted);

        internal NightmareForm(NightmareOptions options)
        {
            Text = "梦魇巢穴";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(430, 220);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18, 16, 18, 12),
                BackColor = Theme.Background
            };
            Controls.Add(layout);
            layout.Controls.Add(Theme.Text("刷取范围", Theme.Ink, true), 0, 0);
            layout.Controls.Add(tacetNest, 0, 1);
            layout.Controls.Add(purification, 0, 2);
            hint.AutoSize = false;
            hint.Height = 42;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 3);

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
            layout.Controls.Add(actions, 0, 4);

            tacetNest.Checked = options.TacetNest;
            purification.Checked = options.Purification;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal NightmareOptions Result { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (DialogResult == DialogResult.OK)
            {
                Result = new NightmareOptions { TacetNest = tacetNest.Checked, Purification = purification.Checked };
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
    }
}
