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
        public bool enabled { get; set; } = true;
        public AccountTaskReference task { get; set; }
        public Dictionary<string, object> config { get; set; }
    }

    public sealed class AccountDocument
    {
        public List<AccountEntry> accounts { get; set; }
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
            return text == null ? null : text.Replace("*", string.Empty).Trim();
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
            AccountDocument document;
            try { document = serializer.Deserialize<AccountDocument>(text); }
            catch (Exception) { throw new InvalidDataException("多账号配置不是有效的 JSON。"); }
            if (document == null || document.accounts == null)
                throw new InvalidDataException("多账号配置缺少 accounts 列表。");
            Validate(document.accounts);
            return document.accounts;
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
                return results ?? new Dictionary<string, object>();
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
                if (account.account_key.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                    throw new InvalidDataException("账号 ID 不能包含控制字符。");
                if (!seen.Add(account.account_key))
                    throw new InvalidDataException("账号 ID 重复：" + account.account_key);
                if (account.task != null &&
                    (string.IsNullOrEmpty(account.task.module) || string.IsNullOrEmpty(account.task.@class)))
                    throw new InvalidDataException("任务引用必须包含 module 与 class。");
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
