using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class Theme
    {
        internal static readonly Color Background = Color.FromArgb(237, 242, 244);
        internal static readonly Color Ink = Color.FromArgb(21, 43, 52);
        internal static readonly Color Muted = Color.FromArgb(79, 105, 113);
        internal static readonly Color Accent = Color.FromArgb(8, 107, 102);
        internal static readonly Color Subtle = Color.FromArgb(225, 241, 239);
        internal static readonly Color White = Color.White;
        internal static readonly Color Warning = Color.FromArgb(151, 60, 30);
        internal static readonly string[] Roles = { "主输出", "副输出", "治疗 / 辅助" };
        internal static readonly string[] Orders = { "共鸣 → 解放 → 声骸", "解放 → 共鸣 → 声骸", "声骸 → 共鸣 → 解放" };

        internal static Font FontAt(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Microsoft YaHei UI", size, style);
        }

        internal static Label Text(string text, Color color, bool bold = false)
        {
            return new Label { AutoSize = true, Text = text, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft,
                Font = bold ? FontAt(10, FontStyle.Bold) : FontAt(9.5f), Margin = new Padding(0, 0, 0, 4) };
        }

        internal static Button Button(string text, bool primary = false)
        {
            var button = new Button
            {
                Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 0, 10, 0),
                ForeColor = primary ? White : Ink, BackColor = primary ? Accent : Subtle,
                Margin = new Padding(0, 0, 7, 0), Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        internal static ComboBox Select(params string[] items)
        {
            var control = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 8, 2) };
            control.Items.AddRange(items);
            if (items.Length > 0)
                control.SelectedIndex = 0;
            return control;
        }

        internal static NumericUpDown FieldTime()
        {
            return new NumericUpDown { Minimum = 1, Maximum = 15, DecimalPlaces = 1, Increment = 0.2m,
                TextAlign = HorizontalAlignment.Center, Width = 67, Margin = new Padding(0, 2, 0, 2) };
        }

        internal static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        }
    }

    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 20;
        private const int HotkeyMessage = 0x0312;

        private readonly Button start = Theme.Button("启动战斗", true);
        private readonly ComboBox taskSelect = Theme.Select("刷 4C 声骸", "批量强化声骸", "无音区", "材料本", "模拟领域", "梦魇巢穴", "周常乐园", "合并声骸", "改主属性", "一条龙", "多账号日常", "多账号配置", "拉海洛方块", "培养目标", "战斗辅助设置", "第二索拉", "启动后台辅助");
        private readonly Button runTask = Theme.Button("运行任务", true);
        private readonly Button resume = Theme.Button("继续");
        private readonly Button preview = Theme.Button("仅检测");
        private readonly Button stop = Theme.Button("停止");
        private readonly Button profiles = Theme.Button("角色档案");
        private readonly Button variants = Theme.Button("角色变体");
        private readonly Label hotkey = Theme.Text("Ctrl + Alt + F8  启动 / 停止", Theme.Muted);
        private readonly Label mode = Theme.Text("已停止", Theme.Accent, true);
        private readonly Label state = Theme.Text("从这里启动，或按全局快捷键。", Theme.Ink);
        private readonly Label target = Theme.Text("—", Theme.Ink, true);
        private readonly Label character = Theme.Text("—", Theme.Ink, true);
        private readonly Label skills = Theme.Text("—", Theme.Ink, true);
        private readonly Label lastAction = Theme.Text("—", Theme.Ink, true);
        private readonly Label targetCaption = Theme.Text("目标", Theme.Muted);
        private readonly Label characterCaption = Theme.Text("当前角色", Theme.Muted);
        private readonly Label skillsCaption = Theme.Text("技能状态", Theme.Muted);
        private readonly Label actionCaption = Theme.Text("最近动作", Theme.Muted);
        private readonly Label footer = Theme.Text("建议使用 1920×1080 游戏画面", Theme.Muted);
        private readonly LinkLabel logFolder = new LinkLabel { Text = "打开日志目录", AutoSize = true,
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        private readonly CheckBox autoTarget = Option("看见敌人血条后自动锁定");
        private readonly CheckBox autoRevive = Option("梦魇巢穴内自动尝试复活");
        private readonly CheckBox useLiberation = Option("大世界使用共鸣解放");
        private readonly CheckBox checkLevitator = Option("检查控物（兼容项）");
        private readonly CheckBox rotate = Option("自动切换队伍角色");
        private readonly CheckBox concerto = Option("切人前优先等协奏充满");
        private readonly CheckBox lowHealth = Option("低血量时优先切治疗");
        private readonly CheckBox supportOpener = Option("战斗前后切到治疗");
        private readonly CheckBox detectKeys = Option("从技能栏识别绑定");
        private readonly ComboBox source = Theme.Select("本地客户端 · 前台", "本地客户端 · 后台",
            "网页云游戏 · 前台", "网页云游戏 · 后台", "本地客户端 · 自动");
        private readonly Button cloudPage = Theme.Button("打开官网云游戏");
        private readonly TextBox guideKey = new TextBox { MaxLength = 18, Width = 66, CharacterCasing = CharacterCasing.Upper };
        private readonly TextBox jumpKey = HotkeyField(), dodgeKey = HotkeyField(), wheelKey = HotkeyField();
        private readonly TextBox bagKey = KeyField();
        private readonly TextBox[] keys = { KeyField(), KeyField(), KeyField(), KeyField() };
        private readonly ComboBox[] roles = new ComboBox[3];
        private readonly ComboBox[] fighters = new ComboBox[3];
        private readonly NumericUpDown[] seconds = new NumericUpDown[3];
        private Panel preferences;
        private Panel team;
        private TableLayoutPanel battleCards;
        private EnhanceStatusPanel enhanceStatus;
        private CombatSettings settings;
        private readonly ConcertoMeter concertoMeter;
        private readonly bool enableGlobalHotkey;
        private CancellationTokenSource work;
        private CancellationTokenSource assistCancellation;
        private Task assistRun;
        private bool assistEnabled;
        private bool startupAssistOnly;
        private bool preserveTaskResult;
        internal enum WorkMode
        {
            Battle, Preview, Farm, Enhance, Domain, Nightmare, Garden, Merge, Change, Daily,
            MultiDaily, MultiConfig, Rahai, Cultivation, SecondSola
        }

        private bool toggleInstalled, closing, inPreview, hasReadout;
        private ManualResetEventSlim enhanceGate;
        private NotifyIcon taskNotice;
        private string persistenceError;
        private string settingsLoadError;

        internal MainForm() : this(SettingsFile.Location) { }

        internal MainForm(string settingsPath, bool enableGlobalHotkey = true)
        {
            this.enableGlobalHotkey = enableGlobalHotkey;
            SettingsPath = settingsPath;
            concertoMeter = new ConcertoMeter(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "CombatConcerto.json"));
            Text = "鸣潮自动战斗";
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.FontAt(9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(918, 664);
            MinimumSize = new Size(878, 674);
            ForeColor = Theme.Ink;
            BackColor = Theme.Background;
            Build();

            try
            {
                settings = SettingsFile.Load(SettingsPath);
                RunLog.Info("设置", "设置已加载，游戏来源=" + settings.Source);
                if (RunLog.Failure != null)
                    SetFooter("无法写入日志。 " + RunLog.Failure, true);
            }
            catch (Exception error)
            {
                RunLog.Error("设置", "读取设置失败，使用默认值", error);
                settings = CombatSettings.Defaults();
                settingsLoadError = FriendlyError(error);
                footer.Text = "配置读取失败，已载入默认值。 " + settingsLoadError;
                footer.ForeColor = Theme.Warning;
            }
            LoadControls();
            start.Click += delegate { BeginRun(false); };
            runTask.Click += delegate { BeginTask(); };
            taskSelect.SelectedIndexChanged += delegate
            {
                if (work == null && taskSelect.SelectedIndex != 1)
                    ShowEnhancementView(false);
            };
            resume.Enabled = false;
            resume.Click += delegate
            {
                if (enhanceGate == null) return;
                RunLog.Info("任务", "用户点击继续");
                enhanceGate.Set();
                resume.Enabled = false;
            };
            preview.Click += delegate { BeginRun(true); };
            stop.Click += delegate { EndRun(); };
            profiles.Click += delegate { OpenProfiles(); };
            variants.Click += delegate { OpenVariants(); };
            logFolder.LinkClicked += delegate { OpenLogFolder(); };
            cloudPage.Click += delegate
            {
                try
                {
                    if (source.SelectedIndex == (int)GameSource.ManagedCloud) CloudBrowser.Open();
                    else System.Diagnostics.Process.Start(CloudBrowser.OfficialPage);
                }
                catch (Exception ex)
                {
                    RunLog.Error("程序", "打开云游戏失败", ex);
                    SetFooter(FriendlyError(ex), true);
                }
            };
        }

        private string SettingsPath { get; set; }

        private void Build()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Padding = new Padding(21, 17, 21, 13), BackColor = Theme.Background };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 139));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            Controls.Add(layout);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 11) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titleGroup = Theme.Row();
            titleGroup.FlowDirection = FlowDirection.TopDown;
            var title = Theme.Text("鸣潮自动战斗", Theme.Ink);
            title.Font = Theme.FontAt(20, FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, 3);
            titleGroup.Controls.Add(title);
            titleGroup.Controls.Add(hotkey);
            header.Controls.Add(titleGroup, 0, 0);
            var actions = Theme.Row();
            actions.Anchor = AnchorStyles.Right;
            taskSelect.Width = 132;
            taskSelect.Margin = new Padding(0, 0, 7, 0);
            taskSelect.DropDownWidth = 260;
            taskSelect.DrawMode = DrawMode.OwnerDrawFixed;
            taskSelect.DrawItem += DrawTaskOption;
            actions.Controls.Add(start);
            actions.Controls.Add(taskSelect);
            actions.Controls.Add(runTask);
            actions.Controls.Add(resume);
            actions.Controls.Add(preview);
            actions.Controls.Add(stop);
            stop.Enabled = false;
            header.Controls.Add(actions, 1, 0);
            layout.Controls.Add(header, 0, 0);

            var report = new TableLayoutPanel { BackColor = Theme.White, Padding = new Padding(16, 12, 16, 10),
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
            report.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            report.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var stateRow = Theme.Row();
            mode.BackColor = Theme.Subtle;
            mode.Padding = new Padding(8, 5, 8, 5);
            mode.Margin = new Padding(0, 0, 10, 0);
            state.Margin = new Padding(0, 5, 0, 0);
            stateRow.Controls.Add(mode);
            stateRow.Controls.Add(state);
            report.Controls.Add(stateRow, 0, 0);
            var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
            metrics.Controls.Add(Metric(targetCaption, target), 0, 0);
            metrics.Controls.Add(Metric(characterCaption, character), 1, 0);
            metrics.Controls.Add(Metric(skillsCaption, skills), 2, 0);
            metrics.Controls.Add(Metric(actionCaption, lastAction), 3, 0);
            report.Controls.Add(metrics, 0, 1);
            layout.Controls.Add(report, 0, 1);

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
            preferences = BuildPreferences();
            team = BuildTeam();
            columns.Controls.Add(preferences, 0, 0);
            columns.Controls.Add(team, 1, 0);
            battleCards = columns;
            enhanceStatus = new EnhanceStatusPanel { Visible = false };
            var details = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            details.Controls.Add(battleCards);
            details.Controls.Add(enhanceStatus);
            layout.Controls.Add(details, 0, 2);

            footer.AutoSize = false;
            footer.AutoEllipsis = true;
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            footer.Height = 22;
            var footerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerRow.Controls.Add(footer, 0, 0);
            footerRow.Controls.Add(logFolder, 1, 0);
            layout.Controls.Add(footerRow, 0, 3);
        }

        private static Control Metric(Label name, Label value)
        {
            var column = Theme.Row();
            column.FlowDirection = FlowDirection.TopDown;
            column.Controls.Add(name);
            column.Controls.Add(value);
            return column;
        }

        private Panel BuildPreferences()
        {
            var card = Card(new Padding(0, 0, 7, 0));
            card.Controls.Add(Theme.Text("战斗设置", Theme.Ink, true));
            var sourceRow = Theme.Row();
            source.Width = 155;
            sourceRow.Controls.Add(source);
            sourceRow.Controls.Add(cloudPage);
            card.Controls.Add(sourceRow);
            cloudPage.Visible = false;
            source.SelectedIndexChanged += delegate
            {
                cloudPage.Visible = source.SelectedIndex == (int)GameSource.Cloud ||
                    source.SelectedIndex == (int)GameSource.ManagedCloud;
            };
            card.Controls.Add(autoTarget);
            card.Controls.Add(autoRevive);
            card.Controls.Add(useLiberation);
            checkLevitator.AccessibleDescription = "切换控物并检查角色是否浮空。参考版本目前未调用此检查，保留配置兼容。";
            card.Controls.Add(checkLevitator);
            card.Controls.Add(rotate);
            card.Controls.Add(concerto);
            card.Controls.Add(lowHealth);
            card.Controls.Add(supportOpener);
            var keyRow = Theme.Row();
            keyRow.Margin = new Padding(0, 8, 0, 0);
            string[] names = { "共鸣", "声骸", "解放", "探索" };
            for (int index = 0; index < names.Length; index++)
            {
                Label label = Theme.Text(names[index], Theme.Muted);
                label.Margin = new Padding(0, 4, 5, 0);
                keyRow.Controls.Add(label);
                keyRow.Controls.Add(keys[index]);
            }
            card.Controls.Add(keyRow);
            var guideRow = Theme.Row();
            guideRow.Controls.Add(detectKeys);
            guideRow.Controls.Add(Theme.Text("图鉴", Theme.Muted));
            guideRow.Controls.Add(guideKey);
            Label bagLabel = Theme.Text("背包", Theme.Muted);
            bagLabel.Margin = new Padding(4, 4, 5, 0);
            guideRow.Controls.Add(bagLabel);
            guideRow.Controls.Add(bagKey);
            card.Controls.Add(guideRow);
            var movementRow = Theme.Row();
            movementRow.Controls.Add(Theme.Text("跳跃", Theme.Muted));
            movementRow.Controls.Add(jumpKey);
            movementRow.Controls.Add(Theme.Text("闪避", Theme.Muted));
            movementRow.Controls.Add(dodgeKey);
            movementRow.Controls.Add(Theme.Text("轮盘", Theme.Muted));
            movementRow.Controls.Add(wheelKey);
            card.Controls.Add(movementRow);
            rotate.CheckedChanged += delegate
            {
                concerto.Enabled = rotate.Checked;
                lowHealth.Enabled = rotate.Checked;
                supportOpener.Enabled = rotate.Checked;
            };
            return card;
        }

        private Panel BuildTeam()
        {
            var card = Card(new Padding(7, 0, 0, 0));
            card.Controls.Add(Theme.Text("队伍轮转", Theme.Ink, true));
            var details = Theme.Text("识别到角色后使用该角色的专用连招。", Theme.Muted);
            details.Margin = new Padding(0, 0, 0, 10);
            card.Controls.Add(details);
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, RowCount = 4, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 141));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 103));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.Controls.Add(Theme.Text("槽位", Theme.Muted), 0, 0);
            grid.Controls.Add(Theme.Text("角色", Theme.Muted), 1, 0);
            grid.Controls.Add(Theme.Text("通用定位", Theme.Muted), 2, 0);
            grid.Controls.Add(Theme.Text("通用驻场", Theme.Muted), 3, 0);
            for (int index = 0; index < roles.Length; index++)
            {
                int slot = index;
                var number = Theme.Text((slot + 1).ToString(), Theme.Ink, true);
                number.Anchor = AnchorStyles.Left;
                grid.Controls.Add(number, 0, slot + 1);
                fighters[slot] = Theme.Select("自动识别");
                fighters[slot].Width = 130;
                fighters[slot].Items.AddRange(CharacterRoster.All);
                fighters[slot].SelectedIndexChanged += delegate
                {
                    bool auto = fighters[slot].SelectedIndex == 0;
                    roles[slot].Enabled = seconds[slot].Enabled = auto;
                };
                grid.Controls.Add(fighters[slot], 1, slot + 1);
                roles[slot] = Theme.Select(Theme.Roles);
                roles[slot].Width = 95;
                seconds[slot] = Theme.FieldTime();
                roles[slot].SelectedIndexChanged += delegate
                {
                    seconds[slot].Value = (decimal)Rotation.ForRole((BattleRole)roles[slot].SelectedIndex).FieldSeconds;
                };
                grid.Controls.Add(roles[slot], 2, slot + 1);
                grid.Controls.Add(seconds[slot], 3, slot + 1);
            }
            card.Controls.Add(grid);
            var actions = Theme.Row();
            actions.Margin = new Padding(0, 14, 0, 0);
            actions.Controls.Add(variants);
            actions.Controls.Add(profiles);
            card.Controls.Add(actions);
            return card;
        }

        private static FlowLayoutPanel Card(Padding margin)
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, BackColor = Theme.White, Padding = new Padding(16, 12, 16, 9), Margin = margin };
        }

        private static CheckBox Option(string name)
        {
            return new CheckBox { Text = name, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        }

        private static TextBox KeyField()
        {
            return new TextBox { MaxLength = 1, Width = 31, CharacterCasing = CharacterCasing.Upper,
                TextAlign = HorizontalAlignment.Center, Margin = new Padding(0, 0, 11, 2) };
        }

        private static TextBox HotkeyField()
        {
            return new TextBox { MaxLength = 18, Width = 66, CharacterCasing = CharacterCasing.Upper,
                TextAlign = HorizontalAlignment.Center, Margin = new Padding(0, 0, 5, 2) };
        }

        internal static string TaskCategory(int index)
        {
            switch (index)
            {
                case 1: case 7: case 8: return "声骸管理";
                case 0: case 2: case 3: case 4: case 5: case 6: case 9: case 10: case 11: case 13: return "日常活动";
                case 12: case 15: return "活动玩法";
                case 14: case 16: return "辅助设置";
                default: return string.Empty;
            }
        }

        private void DrawTaskOption(object sender, DrawItemEventArgs args)
        {
            args.DrawBackground();
            if (args.Index < 0) return;
            bool editing = (args.State & DrawItemState.ComboBoxEdit) != 0;
            Rectangle name = Rectangle.Inflate(args.Bounds, -3, 0);
            if (!editing) name.Width = Math.Max(0, name.Width - 84);
            const TextFormatFlags text = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(args.Graphics, taskSelect.Items[args.Index].ToString(), args.Font, name, args.ForeColor, text);
            if (!editing)
            {
                var category = new Rectangle(name.Right + 4, name.Top, 76, name.Height);
                Color color = (args.State & DrawItemState.Selected) != 0 ? args.ForeColor : Theme.Muted;
                TextRenderer.DrawText(args.Graphics, TaskCategory(args.Index), args.Font, category, color, text);
            }
            args.DrawFocusRectangle();
        }

        private void LoadControls()
        {
            autoTarget.Checked = settings.AutoTarget;
            autoRevive.Checked = settings.AutoRevive;
            useLiberation.Checked = settings.UseLiberation;
            checkLevitator.Checked = settings.CheckLevitator;
            rotate.Checked = settings.RotateParty;
            concerto.Checked = settings.PreferConcerto;
            lowHealth.Checked = settings.SwitchAtLowHealth;
            supportOpener.Checked = settings.SupportOpener;
            concerto.Enabled = rotate.Checked;
            lowHealth.Enabled = rotate.Checked;
            supportOpener.Enabled = rotate.Checked;
            detectKeys.Checked = settings.DetectSkillKeys;
            source.SelectedIndex = (int)settings.Source;
            keys[0].Text = settings.ResonanceKey;
            keys[1].Text = settings.EchoKey;
            keys[2].Text = settings.LiberationKey;
            keys[3].Text = settings.ToolKey;
            guideKey.Text = settings.GuidebookKey ?? "F2";
            bagKey.Text = settings.BagKey ?? "B";
            jumpKey.Text = settings.JumpKey ?? "SPACE";
            dodgeKey.Text = settings.DodgeKey ?? "LSHIFT";
            wheelKey.Text = settings.WheelKey ?? "TAB";
            for (int index = 0; index < roles.Length; index++)
            {
                roles[index].SelectedIndex = (int)settings.Slots[index].Role;
                seconds[index].Value = (decimal)settings.Slots[index].FieldSeconds;
                CharacterKit kit = CharacterRoster.Find(settings.PartyCharacters[index]);
                if (kit != null) fighters[index].SelectedItem = kit;
            }
        }

        private CombatSettings ReadControls()
        {
            var current = new CombatSettings
            {
                AutoTarget = autoTarget.Checked, AutoRevive = autoRevive.Checked,
                UseLiberation = useLiberation.Checked, CheckLevitator = checkLevitator.Checked,
                RotateParty = rotate.Checked, PreferConcerto = concerto.Checked, SwitchAtLowHealth = lowHealth.Checked,
                SupportOpener = supportOpener.Checked, IunoC6 = settings.IunoC6, RocciaC6 = settings.RocciaC6,
                Source = (GameSource)source.SelectedIndex,
                ChisaDps = settings.ChisaDps, SuomingDps = settings.SuomingDps, Farm = settings.Farm,
                Enhance = settings.Enhance, Domain = settings.Domain, Nightmare = settings.Nightmare,
                Change = settings.Change, Daily = settings.Daily, Cultivation = settings.Cultivation,
                MultiAccountExitAfterTask = settings.ExitAfterMultiAccount,
                WorldAssist = settings.WorldAssist, PartyCharacters = new string[3],
                DetectSkillKeys = detectKeys.Checked, ResonanceKey = keys[0].Text, EchoKey = keys[1].Text,
                LiberationKey = keys[2].Text, ToolKey = keys[3].Text, BagKey = bagKey.Text, GuidebookKey = guideKey.Text,
                JumpKey = jumpKey.Text, DodgeKey = dodgeKey.Text, WheelKey = wheelKey.Text,
                Slots = new Rotation[3], Characters = settings.Characters
            };
            for (int index = 0; index < roles.Length; index++)
            {
                current.Slots[index] = Rotation.ForRole((BattleRole)roles[index].SelectedIndex);
                current.Slots[index].FieldSeconds = (double)seconds[index].Value;
                CharacterKit kit = fighters[index].SelectedItem as CharacterKit;
                current.PartyCharacters[index] = kit == null ? null : kit.Id;
            }
            try { current.Validate(); }
            catch (InvalidDataException error)
            {
                RunLog.Warn("设置", "任务设置未通过校验。 " + error.Message);
                throw;
            }
            return current;
        }

        private async void BeginRun(bool isPreview)
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            await StartWork(current, isPreview ? WorkMode.Preview : WorkMode.Battle);
        }

        private async void BeginFarm()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            using (var dialog = new FarmForm(current.Farm ?? FarmOptions.Defaults()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                    return;
                current.Farm = dialog.Result;
            }
            await StartWork(current, WorkMode.Farm);
        }

        private void BeginTask()
        {
            RunLog.Info("任务", "用户选择运行「" + taskSelect.Text + "」");
            switch (taskSelect.SelectedIndex)
            {
                case 0: BeginFarm(); break;
                case 1: BeginEnhance(); break;
                case 2: BeginDomain(DomainKind.Tacet); break;
                case 3: BeginDomain(DomainKind.Forgery); break;
                case 4: BeginDomain(DomainKind.Simulation); break;
                case 5: BeginNightmare(); break;
                case 6: BeginGarden(); break;
                case 7: BeginMerge(); break;
                case 8: BeginChange(); break;
                case 9: BeginDaily(); break;
                case 10: BeginMultiAccount(MultiAccountMode.Sequential); break;
                case 11: BeginMultiAccount(MultiAccountMode.Config); break;
                case 12: BeginRahai(); break;
                case 13: BeginCultivation(); break;
                case 14: SaveWorldAssistOptions(); break;
                case 15: BeginSecondSola(); break;
                case 16:
                    BeginManualAssist();
                    break;
            }
        }

        private async void BeginRahai()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            if (!SupportsRahaiSource(current.Source))
            {
                SetFooter("拉海洛方块需要 Windows 客户端，请使用本地客户端自动或前台模式。", true);
                return;
            }
            await StartWork(current, WorkMode.Rahai);
        }

        private async void BeginCultivation()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            using (var dialog = new CultivationForm(current.Cultivation))
            {
                DialogResult result = dialog.ShowDialog(this);
                if (result != DialogResult.OK && result != DialogResult.Yes)
                    return;
                current.Cultivation = dialog.Result;
                if (result == DialogResult.Yes)
                {
                    settings = current;
                    SaveSettings();
                    if (persistenceError == null)
                        SetFooter("已保存培养目标设置。", false);
                    return;
                }
            }
            await StartWork(current, WorkMode.Cultivation);
        }

        private async void SaveWorldAssistOptions()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            using (var dialog = new WorldAssistForm(current.WorldAssist))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                current.WorldAssist = dialog.Result;
            }
            settings = current;
            SaveSettings();
            if (assistRun != null)
            {
                if (startupAssistOnly && !current.WorldAssist.StartsAutomatically) assistEnabled = false;
                await SuspendAssist();
                BeginAssist();
            }
            if (persistenceError == null)
                SetFooter("已保存战斗辅助设置。", false);
        }

        internal void EnableStartupAssist()
        {
            if (closing || work != null || settingsLoadError != null || settings.WorldAssist == null ||
                !settings.WorldAssist.AllowsStartup(settings.Source)) return;
            WorldAssistOptions startup = settings.WorldAssist.ForStartup();
            if (!startup.NeedsVision && !startup.RestoreCursor && !startup.StartsBattleAutomatically) return;
            assistEnabled = true;
            startupAssistOnly = true;
            BeginAssist();
        }

        private async void BeginManualAssist()
        {
            if (closing || work != null) return;
            preserveTaskResult = false;
            if (assistRun != null && !startupAssistOnly) return;
            assistEnabled = true;
            startupAssistOnly = false;
            await SuspendAssist();
            BeginAssist();
        }

        private void BeginAssist()
        {
            if (!assistEnabled || closing || work != null || assistRun != null)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                assistEnabled = false;
                if (CanShowAssistStatus) SetFooter(error.Message, true);
                else RunLog.Warn("后台辅助", error.Message);
                return;
            }
            if (startupAssistOnly)
            {
                if (!current.WorldAssist.AllowsStartup(current.Source))
                {
                    assistEnabled = false;
                    return;
                }
                current.WorldAssist = current.WorldAssist.ForStartup();
            }
            bool startupBattle = startupAssistOnly && current.WorldAssist.StartsBattleAutomatically;
            var cancellation = new CancellationTokenSource();
            assistCancellation = cancellation;
            var report = new Progress<BattleReport>(value => RenderAssistReport(cancellation, value));
            stop.Enabled = true;
            if (CanShowAssistStatus)
                mode.Text = startupBattle ? "启动辅助和自动战斗" : "后台辅助";
            assistRun = RunAssist(current, report, cancellation, startupBattle);
        }

        private bool CanShowAssistStatus { get { return work == null && !preserveTaskResult; } }

        private void RenderAssistReport(CancellationTokenSource cancellation, BattleReport report)
        {
            if (CanShowAssistStatus && assistCancellation == cancellation && !cancellation.IsCancellationRequested)
                RenderReport(report);
        }

        private async Task RunAssist(CombatSettings current, IProgress<BattleReport> report,
            CancellationTokenSource cancellation, bool includeBattle)
        {
            await Task.Yield();
            try
            {
                await Task.Run(() => BattleLoop.Run(current, false, report, cancellation.Token, concertoMeter, !includeBattle));
            }
            catch (Exception error)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    assistEnabled = false;
                    RunLog.Error("后台辅助", "运行中断", error);
                    if (CanShowAssistStatus) SetFooter(FriendlyError(error), true);
                }
            }
            finally
            {
                assistCancellation = null;
                assistRun = null;
                cancellation.Dispose();
                if (work == null)
                {
                    stop.Enabled = false;
                    if (CanShowAssistStatus) mode.Text = "已停止";
                    if (closing) Close();
                }
            }
        }

        private async Task SuspendAssist()
        {
            Task running = assistRun;
            if (running == null) return;
            assistCancellation.Cancel();
            await running;
        }

        private async void BeginSecondSola()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            await StartWork(current, WorkMode.SecondSola);
        }

        private async void BeginDomain(DomainKind kind)
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            DomainOptions options = current.Domain.ForKind(kind);
            using (var dialog = new DomainForm(options))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                    return;
                current.Domain = dialog.Result;
            }
            await StartWork(current, WorkMode.Domain);
        }

        private async void BeginNightmare()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            using (var dialog = new NightmareForm(current.Nightmare ?? NightmareOptions.Defaults()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                    return;
                current.Nightmare = dialog.Result;
            }
            await StartWork(current, WorkMode.Nightmare);
        }

        private async void BeginMultiAccount(MultiAccountMode accountMode)
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            if (accountMode == MultiAccountMode.Config)
            {
                string folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs");
                using (var form = new AccountConfigForm(new AccountStore(folder), current))
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                        return;
                    current.MultiAccountExitAfterTask = form.ExitAfterTask;
                    if (!form.StartRequested)
                    {
                        settings = current;
                        SaveSettings();
                        if (persistenceError == null) SetFooter("已保存多账号配置。", false);
                        return;
                    }
                }
            }
            else using (var form = new MultiAccountRunForm(current.ExitAfterMultiAccount))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                current.MultiAccountExitAfterTask = form.ExitAfterTask;
                if (!form.StartRequested)
                {
                    settings = current;
                    SaveSettings();
                    if (persistenceError == null) SetFooter("已保存多账号日常设置。", false);
                    return;
                }
            }
            await StartWork(current, accountMode == MultiAccountMode.Config
                ? WorkMode.MultiConfig : WorkMode.MultiDaily);
        }

        private async void BeginDaily()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            using (var dialog = new DailyForm(current.Daily ?? DailyOptions.Defaults()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                    return;
                current.Daily = dialog.Result;
                string blocked = null;
                if (dialog.StartRequested && Array.IndexOf(current.Daily.GetTasks(), DailyTask.Farm4C) >= 0 &&
                    (current.Farm == null || !current.Farm.Teleport))
                    blocked = "「刷 4C 声骸」需要先在刷 4C 设置里开启自动传送。";
                if (!dialog.StartRequested || blocked != null)
                {
                    settings = current;
                    SaveSettings();
                    if (blocked != null)
                        RunLog.Warn("一条龙", "计划未运行。 " + blocked);
                    if (persistenceError == null)
                        SetFooter(blocked ?? "已保存一条龙执行计划。", blocked != null);
                    return;
                }
            }
            await StartWork(current, WorkMode.Daily);
        }

        private async void BeginChange()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            using (var dialog = new ChangeForm(current.Change ?? ChangeOptions.Defaults()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                    return;
                current.Change = dialog.Result;
            }
            await StartWork(current, WorkMode.Change);
        }

        private async void BeginMerge()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            await StartWork(current, WorkMode.Merge);
        }

        private async void BeginGarden()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            await StartWork(current, WorkMode.Garden);
        }

        private async void BeginEnhance()
        {
            if (work != null || closing)
                return;
            CombatSettings current;
            try { current = ReadControls(); }
            catch (InvalidDataException error)
            {
                SetFooter(error.Message, true);
                return;
            }
            using (var dialog = new EnhanceForm(current.Enhance ?? EnhanceOptions.Defaults()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                    return;
                current.Enhance = dialog.Result;
                if (!dialog.StartRequested)
                {
                    settings = current;
                    SaveSettings();
                    if (persistenceError == null)
                        SetFooter("已保存声骸强化条件。", false);
                    return;
                }
            }
            await StartWork(current, WorkMode.Enhance);
        }

        private async Task StartWork(CombatSettings current, WorkMode workMode, bool? commandLineExit = null)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            bool isPreview = workMode == WorkMode.Preview;
            bool waitForWindow = workMode != WorkMode.Battle && !isPreview;
            RunLog.Info("任务", "开始 " + workMode + "，游戏来源=" + current.Source);
            settings = current;
            if (!commandLineExit.HasValue)
                SaveSettings();
            // The command-line and elevation-resume paths must observe the
            // same foreground-only boundary as the interactive activity entry.
            if (workMode == WorkMode.Rahai && !SupportsRahaiSource(current.Source))
            {
                const string reason = "拉海洛方块需要 Windows 客户端，请使用本地客户端自动或前台模式。";
                RunLog.Warn("任务", workMode + " 未启动。 " + reason);
                SetFooter(reason, true);
                return;
            }
            if (waitForWindow && current.Source == GameSource.ManagedCloud && !SupportsManagedCloudTask(workMode))
            {
                const string reason = "专用后台云游戏目前支持改主属性；此任务请使用「网页云游戏 · 前台」。";
                RunLog.Warn("任务", workMode + " 未启动。 " + reason);
                if (persistenceError == null)
                    SetFooter(reason, true);
                return;
            }
            if (!isPreview && (settings.Source == GameSource.Foreground || settings.Source == GameSource.Background ||
                settings.Source == GameSource.AutoLocal) &&
                InputIntegrity.LocalGameNeedsElevation())
            {
                RunLog.Warn("任务", workMode + " 未启动，游戏进程需要更高输入权限");
                OfferAdministratorRestart(workMode, commandLineExit);
                return;
            }
            var cancellation = new CancellationTokenSource();
            preserveTaskResult = false;
            work = cancellation;
            inPreview = isPreview;
            hasReadout = false;
            ShowEnhancementView(workMode == WorkMode.Enhance);
            enhanceStatus.ShowResult(null);
            target.Text = character.Text = skills.Text = lastAction.Text = "—";
            SetRunning(true);
            mode.Text = ModeLabel(workMode);
            state.Text = ModeStartup(workMode);
            if (waitForWindow && persistenceError == null)
                SetFooter(string.Empty, false);
            var progress = new Progress<FarmReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderFarm(report);
            });
            var battleProgress = new Progress<BattleReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderReport(report);
            });
            var enhanceProgress = new Progress<EnhanceReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderEnhance(report);
            });
            var domainProgress = new Progress<DomainReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderDomain(report);
            });
            var nightmareProgress = new Progress<NightmareReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderNightmare(report);
            });
            var gardenProgress = new Progress<GardenReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderGarden(report);
            });
            var mergeProgress = new Progress<MergeReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderMerge(report);
            });
            var changeProgress = new Progress<ChangeReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderChange(report);
            });
            DailyReport dailyResult = null;
            IProgress<DailyReport> dailyProgressUi = new Progress<DailyReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderDaily(report);
            });
            var dailyProgress = new StepProgress<DailyReport>(report =>
            {
                if (report.Finished)
                    dailyResult = report;
                else
                    dailyProgressUi.Report(report);
            });
            var multiProgress = new Progress<MultiAccountReport>(report =>
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(work, cancellation))
                    RenderMulti(report);
            });
            bool needsElevation = false;
            bool completionVerified = true;
            string failure = null;
            FarmReport farmResult = null;
            EnhanceReport enhanceResult = null;
            try
            {
                await SuspendAssist();
                cancellation.Token.ThrowIfCancellationRequested();
                if (waitForWindow)
                {
                    if (current.Source == GameSource.ManagedCloud)
                        state.Text = "请在已开启的专用云游戏窗口中手动登录并打开已筛选背包，等待任务识别。";
                    else
                    {
                        GameSource detected = await WaitForTaskWindow(current.Source, cancellation.Token);
                        state.Text = detected == GameSource.Background ? "已识别游戏窗口，正在准备后台运行。" : ModeStartup(workMode);
                    }
                }
                switch (workMode)
                {
                    case WorkMode.Enhance:
                    {
                        var gate = new ManualResetEventSlim(false);
                        enhanceGate = gate;
                        try
                        {
                            enhanceResult = await Task.Run(() => EnhanceLoop.Run(settings, settings.Enhance, enhanceProgress,
                                cancellation.Token, gate));
                            failure = enhanceResult.Failure;
                            completionVerified = enhanceResult.Finished;
                        }
                        finally
                        {
                            enhanceGate = null;
                            gate.Dispose();
                        }
                        break;
                    }
                    case WorkMode.Domain:
                        failure = await Task.Run(delegate
                        {
                            using (var runner = new DomainRunner(settings, settings.Domain, cancellation.Token, domainProgress))
                            {
                                runner.Run();
                                completionVerified = runner.CompletionVerified;
                                return runner.Failure;
                            }
                        }, cancellation.Token);
                        break;
                    case WorkMode.Nightmare:
                        failure = await Task.Run(delegate
                        {
                            using (var runner = new NightmareNestRunner(settings, settings.Nightmare, cancellation.Token,
                                nightmareProgress))
                            {
                                runner.Run();
                                return runner.Failure ?? runner.CompletionIssue;
                            }
                        }, cancellation.Token);
                        break;
                    case WorkMode.Garden:
                        failure = await Task.Run(delegate
                        {
                            var evidence = new DailyEvidence(RunLog.DirectoryPath, standaloneGarden: true);
                            evidence.StartStep(DailyTask.Garden, 1);
                            using (var runner = new GardenRunner(settings, cancellation.Token, gardenProgress,
                                (stage, frame) => evidence.Save(stage, frame)))
                            {
                                runner.Run();
                                completionVerified = runner.CompletionVerified;
                                return runner.Failure;
                            }
                        });
                        break;
                    case WorkMode.Merge:
                        failure = await Task.Run(delegate
                        {
                            using (var runner = new MergeRunner(settings, cancellation.Token, mergeProgress))
                            {
                                runner.Run();
                                completionVerified = runner.CompletionVerified;
                                return runner.Failure;
                            }
                        });
                        break;
                    case WorkMode.Change:
                        failure = await Task.Run(delegate
                        {
                            using (var runner = new ChangeRunner(settings, settings.Change, cancellation.Token, changeProgress))
                            {
                                runner.Run();
                                return runner.Failure;
                            }
                        });
                        break;
                    case WorkMode.Rahai:
                        IProgress<string> rahaiProgress = new Progress<string>(message =>
                        {
                            if (ReferenceEquals(work, cancellation) && !cancellation.IsCancellationRequested)
                                state.Text = message;
                        });
                        await Task.Run(delegate
                        {
                            using (var runner = new RahaiRunner(settings, cancellation.Token, rahaiProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.Cultivation:
                        await Task.Run(delegate
                        {
                            using (var runner = new CultivationRunner(settings, cancellation.Token, domainProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.SecondSola:
                        IProgress<string> solaProgress = new Progress<string>(message =>
                        {
                            if (ReferenceEquals(work, cancellation) && !cancellation.IsCancellationRequested)
                                state.Text = message;
                        });
                        await Task.Run(delegate
                        {
                            using (var runner = new SecondSolaRunner(settings, cancellation.Token, solaProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.Daily:
                        failure = await Task.Run(delegate
                        {
                            using (var runner = new DailyRunner(settings, settings.Daily, cancellation.Token, dailyProgress))
                            {
                                runner.Run();
                                return runner.Failure;
                            }
                        });
                        completionVerified = dailyResult != null;
                        if (!completionVerified && failure == null && !cancellation.IsCancellationRequested)
                            failure = "一条龙未返回核验结果。";
                        break;
                    case WorkMode.MultiDaily:
                        failure = await Task.Run(delegate
                        {
                            using (var runner = new MultiAccountRunner(settings, MultiAccountMode.Sequential,
                                cancellation.Token, multiProgress))
                            {
                                runner.Run();
                                return runner.Failure;
                            }
                        });
                        break;
                    case WorkMode.MultiConfig:
                    {
                        using (var gate = new ManualResetEventSlim(false))
                        {
                            enhanceGate = gate;
                            try
                            {
                                failure = await Task.Run(delegate
                                {
                                    using (var runner = new MultiAccountRunner(settings, MultiAccountMode.Config,
                                        cancellation.Token, multiProgress, enhanceProgress, gate))
                                    {
                                        runner.Run();
                                        return runner.Failure;
                                    }
                                });
                            }
                            finally { enhanceGate = null; }
                        }
                        break;
                    }
                    case WorkMode.Farm:
                        farmResult = await Task.Run(() => FarmLoop.Run(settings, settings.Farm, progress, cancellation.Token, concertoMeter));
                        completionVerified = farmResult != null && farmResult.Finished;
                        if (farmResult != null && !cancellation.IsCancellationRequested)
                        {
                            RenderFarm(farmResult);
                            failure = farmResult.Failure;
                        }
                        break;
                    default:
                        await Task.Run(() => BattleLoop.Run(settings, isPreview, battleProgress, cancellation.Token, concertoMeter));
                        break;
                }
            }
            catch (InputElevationRequiredException)
            {
                needsElevation = true;
                RunLog.Warn("任务", workMode + " 因输入权限不足中断");
            }
            catch (OperationCanceledException error)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    RunLog.Error("任务", workMode + " 意外取消", error);
                    failure = FriendlyError(error);
                }
            }
            catch (Exception error)
            {
                RunLog.Error("任务", workMode + " 执行异常", error);
                failure = FriendlyError(error);
            }
            finally
            {
                string duration = "，耗时 " + elapsed.ElapsedMilliseconds + " ms";
                if (cancellation.IsCancellationRequested)
                    RunLog.Info("任务", workMode + " 已取消" + duration);
                else if (failure != null)
                    RunLog.Warn("任务", workMode + " 执行结束" + duration + "，失败原因=" + failure);
                else if (!needsElevation)
                    RunLog.Info("任务", workMode + " 执行结束" + duration);
                if (dailyResult != null && !cancellation.IsCancellationRequested)
                    RenderDaily(dailyResult);
                preserveTaskResult = waitForWindow;
                work = null;
                SetRunning(false);
                resume.Enabled = false;
                bool completed = !cancellation.IsCancellationRequested && !needsElevation && failure == null;
                mode.Text = completed && farmResult != null ? "刷取结束" :
                    completed && enhanceResult != null && enhanceResult.Finished ? "强化结束" :
                    enhanceResult != null && enhanceResult.Failure != null ? "强化失败" :
                    completed && (workMode == WorkMode.Daily || workMode == WorkMode.Rahai ||
                        workMode == WorkMode.Cultivation || workMode == WorkMode.SecondSola ||
                        workMode == WorkMode.MultiDaily || workMode == WorkMode.MultiConfig) ? "任务结束" : "已停止";
                if (cancellation.IsCancellationRequested)
                {
                    state.Text = "已停止任务。";
                    if (waitForWindow)
                    {
                        lastAction.Text = "已停止";
                        SetFooter(persistenceError ?? "可以重新选择并运行任务。", persistenceError != null);
                    }
                }
                else if (failure != null)
                {
                    state.Text = failure;
                    if (waitForWindow) lastAction.Text = "已停止";
                    SetFooter(failure, true);
                }
                else if (!waitForWindow)
                    state.Text = "从这里启动，或按全局快捷键。";
                if (closing)
                    Close();
            }
            bool exitAfter = ShouldExitAfterTask(settings, workMode, commandLineExit == true,
                completionVerified && !cancellation.IsCancellationRequested && !needsElevation && failure == null);
            cancellation.Dispose();
            if (exitAfter && !closing)
                ExitGameAndProgram();
            if (needsElevation && !closing) OfferAdministratorRestart(workMode, commandLineExit);
            else if (!exitAfter) BeginAssist();
        }

        internal static bool ShouldExitAfterTask(CombatSettings settings, WorkMode workMode, bool commandLineExit,
            bool completed)
        {
            return completed && ((workMode == WorkMode.Farm && settings.Farm != null && settings.Farm.ExitAfterTask) ||
                (commandLineExit && Program.ReferenceTaskName(workMode,
                    workMode == WorkMode.Domain && settings.Domain != null ? (DomainKind?)settings.Domain.Kind : null) != null) ||
                (workMode == WorkMode.Daily && settings.Daily != null && settings.Daily.ExitAfterTask) ||
                ((workMode == WorkMode.MultiDaily || workMode == WorkMode.MultiConfig) && settings.ExitAfterMultiAccount));
        }

        internal static bool SupportsManagedCloudTask(WorkMode mode)
        {
            // Only the inventory task that starts from the player's pre-opened
            // menu and needs coordinate clicks/Escape has a complete isolated
            // input implementation. Do not dispatch other runners onto it.
            return mode == WorkMode.Change;
        }

        internal static bool SupportsRahaiSource(GameSource source)
        {
            return source == GameSource.AutoLocal || source == GameSource.Foreground;
        }

        private async Task<GameSource> WaitForTaskWindow(GameSource source, CancellationToken token)
        {
            var window = GameWindow.ForTask(source);
            Rectangle bounds;
            string reason;
            string lastReason = null;
            while (!window.TryLocate(out bounds, out reason))
            {
                if (reason != lastReason)
                {
                    RunLog.Info("任务", "等待游戏窗口。 " + reason);
                    lastReason = reason;
                }
                state.Text = reason + "。识别到游戏窗口后自动开始。";
                await Task.Delay(150, token);
            }
            token.ThrowIfCancellationRequested();
            if (window.InputBlockedByIntegrity)
                throw new InputElevationRequiredException();
            RunLog.Info("任务", "游戏窗口已就绪，模式=" + window.Mode + "，分辨率=" + bounds.Width + "×" + bounds.Height);
            return window.Mode;
        }

        internal async void ResumeAfterElevation(WorkMode workMode)
        {
            if (work == null && !closing)
                await StartWork(settings, workMode);
        }

        internal async void StartTaskFromCommandLine(WorkMode workMode, DomainKind? domain, bool exitAfter)
        {
            if (work != null || closing)
                return;
            if (settingsLoadError != null)
            {
                string reason = ModeLabel(workMode) + "未启动，无法读取已保存的配置。 " + settingsLoadError;
                RunLog.Warn("任务", reason);
                SetFooter(reason, true);
                return;
            }
            CombatSettings current = settings;
            if (workMode == WorkMode.Domain && domain.HasValue)
            {
                current = AccountConfig.ForRun(settings);
                current.Domain = current.Domain.ForKind(domain.Value);
            }
            await StartWork(current, workMode, exitAfter);
        }

        private void OfferAdministratorRestart(WorkMode workMode, bool? commandLineExit)
        {
            const string reason = "《鸣潮》以更高权限运行，Windows 已阻止工具发送按键。";
            string task = workMode == WorkMode.Battle ? "自动战斗" : ModeLabel(workMode);
            mode.Text = "需要更高权限";
            state.Text = reason;
            SetFooter("重新启动「" + task + "」可再次请求权限。", true);
            if (!InputIntegrity.CanRequestAdministrator)
            {
                SetFooter("工具无法提升到当前游戏进程的权限。", true);
                return;
            }
            if (persistenceError != null)
            {
                SetFooter("保存任务设置失败。请以管理员身份打开工具并重新选择任务。", true);
                return;
            }
            if (MessageBox.Show(this, reason + "\n\n以管理员身份重启工具并继续「" + task + "」？", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                string arguments;
                if (commandLineExit.HasValue)
                {
                    string taskName = Program.ReferenceTaskName(workMode,
                        workMode == WorkMode.Domain && settings.Domain != null ? (DomainKind?)settings.Domain.Kind : null);
                    if (taskName == null) { SetFooter("无法恢复此命令行任务。", true); return; }
                    arguments = Program.ResumeCommandTask + " -t " + taskName +
                        (commandLineExit.Value ? " --exit" : string.Empty);
                }
                else arguments = Program.ResumeTask + " " + workMode;
                var restart = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, arguments)
                {
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                using (System.Diagnostics.Process elevated = System.Diagnostics.Process.Start(restart)) { }
                Close();
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                SetFooter(error.NativeErrorCode == 1223 ? "已取消管理员授权，任务未开始。" : FriendlyError(error), true);
            }
        }

        private static string ModeLabel(WorkMode workMode)
        {
            switch (workMode)
            {
                case WorkMode.Preview: return "仅检测";
                case WorkMode.Farm: return "刷4C";
                case WorkMode.Enhance: return "强化声骸";
                case WorkMode.Domain: return "体力本";
                case WorkMode.Nightmare: return "梦魇巢穴";
                case WorkMode.Garden: return "周常乐园";
                case WorkMode.Merge: return "合并声骸";
                case WorkMode.Change: return "改主属性";
                case WorkMode.Daily: return "一条龙";
                case WorkMode.MultiDaily: return "多账号日常";
                case WorkMode.MultiConfig: return "多账号配置";
                case WorkMode.Rahai: return "拉海洛方块";
                case WorkMode.Cultivation: return "培养目标";
                case WorkMode.SecondSola: return "第二索拉";
                default: return "已待命";
            }
        }

        private static string ModeStartup(WorkMode workMode)
        {
            switch (workMode)
            {
                case WorkMode.Farm: return "准备刷取 4C 声骸";
                case WorkMode.Enhance: return "准备强化声骸";
                case WorkMode.Domain: return "准备刷取体力本";
                case WorkMode.Nightmare: return "准备清理梦魇巢穴";
                case WorkMode.Garden: return "准备执行周常乐园";
                case WorkMode.Merge: return "准备合并弃置声骸";
                case WorkMode.Change: return "准备修改声骸主属性";
                case WorkMode.Daily: return "准备执行一条龙";
                case WorkMode.MultiDaily: return "准备切换账号执行日常";
                case WorkMode.MultiConfig: return "准备执行多账号配置任务";
                case WorkMode.Rahai: return "准备读取拉海洛方块关卡列表";
                case WorkMode.Cultivation: return "准备读取索拉指南里的培养目标";
                case WorkMode.SecondSola: return "准备识别「第二索拉：未名之梦」";
                default: return "正在查找游戏窗口";
            }
        }

        private void ExitGameAndProgram()
        {
            if (settings.Source == GameSource.ManagedCloud)
            {
                RunLog.Info("任务", "已结束专用后台云游戏任务；仅退出工具，云会话请手动关闭。");
                Application.Exit();
                return;
            }
            try
            {
                var window = new GameWindow(settings.Source);
                Rectangle bounds;
                string reason;
                if (window.TryLocate(out bounds, out reason) && window.Handle != IntPtr.Zero)
                {
                    Native.PostMessageW(window.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    System.Threading.Thread.Sleep(3000);
                }
            }
            catch (Exception) { }
            Application.Exit();
        }

        private void RenderMulti(MultiAccountReport report)
        {
            NotifyTask("多账号", report.Notice);
            ShowEnhancementView(false);
            resume.Enabled = report.Paused && enhanceGate != null && !report.Finished;
            mode.Text = report.Finished ? "任务结束" : "多账号";
            state.Text = report.State;
            target.Text = "已完成 " + report.Completed;
            skills.Text = report.DailyPoints.HasValue ? "积分 " + report.DailyPoints.Value : "—";
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("已完成 {0} 个账号 · {1}", report.Completed,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderDaily(DailyReport report)
        {
            NotifyTask("一条龙", report.Notice);
            mode.Text = report.Finished ? "任务结束" : "一条龙";
            state.Text = report.State;
            target.Text = "体力 " + report.UsedStamina + "/180";
            skills.Text = "积分 " + report.Points;
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("体力 {0}/180 · 积分 {1} · {2}", report.UsedStamina, report.Points,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderChange(ChangeReport report)
        {
            mode.Text = report.Finished ? "任务结束" : "改主属性";
            state.Text = report.State;
            target.Text = "已修改 " + report.Changed;
            skills.Text = "—";
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("已修改 {0} 个 · {1}", report.Changed,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderMerge(MergeReport report)
        {
            NotifyTask("合并声骸", report.Notice);
            mode.Text = report.Finished ? "任务结束" : "合并声骸";
            state.Text = report.State;
            target.Text = "已合成 " + report.Batches + " 批";
            skills.Text = "—";
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("已合成 {0} 批 · {1}", report.Batches,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderGarden(GardenReport report)
        {
            mode.Text = report.Finished ? "任务结束" : "周常乐园";
            state.Text = report.State;
            target.Text = "已确认 " + report.Actions;
            skills.Text = "—";
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("已确认 {0} 次操作 · {1}", report.Actions,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderNightmare(NightmareReport report)
        {
            mode.Text = report.Finished ? "任务结束" : "梦魇巢穴";
            state.Text = report.State;
            target.Text = "已处理 " + report.Nests;
            skills.Text = "—";
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("已处理 {0} 个巢穴 · {1}", report.Nests,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderDomain(DomainReport report)
        {
            mode.Text = report.Finished ? "任务结束" : "体力本";
            state.Text = report.State;
            target.Text = "轮次 " + report.Runs;
            skills.Text = "体力 " + report.UsedStamina;
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("轮次 {0} · 消耗体力 {1} · {2}", report.Runs, report.UsedStamina,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderEnhance(EnhanceReport report)
        {
            ShowEnhancementView(true);
            enhanceStatus.ShowResult(report);
            if (report.Paused && !resume.Enabled || report.Finished && report.Failure == null)
                NotifyTask("批量强化声骸", report.State);
            resume.Enabled = report.Paused;
            mode.Text = report.Failure != null ? "强化失败" : report.Finished ? "强化结束" :
                report.Paused ? "已暂停" : "强化声骸";
            state.Text = report.State;
            target.Text = report.Enhanced + " 只";
            skills.Text = report.Discarded + " 只";
            character.Text = report.ItemNumber == 0 ? "—" : "第 " + report.ItemNumber + " 只";
            lastAction.Text = report.Round == 0 ? "—" : "第 " + report.Round + " 档";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : string.Empty, false);
        }

        private void ShowEnhancementView(bool visible)
        {
            battleCards.Visible = !visible;
            enhanceStatus.Visible = visible;
            targetCaption.Text = visible ? "已锁定" : "目标";
            characterCaption.Text = visible ? "当前声骸" : "当前角色";
            skillsCaption.Text = visible ? "已弃置" : "技能状态";
            actionCaption.Text = visible ? "强化档位" : "最近动作";
        }

        private void NotifyTask(string title, string message)
        {
            if (closing || string.IsNullOrEmpty(message))
                return;
            try
            {
                if (taskNotice == null)
                    taskNotice = new NotifyIcon { Icon = SystemIcons.Information, Text = Text, Visible = true };
                taskNotice.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
            }
            catch (Exception error)
            {
                RunLog.Warn(title, "无法显示任务通知 · " + error.Message);
            }
        }

        private void RenderFarm(FarmReport report)
        {
            mode.Text = report.Finished ? "刷取结束" : "刷4C";
            state.Text = report.State;
            if (!string.IsNullOrEmpty(report.LastAction))
                lastAction.Text = report.LastAction;
            target.Text = report.Attempts > 0 ? "已尝试 " + report.Attempts : "—";
            skills.Text = string.Format("声骸 {0} · {1:F1} 个/小时", report.Echoes, report.Rate);
            if (report.Finished && report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("尝试 {0} · 击杀 {1} · 声骸 {2} · {3:F1} 个/小时 · {4}", report.Attempts, report.Kills, report.Echoes, report.Rate,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderReport(BattleReport report)
        {
            NotifyTask(report.NoticeTitle ?? "登录", report.Notice);
            HudSnapshot hud = report.Hud;
            mode.Text = inPreview ? "仅检测" : report.State == "战斗中" ? "运行中" : "已待命";
            state.Text = report.DetectedMode.HasValue ?
                (report.DetectedMode == GameSource.Background ? "后台 · " : "前台 · ") + report.State : report.State;
            lastAction.Text = report.LastAction;
            if (hud == null || !hud.Valid)
            {
                string status = report.Size.Width > 0
                    ? string.Format("{0}×{1}  ·  {2}", report.Size.Width, report.Size.Height, report.State)
                    : report.State;
                if (hasReadout)
                    status += "  ·  显示上次识别结果";
                SetFooter(persistenceError ?? status, persistenceError != null);
                return;
            }

            hasReadout = true;
            target.Text = hud.Locked ? "已锁定" : hud.EnemyHealth ? "发现血条" : "等待敌人";
            target.ForeColor = hud.Locked ? Theme.Accent : Theme.Ink;
            CharacterProfile profile = hud.Profiles[hud.Current];
            CharacterKit kit = CharacterRoster.Find(hud.RoverForm ?? hud.Fighters[hud.Current]);
            character.Text = (hud.Current + 1) + " 号位  " +
                (kit != null ? kit.Name : profile != null ? profile.Name : Theme.Roles[(int)settings.Slots[hud.Current].Role]);
            skills.Text = FormatSkill(hud, Skill.Resonance) + "   " + FormatSkill(hud, Skill.Echo) +
                "   " + FormatSkill(hud, Skill.Liberation);
            SetFooter(persistenceError ?? string.Format("{0}×{1}  ·  {2}  ·  协奏{3}",
                report.Size.Width, report.Size.Height, hud.Detail, hud.ConcertoFull ? "已满" : "未满"),
                persistenceError != null);
        }

        private static string FormatSkill(HudSnapshot hud, Skill skill)
        {
            int index = (int)skill;
            if (hud.Keys[index] == '\0')
                return "？ 键未知";
            switch (hud.Skills[index])
            {
                case SkillState.Ready: return hud.Keys[index] + " 就绪";
                case SkillState.Cooling: return hud.Keys[index] + " 冷却";
                default: return hud.Keys[index] + " 未就绪";
            }
        }

        private void OpenProfiles()
        {
            try
            {
                CombatSettings current = ReadControls();
                using (var dialog = new ProfileForm(current.Characters, current.Source))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;
                    current.Characters = dialog.Result;
                    settings = current;
                    SaveSettings();
                    if (persistenceError == null)
                        SetFooter("已保存 " + settings.Characters.Count + " 个角色档案。", false);
                }
            }
            catch (InvalidDataException error) { SetFooter(error.Message, true); }
        }

        private void OpenVariants()
        {
            try
            {
                CombatSettings current = ReadControls();
                using (var dialog = new VariantsForm(current))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    settings = current;
                    SaveSettings();
                    if (persistenceError == null) SetFooter("已保存角色变体设置。", false);
                }
            }
            catch (InvalidDataException error) { SetFooter(error.Message, true); }
        }

        private void SaveSettings()
        {
            persistenceError = null;
            try
            {
                SettingsFile.Save(SettingsPath, settings);
                RunLog.Info("设置", "设置已保存");
            }
            catch (Exception error)
            {
                RunLog.Error("设置", "保存设置失败", error);
                persistenceError = "本次设置只能在当前会话使用。 " + FriendlyError(error);
                SetFooter(persistenceError, true);
            }
        }

        private void SetRunning(bool running)
        {
            start.Enabled = runTask.Enabled = taskSelect.Enabled = preview.Enabled = preferences.Enabled =
                team.Enabled = !running;
            stop.Enabled = running;
        }

        private void EndRun()
        {
            assistEnabled = false;
            if (assistCancellation != null)
                assistCancellation.Cancel();
            if (work == null || work.IsCancellationRequested)
                return;
            RunLog.Info("任务", closing ? "关闭窗口，正在取消当前任务" : "收到停止请求");
            work.Cancel();
            mode.Text = "正在停止";
            stop.Enabled = false;
        }

        private void OpenLogFolder()
        {
            try
            {
                string directory = RunLog.DirectoryPath;
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    SetFooter("暂无日志目录。 " + RunLog.Failure, true);
                    return;
                }
                RunLog.Info("程序", "打开日志目录");
                var open = new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true };
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(open)) { }
                if (RunLog.Failure != null)
                    SetFooter("本次日志写入已中断。 " + RunLog.Failure, true);
            }
            catch (Exception error)
            {
                RunLog.Error("程序", "打开日志目录失败", error);
                SetFooter("无法打开日志目录。 " + FriendlyError(error), true);
            }
        }

        private void SetFooter(string text, bool warning)
        {
            footer.Text = text;
            footer.ForeColor = warning ? Theme.Warning : Theme.Muted;
        }

        private static string FriendlyError(Exception error)
        {
            return string.IsNullOrWhiteSpace(error.Message) ? "发生错误。" : error.Message;
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            if (!enableGlobalHotkey)
                return;
            toggleInstalled = Native.RegisterHotKey(Handle, HotkeyId, 0x4000 | 0x0002 | 0x0001, (uint)Keys.F8);
            if (!toggleInstalled)
            {
                RunLog.Warn("程序", "注册 Ctrl+Alt+F8 失败，可以用窗口按钮启动和停止");
                hotkey.Text = "快捷键被占用，可用窗口按钮启动和停止";
            }
        }

        protected override void OnHandleDestroyed(EventArgs args)
        {
            if (toggleInstalled)
                Native.UnregisterHotKey(Handle, HotkeyId);
            toggleInstalled = false;
            base.OnHandleDestroyed(args);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == HotkeyMessage && message.WParam.ToInt32() == HotkeyId && Enabled)
            {
                RunLog.Info("程序", "收到快捷键 Ctrl+Alt+F8");
                if (work == null)
                    BeginRun(false);
                else
                    EndRun();
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnFormClosed(FormClosedEventArgs args)
        {
            if (taskNotice != null)
                taskNotice.Dispose();
            base.OnFormClosed(args);
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (work != null || assistRun != null)
            {
                closing = true;
                args.Cancel = true;
                EndRun();
            }
            base.OnFormClosing(args);
        }
    }
}
