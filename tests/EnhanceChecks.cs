using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace TideCombat
{
    internal static class EnhanceChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        private static EchoStat Stat(string name, double value)
        {
            return new EchoStat { Name = name, Value = value, Y = 0 };
        }

        private static bool Pass(EnhanceOptions options, params EchoStat[] stats)
        {
            string reason;
            return EchoRules.Evaluate(new List<EchoStat>(stats), options, out reason);
        }

        internal static void Rules()
        {
            var options = EnhanceOptions.Defaults();

            Require(Pass(options, Stat("暴击", 7.5), Stat("暴击伤害", 14)), "双爆达标应通过");
            Require(!Pass(options, Stat("暴击", 5.0), Stat("暴击伤害", 21)), "首条暴击不足应丢弃");
            Require(!Pass(options, Stat("暴击伤害", 13.0), Stat("暴击", 7.0)), "首条爆伤按一半计算不足应丢弃");
            Require(!Pass(options, Stat("暴击", 6.0), Stat("暴击伤害", 12)), "双爆总分不足应丢弃");
            Require(!Pass(options, Stat("暴击", 7.0), Stat("攻击百分比", 10), Stat("防御", 10),
                Stat("生命", 10), Stat("防御百分比", 10)), "剩余孔位不足双爆应丢弃");
            Require(!Pass(options, Stat("防御", 10), Stat("暴击", 7.0)), "双爆前出现无效词条应丢弃");
            Require(Pass(options, Stat("暴击", 7.0), Stat("防御", 10), Stat("攻击百分比", 9)), "有效词条数量达标应通过");
            Require(!Pass(options, Stat("防御", 10)), "首条无效应丢弃");

            var relaxed = EnhanceOptions.Defaults();
            relaxed.RequireBothCrit = false;
            relaxed.RequireValidBeforeCrit = false;
            relaxed.ValidCountMinimum = 1;
            relaxed.FirstMustBeValid = false;
            Require(Pass(relaxed, Stat("防御", 10)), "放宽条件后单条防御应通过");
        }

        internal static void FirstCheckedCrit()
        {
            var options = EnhanceOptions.Defaults();
            options.ValidStats = new[] { "暴击", "攻击百分比" };
            options.ValidCountMinimum = 1;
            options.CritTotalMinimum = 10;
            options.RequireValidBeforeCrit = false;
            bool pass = Pass(options, Stat("暴击伤害", 12.0), Stat("暴击", 7.0));
            Require(pass, "首条检查应取第一个有效暴击类型，而不是首个暴击词条");
        }

        internal static void Parsing()
        {
            var lines = new List<OcrLine>
            {
                new OcrLine { Text = "暴击", Box = new Rectangle(100, 300, 40, 20) },
                new OcrLine { Text = "7.5%", Box = new Rectangle(200, 302, 40, 20) },
                new OcrLine { Text = "攻击", Box = new Rectangle(100, 340, 40, 20) },
                new OcrLine { Text = "30", Box = new Rectangle(200, 341, 30, 20) },
                new OcrLine { Text = "辅音", Box = new Rectangle(100, 380, 40, 20) },
                new OcrLine { Text = "加成详情", Box = new Rectangle(100, 420, 80, 20) },
                new OcrLine { Text = "999", Box = new Rectangle(200, 420, 30, 20) }
            };
            List<EchoStat> stats = EchoRules.Parse(lines);
            Require(stats.Count == 2, "应解析出两条词条");
            Require(stats[0].Name == "暴击" && Math.Abs(stats[0].Value - 7.5) < 1e-6, "暴击数值解析错误");
            Require(stats[1].Name == "攻击" && Math.Abs(stats[1].Value - 30) < 1e-6, "攻击数值解析错误");
            bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            var traditional = Rows(windows ? "暴擊傷害" : "暴击伤害", "１４．８％",
                windows ? "共鳴效率" : "共鸣效率", "８．４％");
            stats = EchoRules.Parse(traditional.Lines);
            Require(stats.Count == 2 && stats[0].Name == "暴击伤害" && stats[0].Value == 14.8 &&
                stats[1].Name == "共鸣效率", "繁体词条与全角数值应按简体规则判定");
            traditional.Lines.RemoveAt(1);
            Require(EchoRules.Parse(traditional.Lines).Count == 0, "缺数值时不能借用下一行的数值");

            var trailingLine = Rows("+ 生命", "430-", "+ 暴击伤害", "17.4%");
            stats = EchoRules.Parse(trailingLine.Lines);
            Require(stats.Count == 2 && stats[0].Name == "生命" && stats[0].Value == 430 &&
                stats[0].DisplayValue == "430" && stats[1].Name == "暴击伤害" && stats[1].Value == 17.4,
                "末尾误读为横线时仍能读齐该档词条");
            trailingLine.Lines[1].Text = "-430";
            Require(EchoRules.Parse(trailingLine.Lines).Count == 0, "不能把带负号的值当作正数");
            trailingLine.Lines[1].Text = "430-50";
            Require(EchoRules.Parse(trailingLine.Lines).Count == 0, "不能截断含多个数值的 OCR");
        }

        internal static void NumberNoise()
        {
            var captured = Rows("暴击", "8.7%", "+ 共鸣效率", ".10.0%", "重击伤害加成", "7.1%");
            List<EchoStat> stats = EchoRules.Parse(captured.Lines);
            Require(stats.Count == 3 && stats[1].Name == "共鸣效率" &&
                stats[1].DisplayValue == "10.0%" && stats[1].Value == 10 && stats[2].Value == 7.1,
                "旧词条数值的前导杂点不能让已经出现的第三条漏计");

            captured.Lines[3].Text = "． １０．０ ％ –";
            Require(EchoRules.Parse(captured.Lines).Count == 3, "全角数字与两端杂点应保留完整数值");
            foreach (string ambiguous in new[] { ".10%", "1.0.0%", "10.0%%", "10.0% 7.1%", "10.0%50" })
            {
                captured.Lines[3].Text = ambiguous;
                Require(EchoRules.Parse(captured.Lines).Count == 0, "数值有歧义时应等待重新识别 " + ambiguous);
            }
        }

        internal static void SettingsRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "TideCombat-enhance-" + Guid.NewGuid() + ".json");
            var settings = CombatSettings.Defaults();
            settings.Enhance.RequireBothCrit = false;
            settings.Enhance.PauseAfterSuccess = false;
            settings.Enhance.CritTotalMinimum = 22.2;
            settings.Enhance.FirstCritMinimum = 4.7;
            settings.Enhance.ValidCountMinimum = 0;
            settings.Enhance.ValidStats = new string[0];
            try
            {
                SettingsFile.Save(path, settings);
                EnhanceOptions saved = SettingsFile.Load(path).Enhance;
                Require(!settings.Enhance.PauseAfterSuccess && !saved.PauseAfterSuccess && !saved.RequireBothCrit &&
                    saved.CritTotalMinimum == 22.2 && saved.FirstCritMinimum == 4.7 &&
                    saved.ValidCountMinimum == 0 && saved.ValidStats.Length == 0,
                    "任务启动前的保存必须保留自定义条件和空列表");
            }
            finally { File.Delete(path); }

            bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            var config = new Dictionary<string, object>
            {
                { "首条双爆>=", 8.7 }, { "Pause after Success", true },
                { "有效词条", new object[] { windows ? "暴擊傷害" : "暴击伤害", windows ? "共鳴效率" : "共鸣效率" } }
            };
            EnhanceOptions account = AccountConfig.EnhanceForAccount(settings.Enhance, config);
            Require(account.FirstCritMinimum == 8.7 && account.PauseAfterSuccess &&
                account.ValidStats.Length == 2 && account.ValidStats[0] == "暴击伤害",
                "账号配置应读入 okww 自定义强化条件");
            account.CritTotalMinimum = 19.8;
            account.ValidStats = new string[0];
            AccountConfig.WriteEnhance(config, account);
            CombatSettings before = settings;
            settings = AccountConfig.ForRun(before);
            AccountConfig.Apply(settings, config);
            Require(settings.Enhance.CritTotalMinimum == 19.8 && settings.Enhance.ValidStats.Length == 0,
                "本地账号覆盖必须优先，并支持数组");
            settings = before;
            Require(settings.Enhance.CritTotalMinimum == 22.2, "执行账号任务后必须恢复全局强化条件");
        }

        internal static void SettingsUi()
        {
            var options = EnhanceOptions.Defaults();
            options.CritTotalMinimum = 22.2;
            options.PauseAfterSuccess = false;
            options.ValidStats = new[] { "防御" };
            using (var dialog = new EnhanceForm(options))
            {
                var controls = Descendants(dialog).ToList();
                CheckedListBox list = controls.OfType<CheckedListBox>().Single();
                Require(controls.OfType<CheckBox>().Count() == 4 && controls.OfType<NumericUpDown>().Count() == 3 &&
                    list.Items.Count == 13 && list.CheckedItems.Count == 1 &&
                    (string)list.CheckedItems[0] == "防御", "八个配置项和 13 个词条选项应与配置一致");
                Click(controls, "仅保存");
                Require(dialog.Result.CritTotalMinimum == 22.2 && !dialog.Result.PauseAfterSuccess &&
                    !dialog.StartRequested, "仅保存必须保留自定义配置并与开始分开");
            }
            using (var dialog = new EnhanceForm(options))
            {
                var controls = Descendants(dialog).ToList();
                Click(controls, "恢复默认");
                Click(controls, "开始强化");
                Require(dialog.StartRequested && dialog.Result.CritTotalMinimum == 13.8 &&
                    dialog.Result.PauseAfterSuccess && dialog.Result.ValidStats.Contains("攻击百分比") &&
                    dialog.Result.ValidStats.Length == 3, "恢复默认后启动应提交完整默认值");
            }
            using (var dialog = new EnhanceForm(options, saveOnly: true))
            {
                Click(Descendants(dialog), "保存");
                Require(dialog.Result != null && !dialog.StartRequested, "账号配置只能保存强化参数");
            }
        }

        internal static void ProgressUi()
        {
            var options = EnhanceOptions.Defaults();
            List<EchoStat> stats = EchoRules.Parse(Rows("暴击", "7.5%", "防御", "50").Lines);
            using (var view = new EnhanceStatusPanel())
            {
                view.ShowResult(new EnhanceReport
                {
                    ItemNumber = 3, Round = 2,
                    LastStage = new EnhanceStageReadout(2, stats, options, false, "有效词条不足"),
                    EvidenceDirectory = Path.GetTempPath()
                });
                ListView list = Descendants(view).OfType<ListView>().Single();
                LinkLabel evidence = Descendants(view).OfType<LinkLabel>().Single();
                Require(list.Items.Count == 2 && list.Items[0].SubItems[1].Text == "7.5%" &&
                    list.Items[1].SubItems[1].Text == "50" && list.Items[1].SubItems[2].Text == "无效" &&
                    Descendants(view).OfType<Label>().Any(label => label.Text.Contains("无效 1 条") &&
                        label.Text.Contains("有效词条不足")) && evidence.Enabled,
                    "运行视图须呈现对应数值、无效条数、判定及截图入口");
                view.ShowResult(new EnhanceReport { ItemNumber = 4, EvidenceDirectory = Path.GetTempPath() });
                Require(list.Items.Count == 0 && evidence.Enabled, "新声骸要清除旧判定并保留同批截图入口");
                view.ShowResult(null);
                Require(!evidence.Enabled, "切换批次后应重置截图入口");
            }
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child))
                    yield return descendant;
            }
        }

        private static void Click(IEnumerable<Control> controls, string label)
        {
            Button button = controls.OfType<Button>().Single(control => control.Text == label);
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(button, new object[] { EventArgs.Empty });
        }

        internal static void Flow()
        {
            Require(EnhanceLoop.EvidenceCrop("success") == new Rectangle(144, 81, 448, 414) &&
                EnhanceLoop.EvidenceCrop("failed") == new Rectangle(144, 81, 448, 414) &&
                EnhanceLoop.EvidenceName("success", 9, 5, 3, 6, null) == "3" &&
                EnhanceLoop.EvidenceName("failed", 9, 2, 3, 6, "不合格:数值/等级") == "6_不合格数值等级",
                "成功/失败截图使用原版编号及裁切区域，失败原因不能成为路径");
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            Require(task.Observe(new EnhanceFrame(), 1000).Action == EnhanceAction.None && task.Failure != null,
                "启动时首个背包识别没有培养按钮应按原版立即报错");
            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            task.Observe(Bag(true), 1000);
            EnhanceDecision click = task.Observe(Bag(true), 1100);
            Require(click.Action == EnhanceAction.Click && click.Point == new Point(208, 189),
                "当前选中项为 +0 时也必须先点击第一格");
            Require(Send(task, Bag(true), 1700).Point == click.Point,
                "第一格的输入未投递时不能进入培养");
            Require(Send(task, Bag(false), 2300).Label == "切换等级排序为降序",
                "首格非零时，应先重置当前升序的列表");
            Require(task.Observe(Bag(true), 2900).Action == EnhanceAction.None,
                "仍显示原排序时不能跳过排序确认");
            var descending = Bag(true);
            descending.BagOrder = EchoBagOrder.Descending;
            Require(Send(task, descending, 3200).Point == new Point(560, 825),
                "看到降序后才恢复升序");
            Require(task.Observe(descending, 3750).Action == EnhanceAction.None &&
                Send(task, Bag(true), 4050).Point == click.Point,
                "确认回到升序后重新点第一格");
            task.Observe(Bag(true), 4600);
            Require(task.ScreenshotKind == "before", "新的候选项应有选中截图");
            Require(Send(task, Bag(true), 4700).Label == "打开培养", "应点击已识别的培养入口");
        }

        internal static void CandidateSelectionFeedback()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            task.Observe(Bag(true), 1000);
            Require(Send(task, Bag(true), 1100).UseWindowMessage, "首格优先用菜单消息选中");
            var titleOnly = Bag(true);
            titleOnly.BagLevel = -1;
            titleOnly.HasEchoSkillTitle = true;
            Require(titleOnly.ZeroLevelCandidate, "数字漏读时可使用原版声骸技能标题");
            titleOnly.BagLevel = 25;
            Require(!titleOnly.ZeroLevelCandidate, "明确读到 +25 时不得按零级送去培养");
            var unselected = Bag(true);
            unselected.FirstEchoSelected = false;
            Require(task.Observe(unselected, 1700).Action == EnhanceAction.None && task.ItemNumber == 0,
                "右侧 +0 属于其他卡片时不能打开培养");
            EnhanceDecision retry = Send(task, unselected, 2800);
            Require(retry.Label == "重试选择列表第一格" && !retry.UseWindowMessage,
                "首格仍未选中时可补点一次");
            task.Observe(Bag(true), 3400);
            Require(task.ItemNumber == 1 && task.ScreenshotKind == "before" &&
                Send(task, Bag(true), 3500).Label == "打开培养",
                "看到首格选中框和真实等级 +0 后可进入培养");

            var unreadable = Bag(true);
            unreadable.BagLevel = -1;
            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            task.Observe(unreadable, 1000);
            Send(task, unreadable, 1100);
            Send(task, unreadable, 2800);
            Send(task, unreadable, 4500);
            unreadable.BagOrder = EchoBagOrder.Descending;
            Send(task, unreadable, 5100);
            unreadable.BagOrder = EchoBagOrder.Ascending;
            Send(task, unreadable, 5700);
            Require(task.Observe(unreadable, 6300).Action == EnhanceAction.None && !task.Finished,
                "排序复位后也必须核对等级，漏读不能直接按无候选结束");
            Send(task, unreadable, 7400);
            task.Observe(unreadable, 16001);
            Require(task.Finished && task.Failure == "未确认首格声骸的等级。" &&
                task.ScreenshotKind == "error", "等级始终无法识别时要报告具体原因并保存截图");
        }

        internal static void CandidateExhaustion()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            var descending = Bag(false);
            descending.BagOrder = EchoBagOrder.Descending;
            task.Observe(descending, 1000);
            Send(task, descending, 1100);
            Require(Send(task, descending, 1700).Label == "切换等级排序为升序",
                "初始为降序时，应切到升序才能检查是否还有 +0");
            Require(task.Observe(descending, 2300).Action == EnhanceAction.None, "后台切换未生效时应等待");
            Require(Send(task, Bag(false), 2600).Label == "选择列表第一格", "升序复位后重新选中首格");
            task.Observe(Bag(false), 3150);
            var unreadable = Bag(false);
            unreadable.BagLevel = -1;
            task.Observe(unreadable, 3500);
            task.Observe(Bag(false), 3850);
            Require(!task.Finished, "有一帧未确认时应重新核对");
            task.Observe(Bag(false), 4200);
            Require(task.Finished && task.Failure == null && task.ScreenshotKind == "selection" &&
                task.Status.Contains("首格为 +25"), "确认升序首格非零时正常结束并保留背包截图");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            task.Observe(descending, 1000);
            Send(task, descending, 1100);
            Send(task, descending, 1700);
            Require(Send(task, descending, 3350).Label == "重试切换等级排序为升序" && !task.Finished,
                "排序没有响应时只补点一次");
            task.Observe(descending, 16001);
            Require(task.Failure == "未确认背包等级排序已切换。" && task.ScreenshotKind == "error",
                "降序首格非零不能作为列表耗尽的证据");
        }

        internal static void CultivateConfirmation()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToCultivate(task);
            var bag = Bag(true);
            var changed = Bag(false);
            Require(task.Observe(changed, 1750).Action == EnhanceAction.None,
                "点击培养前必须再次确认首格仍为 +0");
            EnhanceDecision first = Send(task, bag, 1800);
            Require(first.Label == "打开培养" && first.UseWindowMessage && first.SettleMs > 0,
                "培养入口应优先使用有悬停等待的菜单消息");
            Require(task.Observe(bag, 2299).Action == EnhanceAction.None,
                "发出培养点击后应按原版等 0.5 秒");
            EnhanceDecision retry = Send(task, bag, 2300);
            Require(retry.Label == "重试打开培养" && !retry.UseWindowMessage && retry.SettleMs > 0,
                "已确认同一零级卡和培养按钮仍在时重试，本地前台补点使用系统鼠标");
            Require(task.Observe(new EnhanceFrame(), 2850).Action == EnhanceAction.None &&
                task.Observe(changed, 3100).Action == EnhanceAction.None,
                "背包或零级候选证据消失时不得点击");
            Require(Send(task, bag, 3350).Label == "重试打开培养" &&
                Send(task, bag, 3850).Label == "重试打开培养",
                "与原版一致，持续确认按钮/目标时在 5 秒预算内可继续重试");
            Require(Send(task, bag, 6000).Label == "重试打开培养" &&
                task.Observe(bag, 6701).Action == EnhanceAction.None && task.Failure != null,
                "培养始终未打开且超过固定 5 秒预算时应停止");
        }

        internal static void TimingAndMarks()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            EnhanceDecision inTime = task.Observe(Tune(), 4300);
            EnhanceDecision onTimeout = task.Observe(Tune(), 7500);
            Require(inTime.Action == EnhanceAction.None && onTimeout.Action == EnhanceAction.None && task.Failure != null,
                "找不到阶段放入时不能点强化");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            Send(task, Stage(), 2500);
            task.Observe(new EnhanceFrame { MaterialCount = 0 }, 2900);
            task.Observe(new EnhanceFrame(), 3200);
            Require(task.Scan == EnhanceScan.StageInsert, "看到投入数量为 0 时不能进入强化");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            var finalTier = Tune();
            finalTier.TargetStatCount = 5;
            GoToDialog(task, finalTier);
            var overflow = new EnhanceFrame
            {
                OverflowDialog = true, CheckBox = new Rectangle(701, 564, 16, 16),
                ConfirmBox = new Rectangle(1079, 628, 57, 35)
            };
            Require(task.Observe(overflow, 3500).Action == EnhanceAction.None, "调谐后需要等待弹窗稳定");
            Require(task.Observe(overflow, 4730).Action == EnhanceAction.None &&
                task.Observe(overflow, 5229).Action == EnhanceAction.None, "原版识别弹窗后先等待0.5秒");
            Require(Send(task, overflow, 5230).Point == new Point(709, 572), "应先操作已识别的复选框");
            Require(task.Observe(overflow, 5500).Action == EnhanceAction.None, "操作弹窗时不能连点");
            Require(Send(task, overflow, 5730).Point == new Point(1107, 645), "勾选0.5秒后应点击真正的确认按钮");
            Require(task.Observe(overflow, 6600).Action == EnhanceAction.None, "未消失的确认弹窗不能重复提交");
            task.Observe(new EnhanceFrame(), 8700);
            EnhanceFrame passing = Rows("暴击", "7.5%", "暴击伤害", "15%", "攻击", "9%", "防御", "40", "生命", "100");
            task.Observe(Rows("暴击", "7.5%"), 9100);
            Require(!task.WantsLock && task.Round == 0, "词条尚未读齐时不能触发锁定");
            task.Observe(passing, 9500);
            Require(task.WantsLock && task.Observe(new EnhanceFrame(), 9800).Action == EnhanceAction.None,
                "锁定状态未知时不能按 C");
            var unmarked = new EnhanceFrame { LockMark = EchoMark.Unmarked };
            Require(task.Observe(unmarked, 10100).Action == EnhanceAction.None &&
                Send(task, unmarked, 10400).Key == 'C', "连续确认未锁定时才按 C");
            var marked = new EnhanceFrame { LockMark = EchoMark.Marked };
            task.Observe(marked, 11600);
            task.Observe(marked, 11900);
            Require(task.Paused && task.Enhanced == 1 && task.ScreenshotKind == "success",
                "确认锁定后才计数、保存截图并暂停");
            task.Resume(42000);
            Require(task.Observe(new EnhanceFrame { HasEnhancePanel = true }, 42200).Key == (char)0x1b &&
                task.Failure == null, "长时间暂停后必须重新计算返回背包的时限");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task);
            task.Observe(new EnhanceFrame(), 6800);
            task.Observe(Rows("防御", "40"), 7300);
            Require(task.LastStage.InvalidCount == 1 && task.LastStage.Rows[0].Value == "40" &&
                task.LastStage.Verdict == "弃置", "界面应收到状态机刚确认的词条和判定");
            Require(task.WantsDiscard && task.Observe(new EnhanceFrame(), 7600).Action == EnhanceAction.None,
                "弃置状态未知时不能按 Z");
            unmarked = new EnhanceFrame { DropMark = EchoMark.Unmarked };
            task.Observe(unmarked, 8200);
            Require(Send(task, unmarked, 8500).Key == 'Z', "应确认未弃置后再按 Z");
            marked = new EnhanceFrame { DropMark = EchoMark.Marked };
            task.Observe(marked, 9700);
            task.Observe(marked, 10000);
            Require(task.Discarded == 1 && task.ScreenshotKind == "failed", "确认弃置后才计数并保存截图");
        }

        internal static void MaterialAndTuneRecognition()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            var plain = new EnhanceFrame { HasPlainEnhance = true };
            GoToStage(task);
            var emptyStage = Stage();
            emptyStage.MaterialCount = 0;
            Send(task, emptyStage, 2500);
            task.Observe(new EnhanceFrame(), 2900);
            Require(task.Observe(plain, 3200).Action == EnhanceAction.None &&
                task.Scan == EnhanceScan.StageInsert && task.Failure == null,
                "投入数量未知、阶段放入漏读时不能把普通强化误判为没开启调谐");
            task.Observe(new EnhanceFrame { MaterialCount = 0 }, 7500);
            Require(task.Failure != null && task.Failure.Contains("投入材料仍为 0") && task.Round == 0,
                "阶段放入后材料仍为 0 时应报告投入失败");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            Send(task, emptyStage, 2500);
            task.Observe(new EnhanceFrame(), 7500);
            Require(task.Failure != null && task.Failure.Contains("未确认本轮阶段放入"),
                "点击前的零读数不能代表点击后的材料状态");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            Send(task, Stage(), 2500);
            task.Observe(new EnhanceFrame { MaterialCount = 2 }, 2900);
            Require(task.Scan == EnhanceScan.EnhanceButtons &&
                task.Observe(plain, 3200).Action == EnhanceAction.None && task.Failure == null,
                "确认材料后应等待组合按钮，不能在第一帧报错或点击普通强化");
            Require(Send(task, Tune(), 3600).Label == "强化并调谐" && task.Round == 0,
                "组合按钮延迟出现时应提交，发出点击不能计入档位");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            Send(task, Stage(), 2500);
            task.Observe(new EnhanceFrame { HasClearMaterials = true }, 2900);
            Require(task.Scan == EnhanceScan.EnhanceButtons && Send(task, Tune(), 3200).Label == "强化并调谐",
                "阶段放入变成清除时可确认预选生效，无需依赖计数文字");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            Send(task, Stage(), 2500);
            task.Observe(Tune(), 2900);
            Require(task.Scan == EnhanceScan.EnhanceButtons,
                "数量漏读但明确识别组合按钮时仍可进入调谐");
            Require(task.Observe(plain, 7450).Action == EnhanceAction.None &&
                task.Failure != null && task.Failure.Contains("仍只显示") && task.Round == 0,
                "组合按钮等候超时只能报告当前按钮，不能执行普通强化");
        }

        internal static void MaterialPreparationRetries()
        {
            var empty = Stage();
            empty.MaterialCount = 0;
            empty.HasPlainEnhance = true;
            var loaded = Stage();
            loaded.MaterialCount = 1;
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            EnhanceDecision place = Send(task, empty, 2500);
            Require(place.Label == "阶段放入" && place.UseWindowMessage && place.SettleMs > 0,
                "阶段放入首击须在前台和后台都优先使用窗口消息");
            Require(task.Observe(empty, 2800).Action == EnhanceAction.None,
                "点击后按原版等待0.3秒，第一帧零材料仍需复核");
            Require(Send(task, empty, 3050).Label == "再次阶段放入" &&
                task.Observe(empty, 3350).Action == EnhanceAction.None &&
                Send(task, empty, 3600).Label == "再次阶段放入",
                "在固定5秒预算内，连续确认仍为空可按原版节奏重复放入");
            Require(task.Observe(loaded, 3907).Action == EnhanceAction.None &&
                task.Scan == EnhanceScan.EnhanceButtons,
                "读到正数就应该等待强化按钮，即使阶段放入还没消失");
            Require(task.Observe(empty, 4500).Action == EnhanceAction.None,
                "等待调谐时单帧归零不能触发重试");
            Require(task.Observe(empty, 5150).Action == EnhanceAction.None &&
                task.Scan == EnhanceScan.EnhanceButtons, "已退出阶段放入循环后不重新进入另一轮放入");
            var ready = Tune();
            ready.MaterialCount = 2;
            EnhanceDecision submit = Send(task, ready, 7650);
            Require(submit.Label == "强化并调谐" && submit.UseWindowMessage && task.Round == 0,
                "确认材料后用同一路径点击组合按钮，投递时不能计档");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            Send(task, empty, 2500);
            task.Observe(empty, 4450);
            task.Observe(new EnhanceFrame(), 5000);
            Require(task.Observe(empty, 5500).Action == EnhanceAction.None,
                "未能连续读到零材料时继续等待");
            Require(Send(task, empty, 6000).Label == "再次阶段放入",
                "首个点击没有生效时，连续两帧确认后仍可以补点");
            Require(task.Observe(empty, 7350).Action == EnhanceAction.None && task.Failure != null,
                "重试不可重置阶段放入首个5秒预算");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToStage(task);
            var preselected = Tune();
            preselected.CurrentLevel = 0;
            preselected.PreviewLevel = 10;
            preselected.TargetStatCount = 2;
            preselected.MaterialCount = 2;
            Require(task.Observe(preselected, 2500).Action == EnhanceAction.None,
                "未由工具放入的多档预选不能自动提交");
            preselected.PreviewLevel = 5;
            preselected.TargetStatCount = 1;
            Require(task.Observe(preselected, 2900).Action == EnhanceAction.None &&
                task.Scan == EnhanceScan.StageInsert && task.Round == 0,
                "参考每档必须实际识别并完成阶段放入，不自动消耗预选材料");
            Require(task.Observe(preselected, 7500).Action == EnhanceAction.None && task.Failure != null,
                "始终没有完成本档阶段放入时应停止而非提交预选材料");
        }

        internal static void SubmissionConfirmation()
        {
            var preview = Tune();
            preview.Preview = "岩蛛S4型 +0 → +5 · 材料=1";
            preview.AutoBackground = true;
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task, preview);
            Require(task.Round == 0 && task.Observe(preview, 4700).Action == EnhanceAction.None,
                "第一次点击已投递时应等待游戏响应，不能增加档位");
            Require(task.Observe(preview, 5400).Action == EnhanceAction.None && task.Round == 0,
                "持续看到同一预览时不切到前台重发消耗材料操作");
            preview.AutoBackground = false;
            Require(task.Observe(preview, 6000).Action == EnhanceAction.None,
                "切到前台后仍须等待本次提交结果");
            Require(task.Observe(preview, 6400).Action == EnhanceAction.None && task.Round == 0,
                "即使确认同一预览也不能重复提交；参考每档仅提交一次");
            task.Observe(new EnhanceFrame(), 10000);
            task.Observe(Rows("暴击", "7.5%"), 10400);
            Require(task.Round == 1 && task.LastStage.Round == 1,
                "迟到的新增词条仍可确认一档强化");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            preview.AutoBackground = true;
            GoToDialog(task, preview);
            task.Observe(preview, 4700);
            task.Observe(preview, 5400);
            var changed = Tune();
            changed.Preview = "岩蛛S4型 +5 → +10 · 材料=1";
            Require(task.Observe(changed, 6400).Action == EnhanceAction.None,
                "预览已经变化时不能用旧请求补点");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task, preview);
            task.Observe(new EnhanceFrame { OverflowDialog = true }, 4700);
            task.Observe(preview, 5400);
            Require(task.Observe(preview, 6050).Action == EnhanceAction.None,
                "游戏已经弹出确认时不得重新提交");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            preview.AutoBackground = false;
            GoToDialog(task, preview);
            task.Observe(preview, 4700);
            Require(task.Observe(preview, 5400).Action == EnhanceAction.None, "固定窗口模式也不得重复提交");
            Require(task.Observe(preview, 7800).Action == EnhanceAction.None &&
                task.Observe(preview, 8500).Action == EnhanceAction.None &&
                task.Observe(preview, 18500).Action == EnhanceAction.None &&
                task.Failure != null && task.Failure.Contains("仍停在强化预览") && task.Round == 0,
                "游戏持续停在同一预览时应报告提交失败，不可重复点击");
        }

        internal static void StageProgress()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task);
            task.Observe(new EnhanceFrame(), 6800);
            task.Observe(Rows("暴击", "7.5%"), 7250);
            Require(task.Scan == EnhanceScan.StageInsert && task.Round == 1,
                "第一条达标后应尝试下一档");
            Send(task, Stage(), 7600);
            task.Observe(new EnhanceFrame { MaterialCount = 2 }, 8000);
            Send(task, Tune(), 8200);
            task.Observe(new EnhanceFrame(), 11750);
            task.Observe(Rows("暴击", "7.5%"), 12200);
            task.Observe(Rows("暴击", "7.5%"), 21350);
            Require(task.Failure != null && !task.WantsDiscard && task.Enhanced == 0,
                "只有旧词条时不能继续消耗材料或处置声骸");

            task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task);
            task.Observe(new EnhanceFrame(), 6800);
            task.Observe(Rows("暴击", "7.5%"), 7250);
            Send(task, Stage(), 7600);
            task.Observe(new EnhanceFrame { HasClearMaterials = true }, 8000);
            var higherTier = Tune();
            higherTier.TargetStatCount = 3;
            Send(task, higherTier, 8200);
            task.Observe(new EnhanceFrame(), 11750);
            task.Observe(Rows("暴击", "7.8%", "暴击伤害", "15%"), 12200);
            Require(task.Round == 1, "预览达到第 3 个孔位时，读到一部分词条应继续等待");
            task.Observe(Rows("暴击", "7.8%", "暴击伤害", "15%", "攻击", "9%"), 12800);
            Require(task.Round == 2 && task.Scan == EnhanceScan.StageInsert,
                "词条读齐后用当前读数判定，旧词条 OCR 的数值差异不能卡住流程");
        }

        internal static void ResultDismissal()
        {
            var heading = new OcrLine { Text = "调谐成功", Box = new Rectangle(727, 268, 140, 43), Score = 1 };
            var instruction = new OcrLine { Text = "点击任意位置返回", Box = new Rectangle(720, 822, 158, 26), Score = 1 };
            Require(EnhanceLoop.FindResultDismissTarget(new List<OcrLine> { heading, instruction }) == instruction.Box,
                "标题和返回提示同屏时应选择返回提示");
            Require(EnhanceLoop.FindResultDismissTarget(new List<OcrLine> { heading }) == heading.Box,
                "只有调谐成功标题时可以点击标题关闭结果");
            Require(EnhanceLoop.FindResultDismissTarget(new List<OcrLine>()).IsEmpty, "普通强化详情不应生成关闭操作");
        }

        internal static void ResultCloseConfirmation()
        {
            ResultCloseOcrCadence();
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task);
            var popup = new EnhanceFrame
            {
                DismissBox = new Rectangle(720, 822, 158, 26), Background = true, AutoBackground = true
            };
            Require(Send(task, popup, 6000).Label == "关闭调谐结果", "应发送第一次关闭点击");
            foreach (long now in new long[] { 7000, 8400, 10100 })
                Require(task.Observe(popup, now).Action == EnhanceAction.None && task.Round == 0,
                    "后台画面延迟时不能连点、切换前台或计档");
            Require(task.Observe(popup, 10550).Action == EnhanceAction.None,
                "超过反馈等待时间后也应再次确认结果页");
            task.Observe(new EnhanceFrame(), 10800);
            Require(task.Observe(popup, 11100).Action == EnhanceAction.None,
                "单次漏读后重新核对弹窗，不能沿用旧的可见次数");

            var confirmed = Rows("暴击", "7.5%");
            task.Observe(confirmed, 11800);
            Require(task.Round == 0, "结果页短暂消失时仍应等待");
            task.Observe(confirmed, 13200);
            task.Observe(confirmed, 13500);
            Require(task.Round == 1 && task.Scan == EnhanceScan.StageInsert,
                "确认弹窗退回且新增词条已出现后才能进入下一档");
        }

        private static void ResultCloseOcrCadence()
        {
            var popup = new EnhanceFrame { DismissBox = new Rectangle(720, 822, 158, 26) };
            var confirmed = Rows("暴击", "7.5%");
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task);
            EnhanceDecision dismiss = task.Observe(popup, 6000);
            Require(dismiss.Label == "关闭调谐结果" && dismiss.Sent != null, "快速关闭结果需要实际投递回执");
            dismiss.Sent(6100); // The input takes 100ms to send; count from its actual completion.
            foreach (long time in new long[] { 7099, 7100, 8100, 9099, 9199 })
                Require(task.Observe(confirmed, time).Action == EnhanceAction.None &&
                    task.Round == 0 && task.Scan == EnhanceScan.Dialog,
                    "即使立即关闭且词条完整，实际投递后1s+2s+0.1s之前还不能读词条");
            Require(task.Observe(confirmed, 9200).Action == EnhanceAction.None &&
                task.Round == 0 && task.Scan == EnhanceScan.Stats, "投递后3100ms才切换到正式词条OCR");
            task.Observe(confirmed, 9201);
            Require(task.Round == 1 && task.Scan == EnhanceScan.StageInsert, "完整等待后仍须确认新增词条才继续");

            foreach (bool separateResult in new[] { false, true })
            {
                task = new EchoEnhanceTask(EnhanceOptions.Defaults());
                GoToDialog(task);
                var overflow = new EnhanceFrame
                {
                    OverflowDialog = true, CheckBox = new Rectangle(701, 564, 16, 16),
                    ConfirmBox = new Rectangle(1079, 628, 57, 35)
                };
                Require(task.Observe(overflow, 4800).Action == EnhanceAction.None &&
                    Send(task, overflow, 5300).Label == "勾选本次登录不再提示", "溢出仍须先确认复选框");
                EnhanceDecision confirmation = task.Observe(overflow, 5800);
                Require(confirmation.Label == "确认材料溢出提示" && confirmation.Sent != null,
                    "溢出确认不能跳过实际按钮投递");
                confirmation.Sent(5900);
                long readyAt = 8200; // .2s after overflow confirmation + 2s no-dialog OCR + .1s.
                if (separateResult)
                {
                    dismiss = task.Observe(popup, 6100);
                    Require(dismiss.Label == "关闭调谐结果" && dismiss.Sent != null, "溢出后调谐提示仍单独关闭");
                    dismiss.Sent(6200);
                    readyAt = 9300; // Restart the full wait from the later result-closing input.
                }
                Require(task.Observe(confirmed, readyAt - 1).Action == EnhanceAction.None &&
                    task.Round == 0 && task.Scan == EnhanceScan.Dialog, "溢出或后续结果关闭后仍须完整静默等待");
                task.Observe(confirmed, readyAt);
                Require(task.Scan == EnhanceScan.Stats && task.Round == 0, "等待到期才切换词条OCR");
                task.Observe(confirmed, readyAt + 1);
                Require(task.Round == 1 && task.Scan == EnhanceScan.StageInsert, "溢出后的真实新增词条才允许继续");
            }
        }

        internal static void ResultCloseRetry()
        {
            var popup = new EnhanceFrame
            {
                DismissBox = new Rectangle(720, 822, 158, 26), Background = true, AutoBackground = true
            };
            // Even complete-looking stat text must not advance while the result dialog is still visible.
            popup.Lines.AddRange(Rows("暴击", "7.5%").Lines);
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            GoToDialog(task);
            Send(task, popup, 6000);
            Require(task.Observe(popup, 10550).Action == EnhanceAction.None &&
                task.Observe(popup, 10850).Action == EnhanceAction.ActivateGame,
                "自动模式下只有后台结果页持续可见才请求切到前台");
            popup.Background = popup.AutoBackground = false;
            Require(task.Observe(popup, 11000).Action == EnhanceAction.None &&
                task.Observe(popup, 11700).Action == EnhanceAction.None,
                "切换后应等待并重新核对仍可见的结果页");
            EnhanceDecision retry = Send(task, popup, 12000);
            Require(retry.Label == "重试关闭调谐结果" && !retry.UseWindowMessage,
                "从后台切到前台后，改用系统鼠标补点");
            Require(task.Observe(popup, 15500).Action == EnhanceAction.None && task.Failure == null &&
                task.Observe(popup, 18010).Action == EnhanceAction.None && task.Failure != null && task.Round == 0,
                "只补点一次，结果页持续可见时按弹窗出现时间超时");

            foreach (bool background in new[] { false, true })
            {
                popup.Background = background;
                task = new EchoEnhanceTask(EnhanceOptions.Defaults());
                GoToDialog(task);
                Require(!Send(task, popup, 6000).UseWindowMessage, "优先沿用当前窗口模式的关闭操作");
                long retryAt = background ? 10550 : 7850;
                Require(task.Observe(popup, retryAt).Action == EnhanceAction.None, "需再次核对可见结果");
                retry = Send(task, popup, retryAt + 300);
                Require(retry.Label == "重试关闭调谐结果" && retry.UseWindowMessage == !background,
                    "固定前台补点改用菜单消息，固定后台继续使用后台消息");
            }
        }

        internal static void DelayedResultAfterVisibleStats()
        {
            var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
            var tune = Tune();
            tune.Preview = "岩蛛S4型 +0 → +5 · 材料=1";
            tune.PreviewLevel = 5;
            GoToDialog(task, tune);
            task.Observe(new EnhanceFrame(), 5320);
            var transition = Rows("暴击", "7.5%");
            transition.CurrentLevel = 0;
            transition.PreviewLevel = 5;
            transition.HasEnhanceTune = true;
            transition.EnhanceBox = tune.EnhanceBox;
            transition.Preview = tune.Preview;
            Require(task.Observe(transition, 6170).Action == EnhanceAction.None && task.Scan == EnhanceScan.Dialog,
                "提交后的弹窗等待时间内不能判定过渡帧");
            Require(task.Observe(transition, 6800).Action == EnhanceAction.None &&
                task.Observe(transition, 7250).Action == EnhanceAction.None &&
                task.Round == 0 && task.Scan == EnhanceScan.Stats,
                "结算过渡帧提前显示词条时不能计档或重试提交");
            transition.CurrentLevel = transition.PreviewLevel = -1;
            task.Observe(transition, 7600);
            Require(task.Round == 0, "等级暂时漏读时，组合按钮仍在说明结算尚未清除预选");
            var popup = new EnhanceFrame { DismissBox = new Rectangle(720, 822, 158, 26) };
            Require(Send(task, popup, 7900).Label == "关闭调谐结果", "后出现的调谐结果需要关闭");
            var confirmed = Rows("暴击", "7.5%");
            confirmed.CurrentLevel = 5;
            task.Observe(confirmed, 9100);
            Require(task.Round == 0, "关闭结果后仍应等候剩余弹窗");
            Require(task.Observe(confirmed, 10999).Action == EnhanceAction.None && task.Round == 0,
                "已进入词条阶段后的迟到结果也必须从最后一次关闭等待3100ms");
            task.Observe(confirmed, 11000);
            Require(task.Round == 1 && task.Scan == EnhanceScan.StageInsert,
                "弹窗退出后的正式等级和新词条应允许下一档");
            Require(Send(task, popup, 11500).Label == "关闭调谐结果" && task.Round == 1,
                "下一档准备期间还能处理迟到的结果页");
            Require(task.Observe(Stage(), 12700).Action == EnhanceAction.None &&
                task.Observe(popup, 12800).Action == EnhanceAction.None,
                "迟到弹窗短暂漏读时，不能开始下一档或重复关闭");
            Require(task.Observe(Stage(), 14899).Action == EnhanceAction.None &&
                Send(task, Stage(), 14900).Label == "阶段放入", "确认结果完全关闭并静默后才重新进入阶段放入");
        }

        internal static void ReplayReference(string root)
        {
            var vision = new FarmVision();
            using (var reader = new FrameReader())
            using (var ocr = new OcrReader(OcrReader.Directory))
            {
                var task = new EchoEnhanceTask(EnhanceOptions.Defaults());
                task.Observe(Bag(true), 1000);
                using (var bitmap = new Bitmap(Path.Combine(root, "tests", "images", "echo_enhance.png")))
                {
                    var current = new EnhanceFrame();
                    EnhanceLoop.Scan(task, reader.Read(bitmap), current, ocr, vision);
                    Require(current.HasCultivate && current.FirstEchoSelected && current.BagLevel == 0,
                        "参考背包应识别培养、首格选中框和右侧等级 +0");
                }
                GoToStage(task = new EchoEnhanceTask(EnhanceOptions.Defaults()));
                using (var bitmap = new Bitmap(Path.Combine(root, "tests", "images", "find_add_mat.png")))
                {
                    PixelFrame frame = reader.Read(bitmap);
                    var current = new EnhanceFrame();
                    EnhanceLoop.Scan(task, frame, current, ocr, vision);
                    Require(current.HasStageInsert && current.MaterialCount == 0,
                        "参考强化截图应识别阶段放入以及 0/50 的投入材料读数");
                    GoToDialog(task = new EchoEnhanceTask(EnhanceOptions.Defaults()));
                    task.Observe(new EnhanceFrame(), 6800);
                    EnhanceLoop.Scan(task, frame, current, ocr, vision);
                    List<EchoStat> stats = EchoRules.Parse(current.Lines);
                    Require(stats.Count == 5 && stats[0].Name == "暴击伤害" && stats[0].Value == 15 &&
                        stats[3].Name == "防御百分比" && stats[4].Name == "防御",
                        "参考截图的五条副词条应正确读取和配对");
                }
                GoToDialog(task = new EchoEnhanceTask(EnhanceOptions.Defaults()));
                using (var bitmap = new Bitmap(Path.Combine(root, "tests", "images", "find_confirm.png")))
                {
                    var current = new EnhanceFrame();
                    EnhanceLoop.Scan(task, reader.Read(bitmap), current, ocr, vision);
                    Require(current.OverflowDialog && current.CheckBox.Contains(709, 575) &&
                        current.ConfirmBox.Contains(1108, 647), "参考溢出弹窗应分别识别真实复选框和确认按钮");
                }

                ReplayMarks(root, reader, vision, new[] { "echo_locked", "echo_not_locked" });
                ReplayMarks(root, reader, vision, new[] { "echo_dropped", "echo_not_dropped" });
            }
        }

        private static void ReplayMarks(string root, FrameReader reader, FarmVision vision, string[] labels)
        {
            for (int index = 0; index < labels.Length; index++)
            {
                string label = labels[index];
                string path = Directory.GetFiles(Path.Combine(root, "assets", "templates"), label + "_*.png").Single();
                using (var template = new Bitmap(path))
                using (var canvas = new Bitmap(1600, 900))
                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.FromArgb(18, 28, 44));
                    Rectangle area = PatternBank.For(label).First().Area;
                    area.Offset(22, -18);
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    graphics.DrawImage(template, area, new Rectangle(0, 0, template.Width, template.Height), GraphicsUnit.Pixel);
                    Require(EnhanceLoop.ReadMark(reader.Read(canvas), vision, labels) ==
                        (index == 0 ? EchoMark.Marked : EchoMark.Unmarked),
                        "当前参考图标应判定为 " + label);
                }
            }
        }

        private static EnhanceDecision Send(EchoEnhanceTask task, EnhanceFrame frame, long now)
        {
            EnhanceDecision decision = task.Observe(frame, now);
            if (decision.Sent != null)
                decision.Sent(now);
            return decision;
        }

        private static void GoToCultivate(EchoEnhanceTask task)
        {
            task.Observe(Bag(true), 1000);
            Send(task, Bag(true), 1100);
            task.Observe(Bag(true), 1700);
        }

        private static void GoToStage(EchoEnhanceTask task)
        {
            GoToCultivate(task);
            Send(task, Bag(true), 1800);
            task.Observe(new EnhanceFrame { HasEnhancePanel = true }, 2350);
        }

        private static void GoToDialog(EchoEnhanceTask task, EnhanceFrame submit = null)
        {
            GoToStage(task);
            Send(task, Stage(), 2500);
            task.Observe(new EnhanceFrame { MaterialCount = 2 }, 2900);
            Send(task, submit ?? Tune(), 3200);
        }

        private static EnhanceFrame Stage()
        {
            return new EnhanceFrame { HasStageInsert = true, StageBox = new Rectangle(375, 650, 80, 25) };
        }

        private static EnhanceFrame Tune()
        {
            return new EnhanceFrame { HasEnhanceTune = true, EnhanceBox = new Rectangle(190, 810, 110, 35) };
        }

        private static EnhanceFrame Bag(bool zero)
        {
            return new EnhanceFrame
            {
                HasCultivate = true, FirstEchoSelected = true, BagLevel = zero ? 0 : 25,
                BagOrder = EchoBagOrder.Ascending, CultivateBox = new Rectangle(1300, 800, 80, 40)
            };
        }

        private static EnhanceFrame Rows(params string[] entries)
        {
            var frame = new EnhanceFrame();
            for (int index = 0; index < entries.Length; index++)
                frame.Lines.Add(new OcrLine { Text = entries[index], Score = 1,
                    Box = new Rectangle(index % 2 == 0 ? 180 : 500, 272 + index / 2 * 33, 80, 24) });
            return frame;
        }
    }
}
