using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    // The sequential and configured entry points share the multi-account
    // parent setting, independent of the standalone daily task's option.
    internal sealed class MultiAccountRunForm : Form
    {
        private readonly CheckBox exitAfter = new CheckBox
        { Text = "多账号全部完成后退出游戏与程序", AutoSize = true };

        internal MultiAccountRunForm(bool exitAfterTask)
        {
            Text = "多账号日常设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            ClientSize = new Size(420, 150);
            exitAfter.Checked = exitAfterTask;

            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(16) };
            layout.Controls.Add(Theme.Text("使用已保存的一条龙计划执行各账号。", Theme.Muted));
            layout.Controls.Add(exitAfter);
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var start = Theme.Button("保存并开始", true);
            var save = Theme.Button("仅保存");
            var cancel = Theme.Button("取消");
            start.Click += delegate { StartRequested = true; DialogResult = DialogResult.OK; Close(); };
            save.Click += delegate { StartRequested = false; DialogResult = DialogResult.OK; Close(); };
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(start);
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions);
            Controls.Add(layout);
            AcceptButton = start;
            CancelButton = cancel;
        }

        internal bool StartRequested { get; private set; }
        internal bool ExitAfterTask { get { return exitAfter.Checked; } }
    }
}
