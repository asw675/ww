using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TideCombat
{
    public sealed class AccountTaskReference
    {
        public string module { get; set; }
        public string @class { get; set; }
    }

    public sealed class AccountEntry
    {
        public string account_key { get; set; }
        public bool enabled { get; set; }
        public AccountTaskReference task { get; set; }
        public Dictionary<string, object> config { get; set; }

        public AccountEntry() { enabled = true; }
    }

    public sealed class AccountDocument
    {
        public List<AccountEntry> accounts { get; set; }
    }

    internal static class AccountTasks
    {
        internal static readonly string[] Choices =
        {
            "None", "DailyTask", "FarmEchoTask", "EnhanceEchoTask", "TacetTask", "ForgeryTask",
            "SimulationTask", "NightmareNestTask", "GardenTask", "MergeEchoTask", "ChangeEchoTask",
            "CultivationTask", "RahaiBlocksTask", "GalGameTask"
        };

        internal static string Resolve(AccountTaskReference reference)
        {
            if (reference == null)
                return null;
            string name = reference.@class;
            bool registered = Array.IndexOf(Choices, name) > 0;
            bool knownModule = reference.module == "src.task." + name;
            if (!registered || !knownModule)
                throw new InvalidDataException("多账号计划包含未注册的任务 " + reference.module + "." + name + "。");
            return name;
        }
    }

    internal static class AccountKeys
    {
        internal static string Normalize(string account)
        {
            if (string.IsNullOrEmpty(account))
                return account;
            return account.ToLowerInvariant().Replace('0', 'o').Replace(".con", ".com");
        }

        internal static string FromOcr(string text)
        {
            return string.IsNullOrEmpty(text) ? null : text.Replace("*", string.Empty).Trim();
        }

        internal static int FindSelection(IList<string> items, string target)
        {
            if (!string.IsNullOrEmpty(target))
                for (int i = items.Count - 1; i >= 0; i--)
                    if (string.Equals(FromOcr(items[i]), target, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    // Reads and writes configs/MultiAccountConfig.json with the same schema and
    // validation rules as the reference account store.
    internal sealed class AccountStore
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        internal string ConfigPath { get; private set; }
        internal string ResultsPath { get; private set; }

        internal AccountStore(string folder)
        {
            string directory = folder ?? "configs";
            ConfigPath = Path.Combine(directory, "MultiAccountConfig.json");
            ResultsPath = Path.Combine(directory, "MultiAccountResults.json");
        }

        internal List<AccountEntry> Load()
        {
            if (!File.Exists(ConfigPath))
                return new List<AccountEntry>();
            string text = File.ReadAllText(ConfigPath, Encoding.UTF8);
            Dictionary<string, object> document;
            try { document = serializer.DeserializeObject(text) as Dictionary<string, object>; }
            catch (ArgumentException error) { throw new InvalidDataException("多账号配置不是有效的 JSON。", error); }
            catch (InvalidOperationException error) { throw new InvalidDataException("多账号配置不是有效的 JSON。", error); }
            object raw;
            if (document == null || !document.TryGetValue("accounts", out raw) || !(raw is object[]))
                throw new InvalidDataException("多账号配置缺少 accounts 列表。");
            var accounts = new List<AccountEntry>();
            foreach (object item in (object[])raw)
            {
                var row = item as Dictionary<string, object>;
                object key, task, config, enabled;
                if (row == null || !row.TryGetValue("account_key", out key) || !(key is string) ||
                    !row.TryGetValue("task", out task) || !row.TryGetValue("config", out config) ||
                    !(config is Dictionary<string, object>))
                    throw new InvalidDataException("每个账号必须包含文本 ID、任务和配置对象。");
                if (!row.TryGetValue("enabled", out enabled)) enabled = true;
                if (!(enabled is bool)) throw new InvalidDataException("账号启用状态必须为布尔值。");
                var entry = new AccountEntry { account_key = (string)key, enabled = (bool)enabled, config = (Dictionary<string, object>)config };
                if (task != null)
                {
                    var reference = task as Dictionary<string, object>;
                    object module, type;
                    if (reference == null || reference.Count != 2 || !reference.TryGetValue("module", out module) ||
                        !reference.TryGetValue("class", out type) || !(module is string) || !(type is string))
                        throw new InvalidDataException("账号任务引用必须包含 module 和 class 两个文本字段。");
                    entry.task = new AccountTaskReference { module = (string)module, @class = (string)type };
                }
                if (entry.task != null && entry.task.module == "TideCombat" &&
                    Array.IndexOf(AccountTasks.Choices, entry.task.@class) > 0)
                    entry.task.module = "src.task." + entry.task.@class;
                accounts.Add(entry);
            }
            Validate(accounts);
            return accounts;
        }

        internal void Save(List<AccountEntry> accounts)
        {
            Validate(accounts);
            var document = new AccountDocument { accounts = accounts };
            WriteAtomic(ConfigPath, serializer.Serialize(document));
        }

        internal Dictionary<string, object> LoadResults()
        {
            if (!File.Exists(ResultsPath))
                return new Dictionary<string, object>();
            try
            {
                var results = serializer.Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(ResultsPath, Encoding.UTF8));
                if (results == null) throw new InvalidDataException();
                foreach (object item in results.Values)
                {
                    var record = item as Dictionary<string, object>;
                    object status, finished;
                    if (record == null || !record.TryGetValue("status", out status) ||
                        Array.IndexOf(new[] { "success", "failed", "cancelled" }, status as string) < 0 ||
                        !record.TryGetValue("finished_at", out finished) || !(finished is string))
                        throw new InvalidDataException();
                }
                return results;
            }
            catch (Exception) { throw new InvalidDataException("账号运行记录格式无效。"); }
        }

        internal void RecordResult(AccountEntry account, string status, string message)
        {
            var results = LoadResults();
            results[account.account_key] = new Dictionary<string, object>
            {
                { "status", status },
                { "finished_at", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz") },
                { "task", account.task == null ? null : new Dictionary<string, object>
                    { { "module", account.task.module }, { "class", account.task.@class } } },
                { "message", message ?? string.Empty }
            };
            WriteAtomic(ResultsPath, serializer.Serialize(results));
        }

        internal static void Validate(List<AccountEntry> accounts)
        {
            if (accounts == null)
                throw new InvalidDataException("accounts 必须是数组。");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccountEntry account in accounts)
            {
                if (account == null || string.IsNullOrEmpty(account.account_key) ||
                    account.account_key != account.account_key.Trim() || account.account_key.Contains("*"))
                    throw new InvalidDataException("账号 ID 需使用登录列表去掉 * 后的文本，且不能为空或首尾空格。");
                if (Array.Exists(account.account_key.ToCharArray(), c => c < 32))
                    throw new InvalidDataException("账号 ID 不能包含控制字符。");
                if (!seen.Add(account.account_key))
                    throw new InvalidDataException("账号 ID 重复：" + account.account_key);
                if (account.task != null &&
                    (account.task.module == null || account.task.@class == null))
                    throw new InvalidDataException("任务引用必须包含 module 与 class。");
                if (account.task != null && account.task.module == "src.task." + account.task.@class &&
                    (account.task.@class == "MultiAccountDailyTask" || account.task.@class == "MultiAccountConfigTask"))
                    throw new InvalidDataException("多账号计划不能调用多账号任务。");
                if (account.config == null)
                    throw new InvalidDataException("账号任务配置必须是对象。");
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
