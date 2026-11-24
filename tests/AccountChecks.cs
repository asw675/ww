using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace TideCombat
{
    internal static class AccountChecks
    {
        private static void Require(bool condition, string explanation)
        {
            if (!condition)
                throw new InvalidOperationException(explanation);
        }

        internal static void Keys()
        {
            Require(AccountKeys.Normalize("A0B.Con") == "aob.com", "账号归一化错误");
            Require(AccountKeys.FromOcr("***abc@x.com") == "abc@x.com", "OCR 账号去星号错误");
            Require(AccountKeys.FromOcr("  name  ") == "name", "OCR 账号应裁剪空白");
            Require(AccountKeys.FindSelection(new[] { "***lo7@example.com", "***l07@example.com" }, "l07@example.com") == 1,
                "登录列表的原文应优先于 OCR 归一化");
            Require(AccountKeys.FindSelection(new[] { "***a0b.con" }, "aob.com") < 0,
                "配置账号必须匹配去星后的原文");
            Require(AccountKeys.FindSelection(new[] { "Ab***12", "ab12", "*Ab12" }, "Ab12") == 2,
                "重复显示的配置账号应选列表下方的精确匹配项");
            Require(AccountKeys.FindSelection(new[] { "***a0b.con", "***a0b.com" }, "aob.com") < 0,
                "OCR 纠错得到两个可能账号时应拒绝选择");
            foreach (string invalid in new[] { "", "***", "123*1234", " name", "name ", "a\nb" })
            {
                bool rejected = false;
                try { AccountStore.Validate(new List<AccountEntry> { Account(invalid) }); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected, "拒绝包含星号、首尾空格或控制字符的账号 ID");
            }
            foreach (var pair in new[] { Tuple.Create("123***1234", "1231234"), Tuple.Create("Ab****12@outlook.com", "Ab12@outlook.com"),
                Tuple.Create("Some Name", "Some Name"), Tuple.Create("*a*b**", "ab"), Tuple.Create("1234", "1234") })
            {
                Require(AccountKeys.FromOcr(pair.Item1) == pair.Item2, "从原文去除所有星号");
                AccountStore.Validate(new List<AccountEntry> { Account(pair.Item2) });
            }
            Require(AccountKeys.FromOcr("A0****12") != AccountKeys.FromOcr("ao****12"), "配置账号保留大小写及数字");
        }

        private static AccountEntry Account(string account)
        {
            return new AccountEntry { account_key = account, config = new Dictionary<string, object>() };
        }

        internal static void LoginLabels()
        {
            var login = new OcrLine { Text = "Log in", Box = new Rectangle(700,450,100,40) };
            var otherLogin = new OcrLine { Text = "LOGIN", Box = new Rectangle(1200,100,60,30) };
            var upper = new OcrLine { Text = "A*b**12@example.test", Box = new Rectangle(630,300,160,32) };
            var lower = new OcrLine { Text = "Ab12@example.test", Box = new Rectangle(650,410,150,28) };
            var rows = new[] { otherLogin, lower, login, upper };
            Require(MultiAccountRunner.FindTargetRow(rows,"Ab12@example.test") == lower,
                "配置任务应点击位置最低的精确匹配行");
            Require(MultiAccountRunner.FindDropdown(rows, true, "Ab12@example.test") == null,
                "展开的列表不能确认为当前账号");
            lower.Text = "Some Name";
            Require(MultiAccountRunner.FindDropdown(new[] { lower, login, otherLogin }, true, "Some Name") == lower,
                "配置账号支持完整显示名和多个登录标签");
            Require(MultiAccountRunner.FindDropdown(new[] { upper, login }, false, null) == null,
                "顺序任务只识别包含连续四个星号的账号");
            upper.Text = "A****12";
            Require(MultiAccountRunner.FindDropdown(new[] { upper, login, otherLogin }, false, null) == upper,
                "顺序任务允许多个登录标签");
            Require(LoginScreen.AccountLoginPoint(new Size(1600,900), rows) == new Point(750,470),
                "登录按钮必须位于登录区域内");
            Require(LoginScreen.AccountLoginPoint(new Size(2560,1440), new OcrLine[0]) == new Point(1280,817),
                "无按钮时使用游戏坐标中的后备位置");
            Require(LoginScreen.AccountLoginPoint(new Size(2100,900), new OcrLine[0]) == new Point(1050,511),
                "宽屏后备位置保持居中");
        }

        internal static void InvalidPlans()
        {
            string path = Path.Combine(Path.GetTempPath(), "ww-plan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var store = new AccountStore(path);
            const string row = "{\"account_key\":\"fixture\",\"enabled\":true,\"task\":null,\"config\":{}}";
            try
            {
                foreach (string taskName in new[] { "MultiAccountDailyTask", "MultiAccountConfigTask" })
                {
                    AccountEntry recursive = Account("fixture");
                    recursive.task = new AccountTaskReference { module = "src.task." + taskName, @class = taskName };
                    bool rejected = false;
                    try { AccountStore.Validate(new List<AccountEntry> { recursive }); }
                    catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "拒绝嵌套的多账号任务");
                }
                foreach (string bad in new[] {
                    row.Replace("\"fixture\"", "123"), row.Replace("true", "\"true\""), row.Replace("\"task\":null,", ""),
                    row.Replace("\"fixture\"", "\"a\\u0001b\""), row.Replace("\"config\":{}", "\"config\":[]"),
                    row.Replace("null", "{\"module\":\"src.task.DailyTask\",\"class\":\"DailyTask\",\"extra\":1}"),
                    row.Replace("true", "false").Replace("null", "{\"module\":\"src.task.MultiAccountConfigTask\",\"class\":\"MultiAccountConfigTask\"}")
                })
                {
                    string data = "{\"accounts\":[" + bad + "]}";
                    File.WriteAllText(store.ConfigPath,data);
                    bool rejected = false;
                    try { store.Load(); } catch (InvalidDataException) { rejected = true; }
                    Require(rejected && File.ReadAllText(store.ConfigPath) == data, "损坏的账号配置必须在执行前被拒绝");
                }
                File.WriteAllText(store.ConfigPath, "{\"accounts\":[" + row.Replace("\"enabled\":true,", "")
                    .Replace("null", "{\"module\":\"TideCombat\",\"class\":\"DailyTask\"}") + "]}");
                AccountEntry entry = store.Load().Single();
                Require(entry.enabled && entry.task.module == "src.task.DailyTask", "兼容旧版 WW 保存的任务引用");
            }
            finally
            {
                if (File.Exists(store.ConfigPath)) File.Delete(store.ConfigPath);
                Directory.Delete(path);
            }
        }

        internal static void CancelledPlans()
        {
            foreach (MultiAccountMode mode in new[] { MultiAccountMode.Sequential, MultiAccountMode.Config })
            {
                var reports = new List<MultiAccountReport>();
                var token = new CancellationToken(true);
                using (var runner = new MultiAccountRunner(CombatSettings.Defaults(), mode, token,
                    new StepProgress<MultiAccountReport>(reports.Add)))
                {
                    bool cancelled = false;
                    try { runner.Run(); }
                    catch (OperationCanceledException error) { cancelled = error.CancellationToken == token; }
                    Require(cancelled && runner.Failure == null && reports.Count == 0,
                        "已经取消的多账号任务不应启动账号或报告完成、失败状态");
                }
            }
        }

        internal static void EditorDrafts()
        {
            var daily = new AccountTaskReference { module = "src.task.DailyTask", @class = "DailyTask" };
            var change = new AccountTaskReference { module = "src.task.ChangeEchoTask", @class = "ChangeEchoTask" };
            var stored = new Dictionary<string, object> { { "daily", new Dictionary<string, object>
                { { "checkgarden", false }, { "future_field", "keep" } } } };
            var a = new AccountEditorRow(new AccountEntry { task = daily, config = stored });
            var b = new AccountEditorRow(new AccountEntry { task = daily, config = new Dictionary<string, object>() });
            CombatSettings global = CombatSettings.Defaults();
            global.Daily.CheckGarden = false;
            var edit = AccountConfig.ForRun(global);
            edit.Daily.CheckGarden = true;
            edit.Daily.Tasks = new[] { DailyTask.Garden, DailyTask.Mail };
            AccountConfig.WriteOptions(a.Account.config, "Daily", edit.Daily);
            a.SelectTask(change);
            Require(a.Account.config.Count == 0, "不得把一条龙设置带入切换声骸");
            a.Account.config["new"] = 3;
            a.SelectTask(null);
            Require(a.Account.config.Count == 0 && a.Account.task == null, "仅登录不应保存其他任务的草稿");
            a.SelectTask(daily);
            var runtime = AccountConfig.ForRun(global);
            AccountConfig.Apply(runtime, a.Account.config, "DailyTask");
            var fields = (Dictionary<string, object>)a.Account.config["daily"];
            Require(!fields.ContainsKey("checkgarden") && (string)fields["future_field"] == "keep" &&
                runtime.Daily.CheckGarden && runtime.Daily.GetTasks().SequenceEqual(edit.Daily.Tasks) &&
                !global.Daily.CheckGarden && b.Account.config.Count == 0, "独立草稿应恢复并保留未知字段");
            a.SelectTask(change);
            Require((int)a.Account.config["new"] == 3, "同一账号切换回任务应恢复先前的草稿");
            Require(AccountConfigForm.ChoiceIndex(null) == 0 && AccountConfigForm.ChoiceIndex(daily) > 0 &&
                AccountConfigForm.ChoiceIndex(new AccountTaskReference { module = "removed", @class = "DailyTask" }) == -1,
                "已移除的任务不能显示为已注册的同名任务或仅登录");
        }

        internal static void EditorHistory()
        {
            var history = new Dictionary<string, object>();
            string summary, detail;
            AccountConfigForm.HistoryText(history, "id", out summary, out detail);
            Require(summary == "未运行", "没有运行记录应显示未运行");
            foreach (string status in new[] { "success", "failed", "cancelled" })
            {
                history["id"] = new Dictionary<string, object> { { "status", status },
                    { "finished_at", "2026-01-05T11:20:30+08:00" }, { "message", "测试记录" } };
                AccountConfigForm.HistoryText(history, "id", out summary, out detail);
                string label = status == "success" ? "成功" : status == "failed" ? "失败" : "已停止";
                Require(summary.StartsWith(label) && summary.Contains("2026-01-") && detail.Contains("测试记录"),
                    "账号列表应显示记录状态、时间和详情");
            }
            ((Dictionary<string, object>)history["id"])["finished_at"] = "invalid";
            AccountConfigForm.HistoryText(history, "id", out summary, out detail);
            Require(summary.Contains("时间未知"), "账号记录中的无效时间不能阻塞编辑器");
        }

        internal static void Config()
        {
            CombatSettings settings = CombatSettings.Defaults();
            settings.Farm.Teleport = false;
            settings.Farm.BossIndex = 1;
            settings.Daily.CheckGarden = true;
            CombatSettings snapshot = settings;
            settings = AccountConfig.ForRun(snapshot);
            var config = new Dictionary<string, object>
            {
                { "Farm", new Dictionary<string, object> { { "Teleport", true }, { "BossIndex", 3 }, { "BossLevel", 90 } } },
                { "Daily", new Dictionary<string, object> { { "Farm", 1 }, { "MergeIfNeeded", true } } },
                { "Enhance", new Dictionary<string, object> { { "CritTotalMinimum", 15.5 }, { "ValidStats", new ArrayList { "暴击" } } } },
                { "Combat", new Dictionary<string, object> { { "AutoTarget", false }, { "Source", 1 } } }
            };
            int applied = AccountConfig.Apply(settings, config);
            Require(applied >= 8, "应应用多个账号配置项");
            Require(settings.Farm.Teleport && settings.Farm.BossIndex == 3 && settings.Farm.BossLevel == 90, "Farm 覆盖失败");
            Require(settings.Daily.Farm == DomainKind.Forgery && settings.Daily.MergeIfNeeded, "Daily 覆盖失败");
            Require(Math.Abs(settings.Enhance.CritTotalMinimum - 15.5) < 1e-6, "Enhance 数值覆盖失败");
            Require(settings.Enhance.ValidStats.Length == 1 && settings.Enhance.ValidStats[0] == "暴击", "数组覆盖失败");
            Require(!settings.AutoTarget && settings.Source == GameSource.Background, "Combat 覆盖失败");

            settings = snapshot;
            Require(!settings.Farm.Teleport && settings.Farm.BossIndex == 1, "恢复 Farm 失败");
            Require(settings.Daily.Farm == DomainKind.Tacet && settings.Daily.CheckGarden, "恢复 Daily 失败");
            Require(settings.AutoTarget, "恢复 Combat 失败");
        }

        internal static void Store()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ww-account-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new AccountStore(folder);
                Require(store.Load().Count == 0, "新的配置目录应返回空账号列表");
                var accounts = new List<AccountEntry>
                {
                    new AccountEntry
                    {
                        account_key = "a@x.com", enabled = true,
                        task = new AccountTaskReference { module = "TideCombat", @class = "DailyTask" },
                        config = new Dictionary<string, object>()
                    },
                    Account("Some Name"),
                    Account("1231234")
                };
                store.Save(accounts);
                List<AccountEntry> loaded = store.Load();
                Require(loaded.Select(entry => entry.account_key).SequenceEqual(accounts.Select(entry => entry.account_key)),
                    "保留账号、显示名、邮箱和已保存的执行次序");
                Require(loaded[0].task != null && loaded[0].task.@class == "DailyTask" && loaded[0].task.module == "src.task.DailyTask", "任务引用往返失败");

                byte[] original = File.ReadAllBytes(store.ConfigPath);
                bool threw = false;
                try
                {
                    store.Save(new List<AccountEntry> { Account("dup"), Account("dup") });
                }
                catch (InvalidDataException) { threw = true; }
                Require(threw && original.SequenceEqual(File.ReadAllBytes(store.ConfigPath)), "重复账号不应改写已保存的文件");
                threw = false;
                using (var held = new FileStream(store.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { store.Save(new List<AccountEntry> { Account("replacement") }); }
                    catch (IOException) { threw = true; }
                }
                Require(threw && original.SequenceEqual(File.ReadAllBytes(store.ConfigPath)) &&
                    Directory.GetFiles(folder, "*.tmp").Length == 0, "原子替换失败时保留旧配置并移除临时文件");

                const string broken = "{broken";
                File.WriteAllText(store.ConfigPath, broken);
                threw = false;
                try { store.Load(); }
                catch (InvalidDataException) { threw = true; }
                Require(threw && File.ReadAllText(store.ConfigPath) == broken, "损坏的配置需要报错并保留原始内容");
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        internal static void DailyConfig()
        {
            CombatSettings settings = CombatSettings.Defaults();
            var original = new[] { DailyTask.DailyRewards, DailyTask.Stamina, DailyTask.Garden };
            settings.Daily.Tasks = original;
            settings.Daily.NightmareMode = DailyNightmareMode.All;
            CombatSettings snapshot = settings;
            settings = AccountConfig.ForRun(snapshot);
            var serializer = new JavaScriptSerializer();

            int applied = AccountConfig.Apply(settings, serializer.Deserialize<Dictionary<string, object>>(
                "{\"Daily\":{\"CheckGarden\":false}}"));
            Require(applied == 1 && settings.Daily.GetTasks().SequenceEqual(original),
                "旧版账号字段不能覆盖已显式选择的全局任务列表");

            applied = AccountConfig.Apply(settings, serializer.Deserialize<Dictionary<string, object>>(
                "{\"Daily\":{\"Tasks\":[\"mail\",1,\"Nightmare\"],\"NightmareMode\":\"CaptureOnce\"}}"));
            var accountTasks = new[] { DailyTask.Mail, DailyTask.Stamina, DailyTask.Nightmare };
            Require(applied == 2 && settings.Daily.GetTasks().SequenceEqual(accountTasks),
                "按账号覆盖应读取有序任务数组并接受名称或数字");
            Require(settings.Daily.GetNightmareMode() == DailyNightmareMode.CaptureOnce, "梦魇模式覆盖失败");

            foreach (string invalid in new[] { "[]", "[\"Mail\",\"mail\"]", "[\"Mail\",77]", "[null]", "[\"Mail,Garden\"]", "[1.2]" })
            {
                applied = AccountConfig.Apply(settings, serializer.Deserialize<Dictionary<string, object>>(
                    "{\"Daily\":{\"Tasks\":" + invalid + ",\"NightmareMode\":-1}}"));
                Require(applied == 0 && settings.Daily.GetTasks().SequenceEqual(accountTasks) &&
                    settings.Daily.GetNightmareMode() == DailyNightmareMode.CaptureOnce,
                    "无效账号覆盖不能部分替换当前计划：" + invalid);
            }

            applied = AccountConfig.Apply(settings, serializer.Deserialize<Dictionary<string, object>>(
                "{\"Daily\":{\"Tasks\":null,\"NightmareMode\":null,\"AutoNightmare\":true,\"MergeIfNeeded\":true}}"));
            Require(applied == 4 && settings.Daily.Tasks == null &&
                !settings.Daily.GetTasks().Contains(DailyTask.Garden) && settings.Daily.GetTasks().Contains(DailyTask.Merge),
                "显式清空覆盖后应使用当前账号的旧版任务选项");
            Require(settings.Daily.GetNightmareMode() == DailyNightmareMode.All, "旧版账号全刷梦魇选项未生效");

            settings = snapshot;
            Require(settings.Daily.GetTasks().SequenceEqual(original) &&
                settings.Daily.GetNightmareMode() == DailyNightmareMode.All && settings.Daily.CheckGarden,
                "账号任务结束后应恢复全局的顺序、梦魇模式和旧版选项");
        }

        internal static void ReferencePlan()
        {
            Require(AccountTasks.Resolve(null) == null, "None 应保持只登录");
            Require(AccountTasks.ReferenceChoices.SequenceEqual(new[] { "None", "DailyTask", "FarmEchoTask", "NightmareNestTask",
                "TacetTask", "ForgeryTask", "SimulationTask", "MergeEchoTask", "EnhanceEchoTask", "ChangeEchoTask", "GardenTask" }) &&
                AccountTasks.Choices.Skip(AccountTasks.ReferenceChoices.Length).SequenceEqual(AccountTasks.ExtensionChoices) &&
                AccountTasks.DisplayChoices.Skip(AccountTasks.ReferenceChoices.Length).All(name => name.StartsWith("WW 扩展")),
                "参考注册顺序与 WW 显式扩展应分离，不注册递归任务");
            foreach (string name in AccountTasks.Choices.Skip(1))
            {
                Require(AccountTasks.Resolve(new AccountTaskReference { module = "src.task." + name, @class = name }) == name,
                    "注册目录读取原版任务引用");
            }
            foreach (var invalid in new[]
            {
                new AccountTaskReference { module = "TideCombat", @class = "DailyTask" },
                new AccountTaskReference { module = "src.task.ForgeryTask", @class = "TacetTask" },
                new AccountTaskReference { module = "TideCombat", @class = "MissingTask" },
                new AccountTaskReference { module = "TideCombat", @class = "MultiAccountConfigTask" }
            })
            {
                bool rejected = false;
                try { AccountTasks.Resolve(invalid); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected, "错误模块、未知任务和递归任务都必须在运行计划前拒绝");
            }

            var settings = CombatSettings.Defaults();
            settings.Farm.TeleportMode = FarmTeleportMode.WeeklyChallenge;
            settings.Farm.BossIndex = 4;
            Require(settings.Farm.IndexFor(FarmTeleportMode.WeeklyChallenge) == 4 &&
                settings.Farm.IndexFor(FarmTeleportMode.SpecialNightmare) == 1, "旧序号只继承到原来的分类");
            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "Teleport to Boss", "Special Nightmare" }, { "Which Special Nightmare to Teleport", 7 },
                { "Which Weekly Boss to Teleport", 3 }, { "Which Boss Challenge to Teleport", 9 },
                { "Boss", "Nightmare: Hecate" }, { "Echo Pickup Method", "Run in Circle" }
            }, "FarmEchoTask");
            Require(settings.Farm.Teleport && settings.Farm.IndexFor(settings.Farm.TeleportMode) == 7 &&
                settings.Farm.IndexFor(FarmTeleportMode.WeeklyChallenge) == 3 &&
                settings.Farm.IndexFor(FarmTeleportMode.BossChallenge) == 9 &&
                settings.Farm.Pickup == FarmPickupMode.Circle && settings.Farm.Boss == FarmBoss.NightmareHecate,
                "同一份原版配置分别记住三类序号和拾取方式");
            settings.Domain.Index = 12;
            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "Which Forgery Challenge to Farm", 7 }
            }, "ForgeryTask");
            Require(settings.Domain.ForKind(DomainKind.Tacet).Index == 12 &&
                settings.Domain.ForKind(DomainKind.Forgery).Index == 7,
                "独立副本任务的账号覆盖不得把其他副本的序号一并覆盖");
            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "Domain", new Dictionary<string, object> { { "Index", 8 } } }
            }, "ForgeryTask");
            Require(settings.Domain.ForKind(DomainKind.Tacet).Index == 12 &&
                settings.Domain.ForKind(DomainKind.Forgery).Index == 8,
                "老账号缺省 Kind 时嵌套 Index 应归属所选副本任务");
            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "目标属性", "暴击" }, { "必须有双爆", !settings.Enhance.RequireBothCrit }
            }, "ChangeEchoTask");
            Require(settings.Change.Target == "暴击" && settings.Enhance.RequireBothCrit == EnhanceOptions.Defaults().RequireBothCrit,
                "原版改主属性配置应直接应用，其他任务顶层字段不得污染强化配置");
            settings.Daily.Tasks = new[] { DailyTask.DailyRewards, DailyTask.Garden };
            CombatSettings original = settings;
            settings = AccountConfig.ForRun(original);
            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "Which to Farm", "Forgery Challenge" }
            }, "DailyTask");
            Require(settings.Daily.IsReferencePlan && settings.Daily.Farm == DomainKind.Forgery &&
                settings.Daily.GetTasks().Contains(DailyTask.Mail) && settings.Daily.GetTasks().Contains(DailyTask.BattlePass),
                "原版顶层配置不得继承全局 WW 自定义任务数组的缺失步骤");
            settings = AccountConfig.ForRun(original);
            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "Additional Tasks to Run After Daily Task", null }
            }, "DailyTask");
            Require(settings.Daily.IsReferencePlan && !settings.Daily.CheckGarden && !settings.Daily.AutoNightmare &&
                !settings.Daily.MergeIfNeeded && !settings.Daily.Farm4C && settings.Daily.GetTasks().Contains(DailyTask.Mail),
                "参考 null 附加任务列表表示无附加项，而非账号配置错误");
            AccountConfig.Apply(settings, new Dictionary<string, object> { { "Which to Farm", null } }, "NightmareNestTask");
            Require(settings.Nightmare.TacetNest && settings.Nightmare.Purification,
                "参考 null 梦魇选择回退两类");
            bool invalidSelection = false;
            try { AccountConfig.Apply(settings, new Dictionary<string, object> { { "Which to Farm", 17 } }, "NightmareNestTask"); }
            catch (InvalidDataException) { invalidSelection = true; }
            Require(invalidSelection, "非空且非列表的梦魇配置仍必须拒绝");
            settings = AccountConfig.ForRun(original);
            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "Which to Farm", "Cultivation Target" },
                { "Which Forgery Challenge to Farm", 8 },
                { "Material Selection", "Weapon EXP" },
                { "Additional Tasks to Run After Daily Task", new ArrayList { "Auto Farm all Nightmare Nest" } }
            }, "DailyTask");
            Require(settings.Daily.Farm == DomainKind.Cultivation && settings.Daily.ForgeryIndex == 8 &&
                settings.Daily.Material == DomainOptions.Materials[1] && settings.Daily.GetNightmareMode() == DailyNightmareMode.All &&
                !settings.Daily.GetTasks().Contains(DailyTask.Garden), "原版一条龙配置须应用为当前账号的实际任务计划");

            AccountConfig.Apply(settings, new Dictionary<string, object>
            {
                { "Ascension Materials", false },
                { "Echo Set Tacet Suppression", false },
                { "Cultivation", new Dictionary<string, object> { { "Skills", false } } }
            }, "CultivationTask");
            Require(!settings.Cultivation.Ascension && !settings.Cultivation.Skills && !settings.Cultivation.Tacet &&
                settings.Cultivation.Nightmare, "培养目标须读取原版和本地账号选项");
            settings = original;
            Require(settings.Cultivation.Ascension && settings.Cultivation.Skills && settings.Cultivation.Tacet &&
                settings.Daily.GetTasks().SequenceEqual(new[] { DailyTask.DailyRewards, DailyTask.Garden }),
                "培养目标和一条龙选项须在账号处理结束后恢复");
        }
    }
}
