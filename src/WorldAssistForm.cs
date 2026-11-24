using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class WorldAssistForm : Form
    {
        private readonly CheckBox pickup = Toggle("自动拾取");
        private readonly CheckBox skip = Toggle("跳过剧情");
        private readonly CheckBox login = Toggle("自动进入游戏");
        private readonly CheckBox travel = Toggle("自动快速旅行");
        private readonly CheckBox cursor = Toggle("恢复后台游戏导致的跳鼠");
        private readonly CheckBox monthly = Toggle("定时检查月卡");
        private readonly CheckBox startup = Toggle("应用启动时启用普通辅助（登录需手动启动）");
        private readonly CheckBox startupCloud = Toggle("允许已开启的专用后台云游戏参与启动辅助");
        private readonly CheckBox startupBattle = Toggle("应用启动时同时运行自动战斗（明确授权）");
        private readonly NumericUpDown hour = new NumericUpDown { Minimum = 1, Maximum = 24, Width = 50 };
        private readonly TextBox allow = new TextBox { Width = 390 };
        private readonly TextBox block = new TextBox { Width = 390 };

        internal WorldAssistForm(WorldAssistOptions options)
        {
            Text = "战斗辅助设置";
            Font = Theme.FontAt(9.5f);
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(455, 506);
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(16, 12, 16, 12) };
            Controls.Add(layout);
            layout.Controls.Add(Theme.Text("可随应用或战斗运行，并可在任务菜单独立启动。", Theme.Muted));
            pickup.Checked = options.PickUp;
            skip.Checked = options.SkipDialog;
            login.Checked = options.Login;
            travel.Checked = options.Travel;
            cursor.Checked = options.RestoreCursor;
            startup.Checked = options.StartsAutomatically;
            startupCloud.Checked = options.StartManagedCloudWithUtility == true;
            startupBattle.Checked = options.StartBattleWithUtility == true;
            monthly.Checked = options.CheckMonthlyCard ?? true;
            int normalizedHour = MonthlyCardSchedule.NormalizeHour(options.MonthlyCardHour ?? 4);
            hour.Value = normalizedHour == 0 ? 24 : normalizedHour;
            var schedule = Theme.Row();
            schedule.Controls.AddRange(new Control[] { monthly, Theme.Text("本地时间（24=零点）", Theme.Muted), hour });
            allow.Text = string.Join("，", options.PickUpAllow ?? WorldAssistOptions.DefaultAllow);
            block.Text = string.Join("，", options.PickUpBlock ?? WorldAssistOptions.DefaultBlock);
            layout.Controls.AddRange(new Control[] { startup, startupCloud, startupBattle, pickup, skip, login, travel, cursor, schedule,
                Theme.Text("允许的对话交互（逗号分隔）", Theme.Muted), allow,
                Theme.Text("禁止拾取的文字（逗号分隔）", Theme.Muted), block });
            var actions = new FlowLayoutPanel { Width = 390, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var save = Theme.Button("保存", true);
            var cancel = Theme.Button("取消");
            save.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.AddRange(new Control[] { save, cancel });
            layout.Controls.Add(actions);
            AcceptButton = save;
            CancelButton = cancel;
        }

        internal WorldAssistOptions Result
        {
            get
            {
                return new WorldAssistOptions { PickUp = pickup.Checked, SkipDialog = skip.Checked,
                    Login = login.Checked, Travel = travel.Checked, RestoreCursor = cursor.Checked,
                    StartWithUtility = startup.Checked,
                    StartManagedCloudWithUtility = startupCloud.Checked, StartBattleWithUtility = startupBattle.Checked,
                    CheckMonthlyCard = monthly.Checked, MonthlyCardHour = (int)hour.Value,
                    PickUpAllow = Terms(allow.Text), PickUpBlock = Terms(block.Text) };
            }
        }

        private static string[] Terms(string value)
        {
            return value.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim()).Where(term => term.Length > 0).ToArray();
        }

        private static CheckBox Toggle(string text)
        {
            return new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 7, 0, 4) };
        }
    }
}
