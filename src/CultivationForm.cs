using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class CultivationForm : Form
    {
        private readonly CheckBox ascension = Option("共鸣者突破材料");
        private readonly CheckBox skills = Option("技能升级材料");
        private readonly CheckBox tacet = Option("培养声骸的无音区");
        private readonly CheckBox nightmare = Option("培养声骸的梦魇巢穴");

        internal CultivationForm(CultivationOptions options, bool saveOnly = false)
        {
            Text = "培养目标";
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(430, 260);
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(16, 12, 16, 12) };
            Controls.Add(layout);
            layout.Controls.Add(Theme.Text("请先在索拉指南设置培养目标。使用简体中文、16:9 游戏画面。", Theme.Muted));
            ascension.Checked = options.Ascension;
            skills.Checked = options.Skills;
            tacet.Checked = options.Tacet;
            nightmare.Checked = options.Nightmare;
            layout.Controls.AddRange(new Control[] { ascension, skills, tacet, nightmare });
            var actions = new FlowLayoutPanel { Width = 390, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var start = Theme.Button(saveOnly ? "保存" : "保存并开始", true);
            var save = Theme.Button("仅保存");
            var cancel = Theme.Button("取消");
            start.DialogResult = DialogResult.OK;
            save.DialogResult = DialogResult.Yes;
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(start);
            if (!saveOnly) actions.Controls.Add(save);
            else save.Dispose();
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions);
            AcceptButton = start;
            CancelButton = cancel;
        }

        internal CultivationOptions Result
        {
            get
            {
                return new CultivationOptions { Ascension = ascension.Checked, Skills = skills.Checked,
                    Tacet = tacet.Checked, Nightmare = nightmare.Checked };
            }
        }

        private static CheckBox Option(string label)
        {
            return new CheckBox { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 3) };
        }
    }
}
