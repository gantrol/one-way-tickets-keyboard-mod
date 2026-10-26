using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace OneWayTickets.KeyboardMod.Installer
{
    // Installation writes are restricted to added loader/plugin files. The installer
    // neither reads nor changes the contents of the game's original assemblies.
    internal static class LoaderInstallation
    {
        internal const string PluginPath = "BepInEx/plugins/OneWayTickets.KeyboardMod/OneWayTickets.KeyboardMod.dll";
        internal const string PluginNoticePath = "BepInEx/plugins/OneWayTickets.KeyboardMod/ThirdPartyNotices.md";
        internal const string OwnershipPath = "BepInEx/OneWayTickets.KeyboardMod.installed-files.txt";
        private const string OwnershipHeader = "OneWayTickets.KeyboardMod.LoaderOwnership.v1";
        private const string BundleHash = "6C57A714A892EFB4E9E1FEF63D608A3391F30EC984031C465404E4EE381A0755";
        private static Dictionary<string, byte[]> _bundle;

        internal static bool IsInstalled(string gameRoot)
        {
            try
            {
                return File.Exists(PathFor(gameRoot, PluginPath)) && IsCompatibleLoader(gameRoot, Bundle());
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (BadImageFormatException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static bool HasFiles(string gameRoot)
        {
            return File.Exists(PathFor(gameRoot, PluginPath)) || File.Exists(PathFor(gameRoot, OwnershipPath));
        }

        internal static string Install(string gameRoot)
        {
            EnsureGameClosed(gameRoot);
            Dictionary<string, byte[]> bundle = Bundle();
            Dictionary<string, string> owned = ReadOwnership(gameRoot, bundle);
            List<KeyValuePair<string, byte[]>> writes = new List<KeyValuePair<string, byte[]>>();
            bool reuse = IsCompatibleLoader(gameRoot, bundle);

            if (!reuse)
            {
                // A partial installation may be completed, but no existing unknown
                // proxy, configuration, or core library is ever overwritten.
                foreach (KeyValuePair<string, byte[]> item in bundle)
                {
                    string path = PathFor(gameRoot, item.Key);
                    if (File.Exists(path))
                    {
                        if (Hash(File.ReadAllBytes(path)) != Hash(item.Value))
                            throw new InvalidOperationException("检测到现有加载器或同名文件与内置 BepInEx 不兼容，未覆盖任何文件：" + item.Key
                                + "\n请先确认已有加载器的配置；本安装器不会替换其他 Mod 的加载器。");
                    }
                    else
                    {
                        writes.Add(new KeyValuePair<string, byte[]>(item.Key, item.Value));
                        owned[item.Key] = Hash(item.Value);
                    }
                }
                string core = PathFor(gameRoot, "BepInEx/core");
                if (Directory.Exists(core))
                {
                    foreach (string file in Directory.GetFiles(core, "*.dll"))
                    {
                        string key = "BepInEx/core/" + Path.GetFileName(file);
                        if (!bundle.ContainsKey(key))
                            throw new InvalidOperationException("检测到未识别的 BepInEx 核心组件，未改变已有加载器：" + key);
                    }
                }
            }

            byte[] plugin = RequiredResource("OneWayTickets.KeyboardMod.dll");
            writes.Add(new KeyValuePair<string, byte[]>(PluginPath, plugin));
            owned[PluginPath] = Hash(plugin);
            byte[] notice = RequiredResource("BepInEx.ThirdPartyNotices.md");
            writes.Add(new KeyValuePair<string, byte[]>(PluginNoticePath, notice));
            owned[PluginNoticePath] = Hash(notice);

            string ini = "OneWayTickets.KeyboardMod.ini";
            if (!File.Exists(PathFor(gameRoot, ini)))
                writes.Add(new KeyValuePair<string, byte[]>(ini, RequiredResource(ini)));
            writes.Add(new KeyValuePair<string, byte[]>(OwnershipPath, OwnershipBytes(owned)));

            // Every conflict and payload has been checked before adding files.
            // The proxy is added last to enable the loader.
            writes.Sort(delegate(KeyValuePair<string, byte[]> left, KeyValuePair<string, byte[]> right)
            {
                if (left.Key == "winhttp.dll") return 1;
                if (right.Key == "winhttp.dll") return -1;
                return StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key);
            });
            ApplyWrites(gameRoot, writes);
            return "安装成功。";
        }

        internal static string Uninstall(string gameRoot)
        {
            EnsureGameClosed(gameRoot);
            Dictionary<string, byte[]> bundle = Bundle();
            Dictionary<string, string> owned = ReadOwnership(gameRoot, bundle);
            bool removeLoader = OwnsUnchangedLoader(gameRoot, bundle, owned) && !HasOtherMods(gameRoot, bundle);
            DeleteIfPresent(PathFor(gameRoot, PluginPath));
            DeleteIfPresent(PathFor(gameRoot, PluginNoticePath));
            if (removeLoader)
            {
                // Disable loading before deleting the rest of our unchanged files.
                DeleteIfPresent(PathFor(gameRoot, "winhttp.dll"));
                foreach (string relative in bundle.Keys)
                {
                    string path = PathFor(gameRoot, relative);
                    if (File.Exists(path) && Hash(File.ReadAllBytes(path)) == owned[relative]) File.Delete(path);
                }
            }
            DeleteIfPresent(PathFor(gameRoot, OwnershipPath));
            DeleteEmptyDirectory(PathFor(gameRoot, "BepInEx/plugins/OneWayTickets.KeyboardMod"));
            DeleteEmptyDirectory(PathFor(gameRoot, "BepInEx/plugins"));
            DeleteEmptyDirectory(PathFor(gameRoot, "BepInEx/core"));
            DeleteEmptyDirectory(PathFor(gameRoot, "BepInEx"));

            return "已卸载键盘 Mod，原版游戏程序集和改键配置已保留。"
                + (removeLoader ? "\n已移除本安装器添加且未被改动的加载器文件。"
                    : "\n现有、共享或已被修改的加载器文件已保留。");
        }

        private static Dictionary<string, byte[]> Bundle()
        {
            if (_bundle != null) return _bundle;
            byte[] archive = RequiredResource("BepInEx.Loader.zip");
            if (Hash(archive) != BundleHash) throw new InvalidOperationException("内置 BepInEx 加载器校验失败。");
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (MemoryStream stream = new MemoryStream(archive, false))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string relative = entry.FullName.Replace('\\', '/');
                    bool rootFile = relative == "winhttp.dll" || relative == "doorstop_config.ini" || relative == ".doorstop_version";
                    bool coreFile = relative.StartsWith("BepInEx/core/", StringComparison.Ordinal)
                        && (relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                    if ((!rootFile && !coreFile) || relative.IndexOf("..", StringComparison.Ordinal) >= 0) continue;
                    using (Stream input = entry.Open())
                    using (MemoryStream output = new MemoryStream())
                    {
                        input.CopyTo(output);
                        files.Add(relative, output.ToArray());
                    }
                }
            }
            if (!files.ContainsKey("winhttp.dll") || !files.ContainsKey("doorstop_config.ini")
                || !files.ContainsKey("BepInEx/core/BepInEx.dll") || !files.ContainsKey("BepInEx/core/BepInEx.Preloader.dll"))
                throw new InvalidOperationException("内置 BepInEx 加载器不完整。");
            files.Add("BepInEx/core/OneWayTickets.ThirdPartyNotices.md", RequiredResource("BepInEx.ThirdPartyNotices.md"));
            files.Add("BepInEx/core/UnityDoorstop.Source.zip", RequiredResource("UnityDoorstop.Source.zip"));
            _bundle = files;
            return files;
        }

        private static bool IsCompatibleLoader(string gameRoot, Dictionary<string, byte[]> bundle)
        {
            string proxy = PathFor(gameRoot, "winhttp.dll");
            string config = PathFor(gameRoot, "doorstop_config.ini");
            string core = PathFor(gameRoot, "BepInEx/core/BepInEx.dll");
            if (!File.Exists(proxy) || !File.Exists(config) || !File.Exists(core)) return false;
            if (Hash(File.ReadAllBytes(proxy)) != Hash(bundle["winhttp.dll"])) return false;
            Version version = AssemblyName.GetAssemblyName(core).Version;
            if (version == null || version.Major != 5) return false;
            foreach (string relative in bundle.Keys)
            {
                if (relative.StartsWith("BepInEx/core/", StringComparison.Ordinal)
                    && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(PathFor(gameRoot, relative))) return false;
            }

            string section = "";
            string enabled = null;
            string target = null;
            foreach (string raw in File.ReadAllLines(config))
            {
                string line = raw.Trim();
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                if (!string.Equals(section, "General", StringComparison.OrdinalIgnoreCase)) continue;
                int separator = line.IndexOf('=');
                if (separator < 0) continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (string.Equals(key, "enabled", StringComparison.OrdinalIgnoreCase)) enabled = value;
                if (string.Equals(key, "target_assembly", StringComparison.OrdinalIgnoreCase)) target = value.Replace('\\', '/');
            }
            return string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)
                && string.Equals(target, "BepInEx/core/BepInEx.Preloader.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static bool OwnsUnchangedLoader(string gameRoot, Dictionary<string, byte[]> bundle, Dictionary<string, string> owned)
        {
            foreach (string relative in bundle.Keys)
            {
                string expected;
                if (!owned.TryGetValue(relative, out expected)) return false;
                string path = PathFor(gameRoot, relative);
                if (File.Exists(path) && Hash(File.ReadAllBytes(path)) != expected) return false;
            }
            return true;
        }

        private static bool HasOtherMods(string gameRoot, Dictionary<string, byte[]> bundle)
        {
            foreach (string directory in new string[] { "BepInEx/plugins", "BepInEx/patchers", "BepInEx/core" })
            {
                string path = PathFor(gameRoot, directory);
                if (!Directory.Exists(path)) continue;
                Stack<string> pending = new Stack<string>();
                pending.Push(path);
                while (pending.Count > 0)
                {
                    foreach (string entry in Directory.GetFileSystemEntries(pending.Pop()))
                    {
                        FileAttributes attributes = File.GetAttributes(entry);
                        // A mod manager may link external plugin directories. Do not
                        // traverse the link, and preserve the loader for that mod.
                        if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                        string relative = entry.Substring(Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                        if (string.Equals(relative, PluginPath, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(relative, PluginNoticePath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (directory == "BepInEx/core" && bundle.ContainsKey(relative)) continue;
                        return true;
                    }
                }
            }
            return false;
        }

        private static Dictionary<string, string> ReadOwnership(string gameRoot, Dictionary<string, byte[]> bundle)
        {
            Dictionary<string, string> owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = PathFor(gameRoot, OwnershipPath);
            if (!File.Exists(path)) return owned;
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0] != OwnershipHeader) throw new InvalidOperationException("加载器安装记录无法识别，未删除或覆盖文件。");
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] fields = lines[i].Split('\t');
                if (fields.Length != 2 || !IsHash(fields[0])
                    || (!bundle.ContainsKey(fields[1]) && fields[1] != PluginPath && fields[1] != PluginNoticePath))
                    throw new InvalidOperationException("加载器安装记录包含未知文件，未删除或覆盖文件。");
                owned[fields[1]] = fields[0];
            }
            return owned;
        }

        private static byte[] OwnershipBytes(Dictionary<string, string> owned)
        {
            List<string> paths = new List<string>(owned.Keys);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            StringBuilder contents = new StringBuilder(OwnershipHeader + "\n");
            foreach (string path in paths) contents.Append(owned[path]).Append('\t').Append(path).Append('\n');
            return Encoding.UTF8.GetBytes(contents.ToString());
        }

        private static bool IsHash(string value)
        {
            if (value.Length != 64) return false;
            foreach (char character in value)
                if ((character < '0' || character > '9') && (character < 'A' || character > 'F')) return false;
            return true;
        }

        private static byte[] RequiredResource(string name)
        {
            byte[] bytes = Program.ReadEmbeddedResource(name);
            if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("安装包缺少资源：" + name);
            return bytes;
        }

        private static void ApplyWrites(string gameRoot, List<KeyValuePair<string, byte[]>> writes)
        {
            List<KeyValuePair<string, byte[]>> previous = new List<KeyValuePair<string, byte[]>>();
            try
            {
                foreach (KeyValuePair<string, byte[]> item in writes)
                {
                    string path = PathFor(gameRoot, item.Key);
                    previous.Add(new KeyValuePair<string, byte[]>(path, File.Exists(path) ? File.ReadAllBytes(path) : null));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    WriteFileAtomically(path, item.Value);
                }
            }
            catch
            {
                for (int i = previous.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (previous[i].Value == null) DeleteIfPresent(previous[i].Key);
                        else WriteFileAtomically(previous[i].Key, previous[i].Value);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                throw;
            }
        }

        internal static void WriteFileAtomically(string path, byte[] bytes)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static string PathFor(string gameRoot, string relative)
        {
            string root = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("拒绝写入游戏目录以外的路径。");
            string current = target;
            while (true)
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("检测到链接或目录联接，未写入或删除文件：" + current
                            + "\n请使用普通目录安装，或通过管理该链接的 Mod 管理器操作。");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
                current = Path.GetDirectoryName(current);
            }
            return target;
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        private static void EnsureGameClosed(string gameRoot)
        {
            string executable = Path.Combine(Path.GetFullPath(gameRoot), "One Way Tickets.exe");
            foreach (Process process in Process.GetProcessesByName("One Way Tickets"))
            {
                using (process)
                {
                    try
                    {
                        if (string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("请先关闭《再再启程》，再安装或卸载 Mod。");
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        throw new InvalidOperationException("无法确认正在运行的游戏状态，请先关闭《再再启程》。");
                    }
                }
            }
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void DeleteEmptyDirectory(string path)
        {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) Directory.Delete(path);
        }
    }
}
