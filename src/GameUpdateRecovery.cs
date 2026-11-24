using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace TideCombat
{
    internal sealed class GameUpdateRecovery
    {
        private const string GameExecutable = "Wuthering Waves.exe";
        private const string UserAssist = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";
        private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private static readonly string[] RegisteredLocations =
            { "InstallPath", "InstallLocation", "LauncherPath", "DisplayIcon", "UninstallString" };
        private readonly GameWindow window;
        private readonly CancellationToken token;
        private readonly bool cloud;
        private readonly Action<string> notice;

        internal GameUpdateRecovery(GameWindow window, CancellationToken token, bool cloud, Action<string> notice = null)
        {
            this.window = window;
            this.token = token;
            this.cloud = cloud;
            this.notice = notice;
        }

        internal bool Confirm(Func<bool> click)
        {
            string running = cloud ? null : ProcessImage(window.ProcessId);
            GameLanguage language = window.Language;
            return Recover(click, ms => !token.WaitHandle.WaitOne(ms), () =>
            {
                if (cloud || token.IsCancellationRequested) return;
                RestartIfClosed(running, language);
            });
        }

        internal static bool Recover(Func<bool> confirm, Func<int, bool> wait, Action start)
        {
            if (!wait(200) || !confirm()) return false;
            if (wait(60000))
            {
                start();
                wait(30000);
            }
            return true;
        }

        private static string ProcessImage(uint pid)
        {
            try
            {
                using (Process game = Process.GetProcessById((int)pid))
                    return string.Equals(game.ProcessName, GameWindow.LocalProcessName, StringComparison.OrdinalIgnoreCase)
                        ? game.MainModule.FileName : null;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (Win32Exception) { return null; }
        }

        internal static string LauncherPath(string running)
        {
            if (string.IsNullOrEmpty(running) || !string.Equals(Path.GetFileNameWithoutExtension(running),
                GameWindow.LocalProcessName, StringComparison.OrdinalIgnoreCase)) return null;
            DirectoryInfo game = Directory.GetParent(running);
            for (int i = 0; i < 3 && game != null; i++) game = game.Parent;
            return game == null ? null : Path.Combine(game.FullName, GameExecutable);
        }

        private void RestartIfClosed(string running, GameLanguage language)
        {
            string exe = ResolveLauncher(running, FindRecentLauncher, FindRegisteredLauncher);
            if (exe == null || !File.Exists(exe))
            {
                RunLog.Info("登录", "游戏更新后未找到 Wuthering Waves.exe，等待游戏窗口");
                return;
            }
            bool runningAlready = false;
            foreach (Process game in Process.GetProcessesByName(GameWindow.LocalProcessName))
                using (game)
                    if (running == null || string.Equals(ProcessImage((uint)game.Id), running, StringComparison.OrdinalIgnoreCase))
                        runningAlready = true;
            if (runningAlready) return;
            ProcessStartInfo start = PrepareLaunch(exe, language, message =>
            {
                RunLog.Warn("登录", message);
                if (notice != null) notice(message);
            });
            try { using (Process game = Process.Start(start)) { } }
            catch (Win32Exception error) { RunLog.Error("登录", "更新后启动游戏失败", error); }
            catch (InvalidOperationException error) { RunLog.Error("登录", "更新后启动游戏失败", error); }
        }

        internal static string ResolveLauncher(string running, Func<string> recent, Func<string> registered)
        {
            return string.IsNullOrEmpty(running) ? recent() ?? registered() : LauncherPath(running);
        }

        private static string FindRecentLauncher()
        {
            var records = new List<KeyValuePair<string, byte[]>>();
            using (RegistryKey root = ReadRegistry(() => Registry.CurrentUser.OpenSubKey(UserAssist)))
            {
                if (root == null) return null;
                foreach (string name in ReadRegistry(root.GetSubKeyNames) ?? new string[0])
                using (RegistryKey count = ReadRegistry(() => root.OpenSubKey(name + @"\Count")))
                {
                    if (count == null) continue;
                    foreach (string value in ReadRegistry(count.GetValueNames) ?? new string[0])
                        records.Add(new KeyValuePair<string, byte[]>(value, ReadRegistry(() => count.GetValue(value)) as byte[]));
                }
            }
            return MostRecentLauncher(records);
        }

        internal static string MostRecentLauncher(IEnumerable<KeyValuePair<string, byte[]>> records)
        {
            string selected = null;
            ulong latest = 0;
            foreach (var record in records)
            {
                char[] decoded = record.Key.ToCharArray();
                for (int i = 0; i < decoded.Length; i++)
                {
                    char value = decoded[i];
                    if (value >= 'A' && value <= 'Z') decoded[i] = (char)('A' + (value - 'A' + 13) % 26);
                    else if (value >= 'a' && value <= 'z') decoded[i] = (char)('a' + (value - 'a' + 13) % 26);
                }
                string path = Environment.ExpandEnvironmentVariables(new string(decoded));
                if (!path.EndsWith("\\" + GameExecutable, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    continue;
                ulong timestamp = record.Value != null && record.Value.Length >= 68 ? BitConverter.ToUInt64(record.Value, 60) : 0;
                if (selected == null || timestamp > latest || timestamp == latest && string.CompareOrdinal(path, selected) > 0)
                {
                    selected = path;
                    latest = timestamp;
                }
            }
            return selected;
        }

        private static string FindRegisteredLauncher()
        {
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            using (RegistryKey baseKey = ReadRegistry(() => RegistryKey.OpenBaseKey(hive, view)))
            using (RegistryKey root = baseKey == null ? null : ReadRegistry(() => baseKey.OpenSubKey(Uninstall)))
            {
                if (root == null) continue;
                foreach (string name in ReadRegistry(root.GetSubKeyNames) ?? new string[0])
                using (RegistryKey entry = ReadRegistry(() => root.OpenSubKey(name)))
                {
                    if (entry == null) continue;
                    string display = name + " " + ReadRegistry(() => entry.GetValue("DisplayName"));
                    if (!display.Contains("鸣潮") && !display.Contains("鳴潮") &&
                        display.IndexOf("wuthering waves", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var values = new Dictionary<string, string>();
                    foreach (string location in RegisteredLocations)
                    {
                        string registered = ReadRegistry(() => entry.GetValue(location)) as string;
                        values[location] = registered;
                        string found = FindLauncherNear(registered);
                        if (found != null) return found;
                    }
                    // A relative field can only be considered if this same
                    // matching installation record supplies a single trusted
                    // absolute install directory. Never resolve against the
                    // utility's current directory or search the PATH.
                    string relative = ResolveRegisteredRelativeLauncher(values);
                    if (relative != null) return relative;
                }
            }
            return null;
        }

        internal static string ResolveRegisteredRelativeLauncher(IDictionary<string, string> values)
        {
            if (values == null) return null;
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { "InstallPath", "InstallLocation" })
            {
                string value;
                if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) continue;
                try
                {
                    string root = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                    if (!IsFullyQualifiedPath(root) || !Directory.Exists(root)) continue;
                    root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (root.Length == 0 || string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase) ||
                        !NoReparsePoints(root, null)) continue;
                    roots.Add(root);
                }
                catch (Exception error)
                {
                    if (!IsPathError(error)) throw;
                }
            }
            if (roots.Count != 1) return null;
            string basePath = roots.Single();
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string location in RegisteredLocations)
            {
                string value;
                if (!values.TryGetValue(location, out value) || string.IsNullOrWhiteSpace(value)) continue;
                string path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                if (Path.IsPathRooted(path)) continue;
                string found = FindLauncherNear(value, basePath);
                if (found != null) candidates.Add(found);
            }
            return candidates.Count == 1 ? candidates.Single() : null;
        }

        internal static string FindLauncherNear(string registered, string trustedInstallDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(registered)) return null;
            string path = Environment.ExpandEnvironmentVariables(registered.Trim().Trim('"'));
            int extension = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (extension >= 0) path = path.Substring(0, extension + 4);
            try
            {
                bool relative = !Path.IsPathRooted(path);
                string root = null;
                if (!IsFullyQualifiedPath(path))
                {
                    if (!relative || string.IsNullOrEmpty(trustedInstallDirectory) ||
                        !IsFullyQualifiedPath(trustedInstallDirectory) || !Directory.Exists(trustedInstallDirectory)) return null;
                    root = Path.GetFullPath(trustedInstallDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (root.Length == 0 || string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase) ||
                        !NoReparsePoints(root, null)) return null;
                    // Refuse drive-relative/device paths, environment leftovers,
                    // traversal and ambiguous roots even with an install anchor.
                    if (path.IndexOf(':') >= 0 || path.IndexOf('%') >= 0 || path.IndexOf('$') >= 0 ||
                        path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..")) return null;
                    path = Path.GetFullPath(Path.Combine(root, path));
                    if (!WithinDirectory(root, path)) return null;
                }
                var folder = new DirectoryInfo(extension >= 0 ? Path.GetDirectoryName(path) : path);
                string first = Path.Combine(folder.FullName, GameExecutable);
                string nested = Path.Combine(folder.FullName, "Wuthering Waves Game", GameExecutable);
                string sibling = folder.Parent == null ? null : Path.Combine(folder.Parent.FullName, "Wuthering Waves Game", GameExecutable);
                if (root == null) return File.Exists(first) ? first : File.Exists(nested) ? nested : File.Exists(sibling) ? sibling : null;
                string[] candidates = new[] { first, nested, sibling }.Where(candidate => candidate != null &&
                    WithinDirectory(root, candidate) && File.Exists(candidate) && NoReparsePoints(candidate, root))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return candidates.Length == 1 ? candidates[0] : null;
            }
            catch (Exception error)
            {
                if (!IsPathError(error)) throw;
                return null;
            }
        }

        private static bool IsFullyQualifiedPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return false;
            if (Path.DirectorySeparatorChar != '\\') return true;
            // Windows drive-relative paths (C:foo) and root-relative paths
            // (\foo) depend on process state, not a verified install path.
            return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/') || path.StartsWith("\\\\", StringComparison.Ordinal) &&
                !path.StartsWith("\\\\?\\", StringComparison.Ordinal) && !path.StartsWith("\\\\.\\", StringComparison.Ordinal);
        }

        private static bool WithinDirectory(string root, string path)
        {
            string full = Path.GetFullPath(path);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool NoReparsePoints(string path, string boundary)
        {
            try
            {
                string current = Path.GetFullPath(path);
                while (!string.IsNullOrEmpty(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                    if (boundary != null && string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase)) return true;
                    string parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                        return boundary == null;
                    current = parent;
                }
                return boundary == null;
            }
            catch (Exception error)
            {
                if (!IsPathError(error)) throw;
                return false;
            }
        }

        private static bool IsPathError(Exception error)
        {
            return error is ArgumentException || error is NotSupportedException || error is IOException ||
                error is UnauthorizedAccessException || error is System.Security.SecurityException;
        }

        private static T ReadRegistry<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Security.SecurityException) { return null; }
            catch (ArgumentException) { return null; }
        }

        internal static ProcessStartInfo StartInfo(string executable, out bool fallback)
        {
            string package = DetectPackage(executable);
            fallback = package == null;
            return new ProcessStartInfo(executable, "-krqlv=" + (package ?? "hd"))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
            };
        }

        internal static ProcessStartInfo PrepareLaunch(string executable, GameLanguage language, Action<string> warning,
            CultureInfo uiCulture = null)
        {
            bool fallback;
            ProcessStartInfo start = StartInfo(executable, out fallback);
            if (!fallback) return start;
            CultureInfo culture = uiCulture ?? CultureInfo.CurrentUICulture;
            string locale = culture.TwoLetterISOLanguageName;
            switch (locale)
            {
                case "es":
                    warning("No se pudo leer la calidad de recursos del iniciador. Se iniciará con recursos HD.");
                    break;
                case "ja":
                    warning("ランチャーのリソース品質を読み取れなかったため、HD リソースで起動します。");
                    break;
                case "ko":
                    warning("런처의 리소스 품질을 확인할 수 없어 HD 리소스로 시작합니다.");
                    break;
                default:
                    warning(language == GameLanguage.English || language == GameLanguage.Unknown && locale == "en"
                        ? "Couldn't read the launcher's asset quality. Starting with HD assets."
                        : language == GameLanguage.TraditionalChinese || language == GameLanguage.Unknown &&
                            (culture.Name == "zh-TW" || culture.Name == "zh-HK" || culture.Name == "zh-Hant")
                            ? "無法讀取啟動器的資源畫質，將使用高畫質資源啟動。"
                            : "无法读取启动器的资源画质，将使用高清资源启动。");
                    break;
            }
            return start;
        }

        private static Dictionary<string,object> ReadObject(string path, bool encoded)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (encoded)
                {
                    string text = Encoding.ASCII.GetString(data);
                    if (text.Any(char.IsWhiteSpace)) return null;
                    data = Convert.FromBase64String(text);
                    for (int i = 0; i < data.Length; i++) data[i] ^= 0x63;
                }
                string json = new UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF');
                return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (FormatException) { return null; }
        }

        private static object Value(Dictionary<string,object> values, string key)
        {
            object result;
            return values != null && values.TryGetValue(key, out result) ? result : null;
        }

        private static string Package(string value)
        {
            string name = value == null ? null : value.ToLowerInvariant();
            return name == "sd" || name == "hd" || name == "uhd" ? name : null;
        }

        internal static string DetectPackage(string launcher)
        {
            DirectoryInfo game = string.IsNullOrEmpty(launcher) ? null : Directory.GetParent(launcher);
            if (game == null || game.Parent == null) return null;
            var state = ReadObject(Path.Combine(game.Parent.FullName, "kr_game_cache", "platform_ui_state.bin"), true);
            var active = Value(state, "activeGameScope") as Dictionary<string,object>;
            string selected = Value(active, "gameIdentity") as string == "Aki" ? Package(Value(active, "bundleName") as string) : null;
            if (selected != null) return selected;
            var recent = Value(Value(state, "recentGames") as Dictionary<string,object>, "Aki") as Dictionary<string,object>;
            selected = Package(Value(recent, "bundleName") as string);
            if (selected != null) return selected;
            var manifest = ReadObject(Path.Combine(game.FullName, "launcherDownloadConfig.json"), false);
            var bundles = Value(manifest, "bundles") as Dictionary<string,object>;
            if (bundles == null) return null;
            string[] known = bundles.Keys.Select(Package).Where(p => p != null).Distinct().ToArray();
            return known.Length == 1 ? known[0] : null;
        }
    }
}
