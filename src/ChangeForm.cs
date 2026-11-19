using System;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class ChangeForm : Form
    {
        private readonly ComboBox target = Theme.Select(ChangeOptions.Targets);
        private readonly Label hint = Theme.Text(
            "游戏语言需为简体中文。开始前：打开背包声骸界面，筛选要修改的声骸并按等级从 0 排序；" +
            "目标属性不能与当前主属性相同。", Theme.Muted);

        internal ChangeForm(ChangeOptions options)
        {
            Text = "批量改主属性";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(430, 210);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 16, 18, 12),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            Label label = Theme.Text("目标属性", Theme.Muted);
            label.Margin = new Padding(0, 6, 8, 0);
            layout.Controls.Add(label, 0, 0);
            target.Width = 200;
            layout.Controls.Add(target, 1, 0);
            hint.AutoSize = false;
            hint.Height = 56;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 1, 1);

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
            layout.Controls.Add(actions, 0, 2);
            layout.SetColumnSpan(actions, 2);

            int index = Array.IndexOf(ChangeOptions.Targets, options.Target);
            target.SelectedIndex = index >= 0 ? index : 0;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal ChangeOptions Result { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (DialogResult == DialogResult.OK)
            {
                Result = new ChangeOptions { Target = target.SelectedItem.ToString() };
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
