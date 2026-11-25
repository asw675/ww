using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace TideCombat
{
    internal static class DailyChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void ObtainedRewardReceiptNeedsHeadingAndDismissal()
        {
            var heading = new OcrLine { Text = "获得", Box = new Rectangle(764, 212, 72, 38), Score = .99f };
            var footer = new OcrLine { Text = "点击空白处继续", Box = new Rectangle(733, 786, 135, 19), Score = .99f };
            var otherFooter = new OcrLine { Text = footer.Text, Box = new Rectangle(878, 786, 135, 19), Score = .99f };
            var faint = new OcrLine { Text = footer.Text, Box = footer.Box, Score = .5f };
            var unrelated = new OcrLine { Text = "获得声骸", Box = heading.Box, Score = .99f };
            Require(DailyClaimVision.ObtainedReceiptDismissal(new[] { heading }, new[] { footer }) == footer.Box,
                "the short receipt must return the verified continuation prompt");
            Require(DailyClaimVision.ObtainedReceiptDismissal(new[] { heading }, new OcrLine[0]).IsEmpty &&
                DailyClaimVision.ObtainedReceiptDismissal(new[] { unrelated }, new[] { footer }).IsEmpty &&
                DailyClaimVision.ObtainedReceiptDismissal(new[] { heading }, new[] { faint }).IsEmpty &&
                DailyClaimVision.ObtainedReceiptDismissal(new[] { heading }, new[] { footer, otherFooter }).IsEmpty,
                "partial, unrelated, faint, and ambiguous text cannot authorize closing a receipt");
        }

        internal static void ActivityScrollbar()
        {
            using (var image = new Bitmap(1600, 900))
            using (Graphics paint = Graphics.FromImage(image))
            using (var reader = new FrameReader())
            {
                paint.Clear(Color.FromArgb(28, 41, 60));
                paint.FillRectangle(Brushes.White, 1540, 174, 3, 260);
                paint.FillRectangle(Brushes.White, 1558, 174, 5, 22);
                Require(DailyRunner.FindDailyActivityScrollThumb(reader.Read(image)).IsEmpty,
                    "忽略列表内容里的长边缘与滚动条边缘的短干扰");

                paint.FillRectangle(Brushes.White, 1558, 214, 5, 200);
                Require(DailyRunner.FindDailyActivityScrollThumb(reader.Read(image)) ==
                    new Rectangle(1558, 214, 1, 200), "识别右侧日常列表滑块");

                paint.Clear(Color.FromArgb(28, 41, 60));
                paint.FillRectangle(Brushes.White, 1558, 419, 5, 200);
                Require(DailyRunner.FindDailyActivityScrollThumb(reader.Read(image)) ==
                    new Rectangle(1558, 419, 1, 200), "翻页后应测得滑块的新位置");
            }
        }

        internal static void StepRetriesOnce()
        {
            var attempts = new System.Collections.Generic.List<bool>();
            int recoveryChecks = 0;
            var firstFailure = new InvalidOperationException("首次截图未核验");
            var finalFailure = new InvalidOperationException("重试截图仍未核验");
            Func<InvalidOperationException, bool> safeToRetry = error =>
            {
                Require(ReferenceEquals(error, firstFailure), "只处理首次执行的未完成结果");
                recoveryChecks++;
                return true;
            };

            DailyPlan.ExecuteWithOneRetry(attempts.Add, safeToRetry);
            Require(attempts.SequenceEqual(new[] { false }) && recoveryChecks == 0, "已核验的项目只执行一次");

            attempts.Clear();
            DailyPlan.ExecuteWithOneRetry(retry =>
            {
                attempts.Add(retry);
                if (!retry) throw firstFailure;
            }, safeToRetry);
            Require(attempts.SequenceEqual(new[] { false, true }) && recoveryChecks == 1, "可安全恢复的未完成项目只重跑一次");

            attempts.Clear();
            try
            {
                DailyPlan.ExecuteWithOneRetry(retry =>
                {
                    attempts.Add(retry);
                    throw retry ? finalFailure : firstFailure;
                }, safeToRetry);
                throw new Exception("重试失败必须返回失败");
            }
            catch (InvalidOperationException error)
            {
                Require(ReferenceEquals(error, finalFailure) && attempts.SequenceEqual(new[] { false, true }) &&
                    recoveryChecks == 2, "重试失败不能触发第三次执行");
            }

            attempts.Clear();
            try
            {
                DailyPlan.ExecuteWithOneRetry(retry =>
                {
                    attempts.Add(retry);
                    throw firstFailure;
                }, error => false);
                throw new Exception("无法安全恢复时必须返回失败");
            }
            catch (InvalidOperationException error)
            {
                Require(ReferenceEquals(error, firstFailure) && attempts.SequenceEqual(new[] { false }),
                    "无法安全重跑的项目不能重复操作");
            }
        }

        internal static void Plan()
        {
            Require(DailyPlan.RewardReady(100), "100 积分应视为已领取");
            Require(!DailyPlan.RewardReady(99), "99 积分不应视为已领取");
            Require(DailyPlan.NeedsStamina(false, 0), "未满且未用完体力应继续刷");
            Require(!DailyPlan.NeedsStamina(true, 0), "积分已满不应刷体力");
            Require(!DailyPlan.NeedsStamina(false, 180), "体力已用满不应继续");
            DailyOptions defaults = DailyOptions.Defaults();
            var optional = new[] { DailyTask.Garden, DailyTask.Merge, DailyTask.Farm4C };
            Require(defaults.GetTasks().Where(optional.Contains).SequenceEqual(new [] { DailyTask.Garden }) &&
                defaults.GetNightmareMode() == DailyNightmareMode.AsNeeded && !defaults.AutoNightmare &&
                !defaults.MergeIfNeeded && !defaults.Farm4C, "by default only the garden is a selected post-daily task");
            Require(!DailyRunner.ShouldCheckNightmareProgress(defaults), "刷无音区时无需补日常声骸");
            Require(defaults.GetTasks().Take(3).SequenceEqual(new[]
                { DailyTask.Nightmare, DailyTask.Stamina, DailyTask.DailyRewards }),
                "隐式计划应先检查梦魇（无音区按需模式直接跳过），再刷体力、领取日常奖励");
            Require(defaults.IsReferencePlan && defaults.GetReferenceRunTasks(false, 0).First() == DailyTask.Stamina &&
                !defaults.GetReferenceRunTasks(false, 0).Contains(DailyTask.Nightmare) &&
                defaults.GetReferenceRunTasks(true, 0).First() == DailyTask.DailyRewards,
                "参考按需条件不满足时不能执行虚拟梦魇或体力步骤");
            defaults.Farm = DomainKind.Forgery;
            Require(DailyRunner.ShouldCheckNightmareProgress(defaults), "刷材料本时按日常进度决定是否补梦魇");
            Require(defaults.GetTasks().Take(3).SequenceEqual(new[]
                { DailyTask.Nightmare, DailyTask.Stamina, DailyTask.DailyRewards }),
                "材料本按需梦魇在体力本之前，基于初始日常积分判断");
            Require(defaults.GetReferenceRunTasks(false, 0).Take(2).SequenceEqual(new[] { DailyTask.Nightmare, DailyTask.Stamina }) &&
                defaults.GetReferenceRunTasks(false, 180).First() == DailyTask.Nightmare &&
                !defaults.GetReferenceRunTasks(false, 180).Contains(DailyTask.Stamina),
                "材料本的两个前置任务基于同一初始快照并独立判断");
            defaults.AutoNightmare = true;
            defaults.Farm = DomainKind.Tacet;
            Require(defaults.GetTasks().First() == DailyTask.Nightmare &&
                defaults.GetNightmareMode() == DailyNightmareMode.All, "全清梦魇同样必须在无音区前");
            Require(defaults.GetReferenceRunTasks(true, 180).First() == DailyTask.Nightmare &&
                !defaults.GetReferenceRunTasks(true, 180).Contains(DailyTask.Stamina), "原版全清模式不受日常奖励积分制约");
            defaults.AutoNightmare = false;
            defaults.FarmNightmareEcho = false;
            Require(!defaults.GetTasks().Contains(DailyTask.Nightmare), "禁用两种梦魇选项时不运行梦魇");

            defaults.FarmNightmareEcho = true;
            defaults.Farm = DomainKind.Cultivation;
            DailyTask[] cultivationPlan = defaults.GetTasks();
            Require(!defaults.IsReferencePlan, "无原版对应的培养目标明确使用 WW 扩展路径");
            Require(cultivationPlan[0] == DailyTask.Stamina, "WW 培养目标优先执行，避免重复梦魇");
            DailyPlan.OrderCultivationRewardsAndNightmare(cultivationPlan, false);
            Require(cultivationPlan[1] == DailyTask.Nightmare && cultivationPlan[2] == DailyTask.DailyRewards,
                "培养无消耗时保留旧版先梦魇后领奖的顺序");
            DailyPlan.OrderCultivationRewardsAndNightmare(cultivationPlan, true);
            Require(cultivationPlan[1] == DailyTask.DailyRewards && cultivationPlan[2] == DailyTask.Nightmare,
                "培养有确认消耗时保留旧版先领奖后梦魇的顺序");

            int value;
            Require(DailyPlan.TryReadPoints(new[] { Line("２０／１００") }, out value) && value == 20,
                "积分分式应读取分子，避免 20/100 被识别为 20100");
            Require(DailyPlan.TryReadPoints(new[] { Line("今日活跃度：100") }, out value) && value == 100,
                "数字和当前活跃度标签应支持");
            Require(!DailyPlan.TryReadPoints(new[] { Line("40/180"), Line("奖励×2") }, out value),
                "不得把其他计数当作每日积分");
            Require(!DailyPlan.TryReadPoints(new[] { Line("20"), Line("100") }, out value),
                "冲突的活跃度须标为未确认");
            Require(DailyPlan.TryReadStamina(new[] { Line("０／１８０") }, out value) && value == 0 &&
                !DailyPlan.TryReadStamina(new[] { Line("进度未识别") }, out value),
                "需要区分确认消耗 0 体力与未识别");
            Require(!DailyPlan.TryReadStamina(new[] { Line("40/180"), Line("120/180") }, out value),
                "冲突的体力计数不得用于计算消耗额度");
            bool uncertain;
            Require(!DailyPlan.TryReadStamina(new[] { Line("40/180"), Line("120/180") }, out value, out uncertain) && uncertain &&
                !DailyPlan.TryReadStamina(new[] { Line("已完成") }, out value, out uncertain) && !uncertain,
                "必须区分冲突与参考允许按零计算的缺失计数");
            OcrLine weak = Line("40/180"); weak.Score = .5f;
            Require(!DailyPlan.TryReadStamina(new[] { weak }, out value, out uncertain) && uncertain,
                "低置信数值不得视作缺失并盲目消耗体力");
            Require(DailyPlan.ReferenceControlPoint(new Rectangle(1780, 930, 60, 40), new Size(1920, 1080), .93, .882) == new Point(1785, 952) &&
                DailyPlan.ReferenceControlPoint(new Rectangle(1500, 850, 60, 40), new Size(1920, 1080), .93, .882) == new Point(1530, 870),
                "参考坐标只能在已识别按钮内部使用，布局不同改走已识别目标");
            Require(DailyPlan.RecoverableReferenceTask(DailyTask.Nightmare) && DailyPlan.RecoverableReferenceTask(DailyTask.Garden) &&
                DailyPlan.RecoverableReferenceTask(DailyTask.Merge) && !DailyPlan.RecoverableReferenceTask(DailyTask.Stamina) &&
                !DailyPlan.RecoverableReferenceTask(DailyTask.Mail) && !DailyPlan.RecoverableReferenceTask(DailyTask.Farm4C),
                "仅参考明确捕获的异常项目允许安全恢复后继续");
            foreach (bool reference in new[] { false, true })
            {
                Require(DailyPlan.CanAttemptRecovery(DailyTask.Stamina, reference, false, true) &&
                    DailyPlan.CanAttemptRecovery(DailyTask.Nightmare, reference, false, true) &&
                    !DailyPlan.CanAttemptRecovery(DailyTask.Stamina, reference, false, false),
                    "普通未核验返回允许安全恢复，真实体力本错误仍必须停止");
            }
            Require(!TaskContinuation.SameWindow(IntPtr.Zero, 0, IntPtr.Zero, 0) &&
                TaskContinuation.SameWindow(new IntPtr(34), 56, new IntPtr(34), 56) &&
                !TaskContinuation.SameWindow(new IntPtr(34), 56, new IntPtr(35), 56) &&
                !TaskContinuation.SameWindow(new IntPtr(34), 56, new IntPtr(34), 57),
                "正常未核验结果不能跨不明窗口或进程继续");
            Require(GuidebookNavigation.ReferenceOpenDelayMs == 4000 &&
                GuidebookNavigation.ReferenceBeforeTabDelayMs == 2000 && GuidebookNavigation.ReferenceAfterTabDelayMs == 1500 &&
                GuidebookNavigation.ShouldClickTab(false) && !GuidebookNavigation.ShouldClickTab(true),
                "真实图鉴导航采用参考等待，已确认选中不重复发送输入");
        }

        private static OcrLine Line(string text)
        {
            return new OcrLine { Text = text, Box = new Rectangle(300, 300, 50, 25), Score = .95f };
        }

        internal static void NightmareUnresolvedCombatStopsPlan()
        {
            foreach (bool reference in new[] { false, true })
            {
                Require(!DailyPlan.CanAttemptRecovery(DailyTask.Nightmare, reference, false, false,
                    nightmareCombatUnresolved: true), "梦魇未确认开战或清场时必须中止后续步骤");
                Require(!DailyPlan.CanAttemptRecovery(DailyTask.Nightmare, reference, false, true,
                    nightmareCombatUnresolved: true), "普通未核验返回的恢复规则不能覆盖梦魇战斗未核验");
                Require(DailyPlan.CanAttemptRecovery(DailyTask.Nightmare, reference, false, false) &&
                    DailyPlan.CanAttemptRecovery(DailyTask.Nightmare, reference, false, true),
                    "其他梦魇导航或扫描问题仍按各自的恢复规则判断");
            }
        }

        internal static void Options()
        {
            DailyOptions options = DailyOptions.Defaults();
            options.Validate();
            bool threw = false;
            try
            {
                options.TacetIndex = 0;
                options.Validate();
            }
            catch (System.IO.InvalidDataException) { threw = true; }
            Require(threw, "越界序号应拒绝");

            foreach (DailyNightmareMode mode in new[] { DailyNightmareMode.AsNeeded, DailyNightmareMode.CaptureOnce })
            {
                var configured = new NightmareOptions { Purification = true };
                NightmareOptions capture = DailyRunner.CreateNightmareOptions(configured, mode);
                Require(capture.CaptureOnce && capture.Purification && !capture.TacetNest && !configured.CaptureOnce,
                    "单次捕获应只检查选中的巢穴类型，且不得修改独立梦魇任务配置");
                NightmareOptions empty = DailyRunner.CreateNightmareOptions(new NightmareOptions(), mode);
                Require(empty.CaptureOnce && empty.Purification && empty.TacetNest,
                    "未配置巢穴类型的单次捕获应检查两种巢穴");
            }

            NightmareOptions all = DailyRunner.CreateNightmareOptions(new NightmareOptions { TacetNest = true,
                CaptureOnce = true }, DailyNightmareMode.All);
            Require(!all.CaptureOnce && all.TacetNest && !all.Purification,
                "全部模式应清理选中的巢穴类型");
        }

        internal static void AdditionalConfig()
        {
            foreach (string state in new[] { "no-teleport", "no-nest", "valid", "unselected" })
            {
                CombatSettings settings = CombatSettings.Defaults();
                settings.Farm.Teleport = state == "no-nest" || state == "valid";
                settings.Nightmare.TacetNest = false;
                settings.Nightmare.Purification = state == "valid";
                DailyOptions options = DailyOptions.Defaults();
                options.NightmareMode = DailyNightmareMode.All;
                options.Tasks = state == "unselected" ? new[] { DailyTask.Garden } :
                    new[] { DailyTask.Farm4C, DailyTask.Nightmare };
                bool valid = state == "valid" || state == "unselected";
                using (var cancellation = new CancellationTokenSource())
                {
                    if (valid) cancellation.Cancel();
                    var reports = new System.Collections.Generic.List<DailyReport>();
                    using (var runner = new DailyRunner(settings, options, cancellation.Token, new StepProgress<DailyReport>(reports.Add)))
                    {
                        runner.Run();
                        Require(runner.Failed != valid, state + " additional task validation");
                        Require(reports.Count == (valid ? 0 : 1), "invalid configuration must stop before reporting any started task");
                        if (!valid)
                            Require(reports[0].Finished && reports[0].Failure.Contains(state == "no-teleport" ? "传送" : "选择"),
                                "failure should name the missing setting");
                    }
                }
            }
        }

        internal static void SavesOrderedPlan()
        {
            string path = Path.Combine(Path.GetTempPath(), "TideCombat-daily-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                CombatSettings settings = CombatSettings.Defaults();
                settings.Daily.Tasks = new[] { DailyTask.Mail, DailyTask.Nightmare, DailyTask.DailyRewards };
                settings.Daily.NightmareMode = DailyNightmareMode.CaptureOnce;
                settings.Daily.ExitAfterTask = true;
                SettingsFile.Save(path, settings);
                DailyOptions restored = SettingsFile.Load(path).Daily;
                Require(restored.GetTasks().SequenceEqual(settings.Daily.Tasks) &&
                    restored.GetNightmareMode() == DailyNightmareMode.CaptureOnce && restored.ExitAfterTask,
                    "重开程序后执行项目、顺序或执行方式丢失");

                restored.NightmareMode = DailyNightmareMode.AsNeeded;
                Require(DailyRunner.ShouldCheckNightmareProgress(restored),
                    "未选择刷体力时，默认无音区副本不应阻止按需补梦魇");

                foreach (DailyTask[] invalid in new[]
                {
                    new DailyTask[0], new[] { DailyTask.Mail, DailyTask.Mail }, new[] { (DailyTask)99 }
                })
                {
                    restored.Tasks = invalid;
                    bool rejected = false;
                    try { restored.Validate(); }
                    catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "空列表、重复或未知任务应拒绝");
                }
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        internal static void ReadsLegacySettings()
        {
            const string oldSettings = "{\"Daily\":{\"Farm\":1,\"FarmNightmareEcho\":false,\"AutoNightmare\":true,"
                + "\"CheckGarden\":false,\"MergeIfNeeded\":true,\"Farm4C\":true}}";
            CombatSettings settings;
            using (var source = new MemoryStream(Encoding.UTF8.GetBytes(oldSettings)))
                settings = (CombatSettings)new DataContractJsonSerializer(typeof(CombatSettings)).ReadObject(source);
            DailyOptions daily = settings.Daily;
            daily.Validate();
            Require(daily.GetTasks().SequenceEqual(new[] { DailyTask.Nightmare, DailyTask.Stamina, DailyTask.DailyRewards,
                DailyTask.Mail, DailyTask.BattlePass, DailyTask.Merge, DailyTask.Farm4C }) &&
                daily.GetNightmareMode() == DailyNightmareMode.All && daily.Farm == DomainKind.Forgery,
                "旧配置保留任务和梦魇方式，并采用参考版本的隐式顺序");
        }
    }
}
