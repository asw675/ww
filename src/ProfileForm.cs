using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class ProfileForm : Form
    {
        private readonly ComboBox existing = Theme.Select();
        private readonly TextBox name = new TextBox { MaxLength = 24, Width = 220 };
        private readonly ComboBox role = Theme.Select(Theme.Roles);
        private readonly ComboBox order = Theme.Select(Theme.Orders);
        private readonly ComboBox slot = Theme.Select("1 号位", "2 号位", "3 号位");
        private readonly NumericUpDown seconds = Theme.FieldTime();
        private readonly Button train = Theme.Button("录入头像");
        private readonly Button save = Theme.Button("保存档案", true);
        private readonly Button remove = Theme.Button("删除档案");
        private readonly Label message = Theme.Text("录入后可在不同槽位识别角色。", Theme.Muted);
        private readonly List<CharacterProfile> characters;
        private readonly GameSource source;
        private byte[] sample;
        private CancellationTokenSource enrollment;

        internal ProfileForm(List<CharacterProfile> characters, GameSource source = GameSource.AutoLocal)
        {
            this.characters = characters.ToList();
            this.source = source;
            Text = "角色档案";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            Font = Theme.FontAt(9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ForeColor = Theme.Ink;
            BackColor = Theme.White;
            ClientSize = new Size(545, 342);
            Build();

            existing.Items.Add("新建角色");
            foreach (CharacterProfile profile in characters)
                existing.Items.Add(profile.Name);
            existing.SelectedIndexChanged += delegate { LoadSelection(); };
            role.SelectedIndexChanged += delegate { UseRoleDefaults(); };
            train.Click += delegate { Enroll(); };
            save.Click += delegate { SaveProfile(); };
            remove.Click += delegate { RemoveProfile(); };
            existing.SelectedIndex = 0;
        }

        internal List<CharacterProfile> Result { get { return characters; } }

        private void Build()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8,
                Padding = new Padding(20, 15, 20, 14), BackColor = Theme.White };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 6; index++)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            Controls.Add(grid);
            existing.Width = 220;
            role.Width = 140;
            order.Width = 220;
            slot.Width = 101;
            AddField(grid, "选择档案", existing, 0);
            AddField(grid, "角色名称", name, 1);
            AddField(grid, "定位", role, 2);
            AddField(grid, "技能优先级", order, 3);
            AddField(grid, "驻场秒数", seconds, 4);
            var captureRow = Theme.Row();
            captureRow.Controls.Add(slot);
            captureRow.Controls.Add(train);
            AddField(grid, "当前游戏槽位", captureRow, 5);
            message.AutoSize = false;
            message.Dock = DockStyle.Fill;
            grid.Controls.Add(message, 0, 6);
            grid.SetColumnSpan(message, 2);
            var buttons = Theme.Row();
            var cancel = Theme.Button("取消");
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(save);
            buttons.Controls.Add(remove);
            buttons.Controls.Add(cancel);
            grid.Controls.Add(buttons, 1, 7);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static void AddField(TableLayoutPanel grid, string label, Control input, int row)
        {
            var text = Theme.Text(label, Theme.Muted);
            text.Anchor = AnchorStyles.Left;
            input.Anchor = AnchorStyles.Left;
            grid.Controls.Add(text, 0, row);
            grid.Controls.Add(input, 1, row);
        }

        private void LoadSelection()
        {
            CharacterProfile profile = existing.SelectedIndex > 0 ? characters[existing.SelectedIndex - 1] : null;
            name.Text = profile != null ? profile.Name : string.Empty;
            sample = profile != null ? profile.Portrait : null;
            Rotation rotation = profile != null ? profile.Rotation : Rotation.ForRole(BattleRole.Main);
            role.SelectedIndex = (int)rotation.Role;
            order.SelectedIndex = (int)rotation.Order;
            seconds.Value = (decimal)rotation.FieldSeconds;
            remove.Enabled = profile != null;
            SetMessage(profile != null ? "此档案已有头像样本，可重新录入。" : "选择角色所在槽位，点击录入头像。");
        }

        private void UseRoleDefaults()
        {
            Rotation preset = Rotation.ForRole((BattleRole)role.SelectedIndex);
            order.SelectedIndex = (int)preset.Order;
            seconds.Value = (decimal)preset.FieldSeconds;
        }

        private async void Enroll()
        {
            if (enrollment != null)
                return;
            int currentSlot = slot.SelectedIndex;
            var cancellation = new CancellationTokenSource();
            enrollment = cancellation;
            existing.Enabled = slot.Enabled = train.Enabled = save.Enabled = remove.Enabled = false;
            SetMessage(source == GameSource.Foreground || source == GameSource.Cloud ?
                "请在 15 秒内切到游戏并显示该槽位的队伍头像。" : "正在查找游戏画面，请显示该槽位的队伍头像。");
            try
            {
                byte[] portrait = await Task.Run(() => BattleLoop.EnrollPortrait(currentSlot, source, cancellation.Token));
                if (!IsDisposed)
                {
                    sample = portrait;
                    SetMessage("已捕获 " + (currentSlot + 1) + " 号位头像，可以保存档案。");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed)
                    SetMessage(error.Message, true);
            }
            finally
            {
                enrollment = null;
                cancellation.Dispose();
                if (!IsDisposed)
                {
                    existing.Enabled = slot.Enabled = train.Enabled = save.Enabled = true;
                    remove.Enabled = existing.SelectedIndex > 0;
                }
            }
        }

        private void SaveProfile()
        {
            int index = existing.SelectedIndex - 1;
            string label = name.Text.Trim();
            if (label.Length == 0 || sample == null)
            {
                SetMessage("填写角色名称并录入头像后再保存。", true);
                return;
            }
            for (int item = 0; item < characters.Count; item++)
            {
                if (item == index || !string.Equals(characters[item].Name, label, StringComparison.OrdinalIgnoreCase))
                    continue;
                SetMessage("已有同名档案，请选择它进行编辑。", true);
                return;
            }
            var profile = new CharacterProfile
            {
                Name = label, Portrait = sample,
                Rotation = new Rotation { Role = (BattleRole)role.SelectedIndex,
                    Order = (SkillOrder)order.SelectedIndex, FieldSeconds = (double)seconds.Value }
            };
            if (index < 0)
                characters.Add(profile);
            else
                characters[index] = profile;
            DialogResult = DialogResult.OK;
        }

        private void RemoveProfile()
        {
            int index = existing.SelectedIndex - 1;
            if (index < 0)
                return;
            if (MessageBox.Show(this, "删除「" + characters[index].Name + "」的角色档案？", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            characters.RemoveAt(index);
            DialogResult = DialogResult.OK;
        }

        private void SetMessage(string text, bool warning = false)
        {
            message.Text = text;
            message.ForeColor = warning ? Theme.Warning : Theme.Muted;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (enrollment != null)
                enrollment.Cancel();
            base.OnFormClosing(e);
        }
    }
}
