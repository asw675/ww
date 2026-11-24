using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class DailyForm : Form
    {
        private readonly ListView tasks = new ListView
        {
            View = View.Details, HeaderStyle = ColumnHeaderStyle.None, CheckBoxes = true,
            FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill,
            AccessibleName = "一条龙任务执行顺序", Margin = new Padding(0, 6, 0, 6)
        };
        private readonly Button moveUp = Theme.Button("上移");
        private readonly Button moveDown = Theme.Button("下移");
        private readonly Button start = Theme.Button("保存并开始", true);
        private readonly Button save = Theme.Button("仅保存");
        private readonly Label summary = Theme.Text("", Theme.Muted);
        private readonly ComboBox farm = Theme.Select("无音区", "材料本", "模拟领域", "培养目标");
        private readonly NumericUpDown tacetIndex = Number(1, 21, 1);
        private readonly NumericUpDown forgeryIndex = Number(1, 20, 1);
        private readonly ComboBox material = Theme.Select(DomainOptions.Materials);
        private readonly ComboBox nightmare = Theme.Select("按日常需求补一只", "刷取一只", "清理全部");
        private readonly CheckBox exitAfter = new CheckBox
            { Text = "完成后退出游戏与程序", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        private readonly Label farm4CNote = Theme.Text("刷 4C 需先在刷 4C 设置里开启自动传送。", Theme.Muted);

        internal DailyForm(DailyOptions options, bool saveOnly = false)
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
            ClientSize = new Size(654, 408);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(18, 16, 18, 12),
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            var hint = Theme.Text("勾选要执行的任务，按列表从上到下运行。选中一行后可调整顺序。", Theme.Muted);
            hint.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(hint, 0, 0);
            layout.SetColumnSpan(hint, 2);
            layout.Controls.Add(BuildTasks(), 0, 1);
            layout.Controls.Add(BuildOptions(), 1, 1);
            summary.Margin = new Padding(0, 8, 0, 0);
            summary.Anchor = AnchorStyles.Left;
            layout.Controls.Add(summary, 0, 2);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true,
                WrapContents = false, Margin = new Padding(0, 8, 0, 0)
            };
            var cancel = Theme.Button("取消");
            cancel.Margin = Padding.Empty;
            cancel.DialogResult = DialogResult.Cancel;
            start.DialogResult = DialogResult.OK;
            save.DialogResult = DialogResult.OK;
            start.Click += delegate { StartRequested = true; };
            save.Click += delegate { StartRequested = false; };
            actions.Controls.Add(cancel);
            start.Visible = !saveOnly;
            if (saveOnly) save.Text = "保存";
            actions.Controls.Add(start);
            actions.Controls.Add(save);
            layout.Controls.Add(actions, 1, 2);

            farm.SelectedIndex = (int)options.Farm;
            tacetIndex.Value = Math.Max(1, Math.Min(21, options.TacetIndex));
            forgeryIndex.Value = Math.Max(1, Math.Min(20, options.ForgeryIndex));
            int materialIndex = Array.IndexOf(DomainOptions.Materials, options.Material);
            material.SelectedIndex = materialIndex >= 0 ? materialIndex : DomainOptions.Materials.Length - 1;
            nightmare.SelectedIndex = (int)options.GetNightmareMode();
            exitAfter.Checked = options.ExitAfterTask;

            DailyTask[] enabled = options.GetTasks();
            foreach (DailyTask task in enabled)
                AddTask(task, true);
            foreach (DailyTask task in Enum.GetValues(typeof(DailyTask)))
                if (Array.IndexOf(enabled, task) < 0)
                    AddTask(task, false);
            tasks.Items[0].Selected = true;
            tasks.ItemChecked += delegate { RefreshSelection(); };
            tasks.SelectedIndexChanged += delegate { RefreshMoveButtons(); };
            farm.SelectedIndexChanged += delegate { RefreshSelection(); };
            moveUp.Click += delegate { MoveTask(-1); };
            moveDown.Click += delegate { MoveTask(1); };
            RefreshSelection();
            RefreshMoveButtons();
            AcceptButton = save;
            CancelButton = cancel;
        }

        internal DailyOptions Result { get; private set; }
        internal bool StartRequested { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (DialogResult == DialogResult.OK)
            {
                var value = new DailyOptions
                {
                    Tasks = CheckedTasks().ToArray(),
                    Farm = (DomainKind)farm.SelectedIndex,
                    TacetIndex = (int)tacetIndex.Value,
                    ForgeryIndex = (int)forgeryIndex.Value,
                    Material = material.SelectedItem as string,
                    NightmareMode = (DailyNightmareMode)nightmare.SelectedIndex,
                    ExitAfterTask = exitAfter.Checked
                };
                try
                {
                    DailyTask[] chosen = value.Tasks;
                    value.FarmNightmareEcho = chosen.Contains(DailyTask.Nightmare);
                    value.AutoNightmare = value.FarmNightmareEcho && value.NightmareMode == DailyNightmareMode.All;
                    value.CheckGarden = chosen.Contains(DailyTask.Garden);
                    value.MergeIfNeeded = chosen.Contains(DailyTask.Merge);
                    value.Farm4C = chosen.Contains(DailyTask.Farm4C);
                    value.Tasks = null;
                    if (!value.GetTasks().SequenceEqual(chosen))
                        value.Tasks = chosen;
                    value.Validate();
                    Result = value;
                }
                catch (InvalidDataException error)
                {
                    summary.ForeColor = Theme.Warning;
                    summary.Text = error.Message;
                    args.Cancel = true;
                    DialogResult = DialogResult.None;
                }
            }
            base.OnFormClosing(args);
        }

        private Control BuildTasks()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12),
                Margin = new Padding(0, 0, 8, 0), BackColor = Theme.White
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(Theme.Text("任务顺序", Theme.Ink, true), 0, 0);
            tasks.Columns.Add("任务");
            tasks.Resize += delegate
            {
                tasks.Columns[0].Width = Math.Max(1, tasks.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
            };
            layout.Controls.Add(tasks, 0, 1);
            var reorder = Theme.Row();
            reorder.Controls.Add(moveUp);
            reorder.Controls.Add(moveDown);
            layout.Controls.Add(reorder, 0, 2);
            return layout;
        }

        private Control BuildOptions()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(12),
                Margin = new Padding(0), BackColor = Theme.White
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 8; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = Theme.Text("任务选项", Theme.Ink, true);
            layout.Controls.Add(title, 0, 0);
            layout.SetColumnSpan(title, 2);
            Add(layout, "体力本类型", farm, 1);
            Add(layout, "无音区序号", tacetIndex, 2);
            Add(layout, "材料本序号", forgeryIndex, 3);
            Add(layout, "模拟领域材料", material, 4);
            Add(layout, "梦魇巢穴", nightmare, 5);
            layout.Controls.Add(exitAfter, 0, 6);
            layout.SetColumnSpan(exitAfter, 2);
            farm4CNote.Margin = new Padding(0, 10, 0, 0);
            layout.Controls.Add(farm4CNote, 0, 7);
            layout.SetColumnSpan(farm4CNote, 2);
            return layout;
        }

        private void AddTask(DailyTask task, bool enabled)
        {
            var item = new ListViewItem(DailyPlan.Label(task)) { Tag = task, Checked = enabled };
            tasks.Items.Add(item);
        }

        private List<DailyTask> CheckedTasks()
        {
            var enabled = new List<DailyTask>();
            foreach (ListViewItem item in tasks.Items)
                if (item.Checked)
                    enabled.Add((DailyTask)item.Tag);
            return enabled;
        }

        private void RefreshSelection()
        {
            List<DailyTask> enabled = CheckedTasks();
            bool canSave = enabled.Count > 0;
            start.Enabled = save.Enabled = canSave;
            summary.Text = canSave ? "已选 " + enabled.Count + " 项任务" : "请至少勾选一项任务。";
            summary.ForeColor = canSave ? Theme.Muted : Theme.Warning;
            farm.Enabled = enabled.Contains(DailyTask.Stamina);
            tacetIndex.Enabled = farm.Enabled && farm.SelectedIndex == (int)DomainKind.Tacet;
            forgeryIndex.Enabled = farm.Enabled && farm.SelectedIndex == (int)DomainKind.Forgery;
            material.Enabled = farm.Enabled && farm.SelectedIndex == (int)DomainKind.Simulation;
            nightmare.Enabled = enabled.Contains(DailyTask.Nightmare);
            farm4CNote.Visible = enabled.Contains(DailyTask.Farm4C);
        }

        private void RefreshMoveButtons()
        {
            int current = tasks.SelectedIndices.Count == 0 ? -1 : tasks.SelectedIndices[0];
            moveUp.Enabled = current > 0;
            moveDown.Enabled = current >= 0 && current < tasks.Items.Count - 1;
        }

        private void MoveTask(int offset)
        {
            if (tasks.SelectedItems.Count == 0)
                return;
            ListViewItem item = tasks.SelectedItems[0];
            int index = item.Index;
            int destination = index + offset;
            if (destination < 0 || destination >= tasks.Items.Count)
                return;
            bool enabled = item.Checked;
            tasks.BeginUpdate();
            try
            {
                tasks.Items.RemoveAt(index);
                tasks.Items.Insert(destination, item);
                item.Checked = enabled;
                item.Selected = item.Focused = true;
                item.EnsureVisible();
            }
            finally
            {
                tasks.EndUpdate();
            }
            RefreshSelection();
            RefreshMoveButtons();
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
            if (control is ComboBox)
                control.Width = 155;
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }
}
