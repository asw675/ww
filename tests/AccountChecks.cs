using System;
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
