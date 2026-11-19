using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

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
        }

        internal static void Config()
        {
            CombatSettings settings = CombatSettings.Defaults();
            settings.Farm.Teleport = false;
            settings.Farm.BossIndex = 1;
            settings.Daily.CheckGarden = true;
            AccountSnapshot snapshot = AccountConfig.Snapshot(settings);
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

            AccountConfig.Restore(settings, snapshot);
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
                var accounts = new List<AccountEntry>
                {
                    new AccountEntry
                    {
                        account_key = "a@x.com", enabled = true,
                        task = new AccountTaskReference { module = "TideCombat", @class = "DailyTask" },
                        config = new Dictionary<string, object>()
                    }
                };
                store.Save(accounts);
                List<AccountEntry> loaded = store.Load();
                Require(loaded.Count == 1 && loaded[0].account_key == "a@x.com", "配置读写往返失败");
                Require(loaded[0].task != null && loaded[0].task.@class == "DailyTask", "任务引用往返失败");

                bool threw = false;
                try
                {
                    store.Save(new List<AccountEntry>
                    {
                        new AccountEntry { account_key = "dup" },
                        new AccountEntry { account_key = "dup" }
                    });
                }
                catch (InvalidDataException) { threw = true; }
                Require(threw, "重复账号应拒绝");
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }
    }
}
