using System;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class DomainForm : Form
    {
        private readonly ComboBox kind = Theme.Select("无音区", "材料本", "模拟领域");
        private readonly NumericUpDown index = new NumericUpDown
        {
            Minimum = 1, Maximum = 21, Value = 1, Width = 96, TextAlign = HorizontalAlignment.Left,
            Margin = new Padding(0, 2, 0, 6)
        };
        private readonly ComboBox material = Theme.Select(DomainOptions.Materials);
        private readonly Label hint = Theme.Text(
            "需要已解锁 F2 图鉴中的对应副本，工具会自动读取体力并反复刷取。", Theme.Muted);

        internal DomainForm(DomainOptions options, bool saveOnly = false)
        {
            Text = "体力本";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(410, 260);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 16, 18, 12),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            Add(layout, "副本类型", kind, 0);
            Add(layout, "序号", index, 1);
            Add(layout, "模拟领域材料", material, 2);
            hint.AutoSize = false;
            hint.Height = 40;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 1, 3);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            var cancel = Theme.Button("取消");
            var ok = Theme.Button(saveOnly ? "保存" : "开始", true);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(ok);
            layout.Controls.Add(actions, 0, 4);
            layout.SetColumnSpan(actions, 2);

            kind.SelectedIndexChanged += delegate
            {
                index.Maximum = kind.SelectedIndex == (int)DomainKind.Forgery ? 20 : 21;
                index.Enabled = kind.SelectedIndex != (int)DomainKind.Simulation;
                material.Enabled = kind.SelectedIndex == (int)DomainKind.Simulation;
            };
            kind.SelectedIndex = (int)options.Kind;
            kind.Enabled = !saveOnly;
            index.Enabled = options.Kind != DomainKind.Simulation;
            material.Enabled = options.Kind == DomainKind.Simulation;
            index.Value = Math.Max(1, Math.Min(index.Maximum, options.Index));
            int materialIndex = Array.IndexOf(DomainOptions.Materials, options.Material);
            material.SelectedIndex = materialIndex >= 0 ? materialIndex : DomainOptions.Materials.Length - 1;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal DomainOptions Result { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (DialogResult == DialogResult.OK)
            {
                Result = new DomainOptions
                {
                    Kind = (DomainKind)kind.SelectedIndex,
                    Index = (int)index.Value,
                    Material = material.SelectedItem == null ? DomainOptions.Materials[2] :
                        material.SelectedItem.ToString()
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

        private static void Add(TableLayoutPanel layout, string name, Control control, int row)
        {
            Label label = Theme.Text(name, Theme.Muted);
            label.Margin = new Padding(0, 6, 8, 0);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }
}
