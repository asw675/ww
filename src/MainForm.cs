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
        private readonly ComboBox taskSelect = Theme.Select("刷 4C 声骸", "批量强化声骸", "无音区", "材料本", "模拟领域", "梦魇巢穴", "周常乐园", "合并声骸", "改主属性");
        private readonly Button runTask = Theme.Button("运行任务", true);
        private readonly Button preview = Theme.Button("仅检测");
        private readonly Button stop = Theme.Button("停止");
        private readonly Button profiles = Theme.Button("角色档案");
        private readonly Button variants = Theme.Button("角色变体");
        private readonly Label hotkey = Theme.Text("Ctrl + Alt + F8  启动 / 停止", Theme.Muted);
        private readonly Label mode = Theme.Text("已停止", Theme.Accent, true);
        private readonly Label state = Theme.Text("切回游戏后按快捷键，或从这里启动。", Theme.Ink);
        private readonly Label target = Theme.Text("—", Theme.Ink, true);
        private readonly Label character = Theme.Text("—", Theme.Ink, true);
        private readonly Label skills = Theme.Text("—", Theme.Ink, true);
        private readonly Label lastAction = Theme.Text("—", Theme.Ink, true);
        private readonly Label footer = Theme.Text("建议使用 1920×1080 游戏画面", Theme.Muted);
        private readonly CheckBox autoTarget = Option("看见敌人血条后自动锁定");
        private readonly CheckBox autoRevive = Option("角色倒下后自动点击复活");
        private readonly CheckBox useLiberation = Option("大世界使用共鸣解放");
        private readonly CheckBox checkLevitator = Option("根据控物图标判断浮空");
        private readonly CheckBox rotate = Option("自动切换队伍角色");
        private readonly CheckBox concerto = Option("切人前优先等协奏充满");
        private readonly CheckBox lowHealth = Option("低血量时优先切治疗");
        private readonly CheckBox supportOpener = Option("战斗前后切到治疗");
        private readonly CheckBox detectKeys = Option("从技能栏识别绑定");
        private readonly ComboBox source = Theme.Select("本地客户端 · 前台", "本地客户端 · 后台", "网页云游戏 · 前台", "网页云游戏 · 后台");
        private readonly Button cloudPage = Theme.Button("打开官网云游戏");
        private readonly TextBox[] keys = { KeyField(), KeyField(), KeyField(), KeyField() };
        private readonly ComboBox[] roles = new ComboBox[3];
        private readonly ComboBox[] fighters = new ComboBox[3];
        private readonly NumericUpDown[] seconds = new NumericUpDown[3];
        private Panel preferences;
        private Panel team;
        private CombatSettings settings;
        private readonly ConcertoMeter concertoMeter;
        private CancellationTokenSource work;
        private enum WorkMode { Battle, Preview, Farm, Enhance, Domain, Nightmare, Garden, Merge, Change }

        private bool toggleInstalled, closing, inPreview, hasReadout;
        private string persistenceError;

        internal MainForm() : this(SettingsFile.Location) { }

        internal MainForm(string settingsPath)
        {
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

            try { settings = SettingsFile.Load(SettingsPath); }
            catch (Exception error)
            {
                settings = CombatSettings.Defaults();
                footer.Text = "配置读取失败，已载入默认值。 " + FriendlyError(error);
                footer.ForeColor = Theme.Warning;
            }
            LoadControls();
            start.Click += delegate { BeginRun(false); };
            runTask.Click += delegate { BeginTask(); };
            preview.Click += delegate { BeginRun(true); };
            stop.Click += delegate { EndRun(); };
            profiles.Click += delegate { OpenProfiles(); };
            variants.Click += delegate { OpenVariants(); };
            cloudPage.Click += delegate
            {
                try
                {
                    if (source.SelectedIndex == (int)GameSource.ManagedCloud) CloudBrowser.Open();
                    else System.Diagnostics.Process.Start(CloudBrowser.OfficialPage);
                }
                catch (Exception ex) { SetFooter(FriendlyError(ex), true); }
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
            actions.Controls.Add(start);
            actions.Controls.Add(taskSelect);
            actions.Controls.Add(runTask);
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
            metrics.Controls.Add(Metric("目标", target), 0, 0);
            metrics.Controls.Add(Metric("当前角色", character), 1, 0);
            metrics.Controls.Add(Metric("技能状态", skills), 2, 0);
            metrics.Controls.Add(Metric("最近动作", lastAction), 3, 0);
            report.Controls.Add(metrics, 0, 1);
            layout.Controls.Add(report, 0, 1);

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
            preferences = BuildPreferences();
            team = BuildTeam();
            columns.Controls.Add(preferences, 0, 0);
            columns.Controls.Add(team, 1, 0);
            layout.Controls.Add(columns, 0, 2);

            footer.AutoSize = false;
            footer.AutoEllipsis = true;
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            footer.Height = 22;
            layout.Controls.Add(footer, 0, 3);
        }

        private static Control Metric(string name, Label value)
        {
            var column = Theme.Row();
            column.FlowDirection = FlowDirection.TopDown;
            column.Controls.Add(Theme.Text(name, Theme.Muted));
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
            source.SelectedIndexChanged += delegate { cloudPage.Visible = source.SelectedIndex >= (int)GameSource.Cloud; };
            card.Controls.Add(autoTarget);
            card.Controls.Add(autoRevive);
            card.Controls.Add(useLiberation);
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
            card.Controls.Add(detectKeys);
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

        private void LoadControls()
        {
            autoTarget.Checked = settings.AutoTarget;
            autoRevive.Checked = settings.AutoRevive;
            useLiberation.Checked = settings.UseLiberation;
            rotate.Checked = settings.RotateParty;
            concerto.Checked = settings.PreferConcerto;
            lowHealth.Checked = settings.SwitchAtLowHealth;
            supportOpener.Checked = settings.SupportOpener;
            concerto.Enabled = rotate.Checked;
            lowHealth.Enabled = rotate.Checked;
            supportOpener.Enabled = rotate.Checked;
            detectKeys.Checked = settings.DetectSkillKeys;
            checkLevitator.Checked = settings.CheckLevitator;
            source.SelectedIndex = (int)settings.Source;
            keys[0].Text = settings.ResonanceKey;
            keys[1].Text = settings.EchoKey;
            keys[2].Text = settings.LiberationKey;
            keys[3].Text = settings.ToolKey;
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
                UseLiberation = useLiberation.Checked,
                RotateParty = rotate.Checked, PreferConcerto = concerto.Checked, SwitchAtLowHealth = lowHealth.Checked,
                SupportOpener = supportOpener.Checked, IunoC6 = settings.IunoC6, RocciaC6 = settings.RocciaC6,
                Source = (GameSource)source.SelectedIndex, CheckLevitator = checkLevitator.Checked,
                ChisaDps = settings.ChisaDps, SuomingDps = settings.SuomingDps, Farm = settings.Farm,
                Enhance = settings.Enhance, Domain = settings.Domain, Nightmare = settings.Nightmare,
                Change = settings.Change, PartyCharacters = new string[3],
                DetectSkillKeys = detectKeys.Checked, ResonanceKey = keys[0].Text, EchoKey = keys[1].Text,
                LiberationKey = keys[2].Text, ToolKey = keys[3].Text, Slots = new Rotation[3], Characters = settings.Characters
            };
            for (int index = 0; index < roles.Length; index++)
            {
                current.Slots[index] = Rotation.ForRole((BattleRole)roles[index].SelectedIndex);
                current.Slots[index].FieldSeconds = (double)seconds[index].Value;
                CharacterKit kit = fighters[index].SelectedItem as CharacterKit;
                current.PartyCharacters[index] = kit == null ? null : kit.Id;
            }
            current.Validate();
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
            if (current.Source == GameSource.Background || current.Source == GameSource.ManagedCloud)
            {
                SetFooter("4C 刷取需要「前台」来源：本地客户端·前台或网页云游戏·前台。", true);
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
                default: BeginChange(); break;
            }
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
            if (current.Source == GameSource.Background || current.Source == GameSource.ManagedCloud)
            {
                SetFooter("体力本需要「前台」来源：本地客户端·前台或网页云游戏·前台。", true);
                return;
            }
            DomainOptions options = current.Domain ?? DomainOptions.Defaults();
            options.Kind = kind;
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
            if (current.Source == GameSource.Background || current.Source == GameSource.ManagedCloud)
            {
                SetFooter("梦魇巢穴需要「前台」来源：本地客户端·前台或网页云游戏·前台。", true);
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
            if (current.Source == GameSource.Background || current.Source == GameSource.ManagedCloud)
            {
                SetFooter("改主属性需要「前台」来源：本地客户端·前台或网页云游戏·前台。", true);
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
            if (current.Source == GameSource.Background || current.Source == GameSource.ManagedCloud)
            {
                SetFooter("合并声骸需要「前台」来源：本地客户端·前台或网页云游戏·前台。", true);
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
            if (current.Source == GameSource.Background || current.Source == GameSource.ManagedCloud)
            {
                SetFooter("周常乐园需要「前台」来源：本地客户端·前台或网页云游戏·前台。", true);
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
            if (current.Source == GameSource.Background || current.Source == GameSource.ManagedCloud)
            {
                SetFooter("强化声骸需要「前台」来源：本地客户端·前台或网页云游戏·前台。", true);
                return;
            }
            using (var dialog = new EnhanceForm(current.Enhance ?? EnhanceOptions.Defaults()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                    return;
                current.Enhance = dialog.Result;
            }
            await StartWork(current, WorkMode.Enhance);
        }

        private async Task StartWork(CombatSettings current, WorkMode workMode)
        {
            bool isPreview = workMode == WorkMode.Preview;
            settings = current;
            SaveSettings();
            if (!isPreview && (settings.Source == GameSource.Foreground || settings.Source == GameSource.Background) &&
                InputIntegrity.LocalGameNeedsElevation())
            {
                OfferAdministratorRestart();
                return;
            }
            var cancellation = new CancellationTokenSource();
            work = cancellation;
            inPreview = isPreview;
            hasReadout = false;
            target.Text = character.Text = skills.Text = lastAction.Text = "—";
            SetRunning(true);
            mode.Text = ModeLabel(workMode);
            state.Text = ModeStartup(workMode);
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
            bool needsElevation = false;
            string failure = null;
            try
            {
                switch (workMode)
                {
                    case WorkMode.Enhance:
                        await Task.Run(() => EnhanceLoop.Run(settings, settings.Enhance, enhanceProgress, cancellation.Token));
                        break;
                    case WorkMode.Domain:
                        await Task.Run(delegate
                        {
                            using (var runner = new DomainRunner(settings, settings.Domain, cancellation.Token, domainProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.Nightmare:
                        await Task.Run(delegate
                        {
                            using (var runner = new NightmareNestRunner(settings, settings.Nightmare, cancellation.Token,
                                nightmareProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.Garden:
                        await Task.Run(delegate
                        {
                            using (var runner = new GardenRunner(settings, cancellation.Token, gardenProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.Merge:
                        await Task.Run(delegate
                        {
                            using (var runner = new MergeRunner(settings, cancellation.Token, mergeProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.Change:
                        await Task.Run(delegate
                        {
                            using (var runner = new ChangeRunner(settings, settings.Change, cancellation.Token, changeProgress))
                                runner.Run();
                        });
                        break;
                    case WorkMode.Farm:
                        await Task.Run(() => FarmLoop.Run(settings, settings.Farm, progress, cancellation.Token, concertoMeter));
                        break;
                    default:
                        await Task.Run(() => BattleLoop.Run(settings, isPreview, battleProgress, cancellation.Token, concertoMeter));
                        break;
                }
            }
            catch (InputElevationRequiredException)
            {
                needsElevation = true;
            }
            catch (Exception error)
            {
                failure = FriendlyError(error);
            }
            finally
            {
                work = null;
                cancellation.Dispose();
                SetRunning(false);
                mode.Text = "已停止";
                state.Text = failure == null ? "切回游戏后按快捷键，或从这里启动。" : "任务已停止，见下方错误信息。";
                if (failure != null) SetFooter(failure, true);
                if (closing)
                    Close();
            }
            if (needsElevation && !closing) OfferAdministratorRestart();
        }

        internal void ResumeAfterElevation() { BeginRun(false); }

        private void OfferAdministratorRestart()
        {
            const string reason = "《鸣潮》以更高权限运行，Windows 已阻止工具发送战斗按键。";
            mode.Text = "需要更高权限";
            state.Text = reason;
            SetFooter("点击「启动战斗」可重新请求权限。", true);
            if (!InputIntegrity.CanRequestAdministrator)
            {
                SetFooter("工具无法提升到当前游戏进程的权限。", true);
                return;
            }
            if (MessageBox.Show(this, reason + "\n\n以管理员身份重启工具并继续战斗待命？", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                var restart = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, Program.ResumeBattle)
                {
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                using (System.Diagnostics.Process elevated = System.Diagnostics.Process.Start(restart)) { }
                Close();
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                SetFooter(error.NativeErrorCode == 1223 ? "已取消管理员授权，暂未开始战斗。" : FriendlyError(error), true);
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
                case WorkMode.Preview: return "等待切回《鸣潮》客户端";
                default: return "等待切回《鸣潮》客户端";
            }
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
            target.Text = "操作 " + report.Actions;
            skills.Text = "—";
            lastAction.Text = report.Finished ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("已执行 {0} 次操作 · {1}", report.Actions,
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
            mode.Text = report.Finished ? "强化结束" : report.Paused ? "已暂停" : "强化声骸";
            state.Text = report.State;
            target.Text = "成功 " + report.Enhanced;
            skills.Text = "失败 " + report.Discarded;
            character.Text = "—";
            lastAction.Text = report.Finished || report.Paused ? "已停止" : "运行中";
            if (report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("成功 {0} · 失败 {1} · {2}", report.Enhanced, report.Discarded,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderFarm(FarmReport report)
        {
            mode.Text = report.Finished ? "刷取结束" : "刷4C";
            state.Text = report.State;
            if (!string.IsNullOrEmpty(report.LastAction))
                lastAction.Text = report.LastAction;
            target.Text = report.Kills > 0 ? "已刷 " + report.Kills : "—";
            skills.Text = string.Format("声骸 {0} · {1:F1} 个/小时", report.Echoes, report.Rate);
            if (report.Finished && report.Failure != null)
                SetFooter(report.Failure, true);
            else
                SetFooter(string.Format("击杀 {0} · 声骸 {1} · {2:F1} 个/小时 · {3}", report.Kills, report.Echoes, report.Rate,
                    report.Size.Width > 0 ? report.Size.Width + "×" + report.Size.Height : "—"), false);
        }

        private void RenderReport(BattleReport report)
        {
            HudSnapshot hud = report.Hud;
            mode.Text = inPreview ? "仅检测" : report.State == "战斗中" ? "运行中" : "已待命";
            state.Text = report.State;
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
                using (var dialog = new ProfileForm(current.Characters))
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
            try { SettingsFile.Save(SettingsPath, settings); }
            catch (Exception error)
            {
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
            if (work == null)
                return;
            work.Cancel();
            mode.Text = "正在停止";
            stop.Enabled = false;
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
            toggleInstalled = Native.RegisterHotKey(Handle, HotkeyId, 0x4000 | 0x0002 | 0x0001, (uint)Keys.F8);
            if (!toggleInstalled)
                hotkey.Text = "快捷键被占用，可用窗口按钮启动和停止";
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
                if (work == null)
                    BeginRun(false);
                else
                    EndRun();
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (work != null)
            {
                closing = true;
                args.Cancel = true;
                EndRun();
            }
            base.OnFormClosing(args);
        }
    }
}
