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
                { DailyTask.Stamina, DailyTask.DailyRewards, DailyTask.Nightmare }),
                "无音区应先刷完，再领取日常奖励，最后检查梦魇");
            defaults.Farm = DomainKind.Forgery;
            Require(DailyRunner.ShouldCheckNightmareProgress(defaults), "刷材料本时按日常进度决定是否补梦魇");
            Require(defaults.GetTasks().Take(3).SequenceEqual(new[]
                { DailyTask.Stamina, DailyTask.Nightmare, DailyTask.DailyRewards }),
                "材料本的按需梦魇应在检查日常奖励前执行");

            DailyTask[] standard = defaults.GetTasks();
            DailyPlan.OrderRewardsAndNightmare(standard, true);
            Require(standard[1] == DailyTask.DailyRewards && standard[2] == DailyTask.Nightmare,
                "无音区或培养目标实际领奖后先领日常");
            DailyPlan.OrderRewardsAndNightmare(standard, false);
            Require(standard[1] == DailyTask.Nightmare && standard[2] == DailyTask.DailyRewards,
                "没有体力回执时先执行已启用的梦魇");

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
        }

        private static OcrLine Line(string text)
        {
            return new OcrLine { Text = text, Box = new Rectangle(300, 300, 50, 25), Score = .95f };
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
            Require(daily.GetTasks().SequenceEqual(new[] { DailyTask.Stamina, DailyTask.DailyRewards, DailyTask.Nightmare,
                DailyTask.Mail, DailyTask.BattlePass, DailyTask.Merge, DailyTask.Farm4C }) &&
                daily.GetNightmareMode() == DailyNightmareMode.All && daily.Farm == DomainKind.Forgery,
                "旧配置的任务、顺序或梦魇方式发生变化");
        }
    }
}
