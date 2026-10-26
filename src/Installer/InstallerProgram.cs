using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("《再再启程》键盘减负 Mod 一键安装器")]
[assembly: AssemblyDescription("《再再启程 One Way Tickets》双手键盘与减负辅助 Mod 一键安装/卸载工具")]
[assembly: AssemblyProduct("OneWayTickets.KeyboardMod.Installer")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace OneWayTickets.KeyboardMod.Installer
{
    internal static class Program
    {
        private static readonly string GameFolderName = "\u518D\u518D\u542F\u7A0B One Way Tickets";

        [STAThread]
        private static int Main(string[] args)
        {
            // Command-line silent mode support:
            //   --install [gameRoot]
            //   --uninstall [gameRoot]
            if (args != null && args.Length > 0)
            {
                string cmd = args[0].Trim().ToLowerInvariant();
                string explicitRoot = args.Length > 1 ? args[1] : null;
                if (cmd == "--install" || cmd == "-i" || cmd == "/install")
                {
                    try
                    {
                        string root = ResolveGameRoot(explicitRoot, false);
                        if (string.IsNullOrEmpty(root))
                        {
                            Console.Error.WriteLine("[ERROR] Could not locate 'One Way Tickets' directory.");
                            return 1;
                        }
                        string msg = PerformInstall(root);
                        Console.WriteLine(msg);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("[ERROR] " + ex.Message);
                        return 1;
                    }
                }
                if (cmd == "--uninstall" || cmd == "-u" || cmd == "/uninstall")
                {
                    try
                    {
                        string root = ResolveGameRoot(explicitRoot, false);
                        if (string.IsNullOrEmpty(root))
                        {
                            Console.Error.WriteLine("[ERROR] Could not locate 'One Way Tickets' directory.");
                            return 1;
                        }
                        string msg = PerformUninstall(root);
                        Console.WriteLine(msg);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("[ERROR] " + ex.Message);
                        return 1;
                    }
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
            return 0;
        }

        internal static byte[] ReadEmbeddedResource(string resourceName)
        {
            Assembly exeAsm = Assembly.GetExecutingAssembly();
            using (Stream stream = exeAsm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                byte[] buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return buffer;
            }
        }

        internal static bool IsValidGameRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string worldDll = Path.Combine(path, @"One Way Tickets_Data\Managed\World.dll");
                return File.Exists(worldDll);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsModInstalled(string gameRoot)
        {
            return IsValidGameRoot(gameRoot) && LoaderInstallation.IsInstalled(gameRoot);
        }

        internal static bool HasModFiles(string gameRoot)
        {
            return IsValidGameRoot(gameRoot) && LoaderInstallation.HasFiles(gameRoot);
        }

        internal static string ResolveGameRoot(string explicitPath, bool promptIfNotFound)
        {
            if (IsValidGameRoot(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            string envDir = Environment.GetEnvironmentVariable("ONE_WAY_TICKETS_DIR");
            if (IsValidGameRoot(envDir))
            {
                return Path.GetFullPath(envDir);
            }

            // Check if the exe itself was placed inside the game root directory
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (IsValidGameRoot(exeDir))
            {
                return Path.GetFullPath(exeDir);
            }

            List<string> steamLibs = new List<string>();

            // 1. Registry lookup for Steam
            AddSteamPathsFromRegistry(Registry.CurrentUser, @"Software\Valve\Steam", steamLibs);
            AddSteamPathsFromRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", steamLibs);
            AddSteamPathsFromRegistry(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", steamLibs);

            // 2. Common SteamLibrary locations on all fixed drives
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                string[] commonSubs = new string[]
                {
                    "SteamLibrary",
                    "Steam",
                    @"Program Files (x86)\Steam",
                    @"Program Files\Steam",
                    @"Games\SteamLibrary",
                    @"Games\Steam"
                };

                for (int i = 0; i < drives.Length; i++)
                {
                    DriveInfo d = drives[i];
                    if (!d.IsReady) continue;
                    string root = d.RootDirectory.FullName;
                    for (int j = 0; j < commonSubs.Length; j++)
                    {
                        string cand = Path.Combine(root, commonSubs[j]);
                        AddUniquePath(steamLibs, cand);
                    }
                }
            }
            catch
            {
                // Ignore drive enumeration failures
            }

            // 3. Check each library
            for (int i = 0; i < steamLibs.Count; i++)
            {
                string commonDir = Path.Combine(steamLibs[i], @"steamapps\common");
                if (!Directory.Exists(commonDir)) continue;

                string direct = Path.Combine(commonDir, GameFolderName);
                if (IsValidGameRoot(direct))
                {
                    return Path.GetFullPath(direct);
                }

                try
                {
                    string[] dirs = Directory.GetDirectories(commonDir, "*One Way Tickets*");
                    for (int j = 0; j < dirs.Length; j++)
                    {
                        if (IsValidGameRoot(dirs[j]))
                        {
                            return Path.GetFullPath(dirs[j]);
                        }
                    }
                }
                catch
                {
                    // Ignore permission errors
                }
            }

            if (promptIfNotFound)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "请选择《再再启程 One Way Tickets》游戏安装目录（包含 One Way Tickets.exe 的文件夹）";
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        if (IsValidGameRoot(dialog.SelectedPath))
                        {
                            return Path.GetFullPath(dialog.SelectedPath);
                        }
                    }
                }
            }

            return null;
        }

        private static void AddSteamPathsFromRegistry(RegistryKey rootKey, string subKeyPath, List<string> steamLibs)
        {
            try
            {
                using (RegistryKey key = rootKey.OpenSubKey(subKeyPath, false))
                {
                    if (key == null) return;
                    string[] propNames = new string[] { "SteamPath", "InstallPath" };
                    for (int i = 0; i < propNames.Length; i++)
                    {
                        object valObj = key.GetValue(propNames[i]);
                        string val = valObj as string;
                        if (string.IsNullOrWhiteSpace(val)) continue;

                        string normalized = val.Replace('/', '\\').TrimEnd('\\');
                        AddUniquePath(steamLibs, normalized);

                        string vdfPath = Path.Combine(normalized, @"steamapps\libraryfolders.vdf");
                        if (File.Exists(vdfPath))
                        {
                            string[] lines = File.ReadAllLines(vdfPath);
                            Regex rx = new Regex("\"path\"\\s+\"([^\"]+)\"");
                            for (int l = 0; l < lines.Length; l++)
                            {
                                Match m = rx.Match(lines[l]);
                                if (m.Success)
                                {
                                    string libPath = m.Groups[1].Value.Replace("\\\\", "\\").TrimEnd('\\');
                                    AddUniquePath(steamLibs, libPath);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore registry access failures
            }
        }

        private static void AddUniquePath(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (!Directory.Exists(path)) return;
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                list.Add(path);
            }
            catch
            {
            }
        }

        internal static string PerformInstall(string gameRoot)
        {
            if (!IsValidGameRoot(gameRoot))
                throw new InvalidOperationException("所选文件夹不是有效的《再再启程 One Way Tickets》游戏目录。");
            return LoaderInstallation.Install(gameRoot);
        }

        internal static string PerformUninstall(string gameRoot)
        {
            if (!IsValidGameRoot(gameRoot))
                throw new InvalidOperationException("所选文件夹不是有效的《再再启程 One Way Tickets》游戏目录。");
            return LoaderInstallation.Uninstall(gameRoot);
        }
    }
    internal sealed class InstallerForm : Form
    {
        private readonly TextBox _pathTextBox;
        private readonly Label _statusBadgeLabel;
        private readonly Label _logLabel;
        private readonly Button _installButton;
        private readonly Button _uninstallButton;
        private readonly Button _openConfigButton;

        public InstallerForm()
        {
            Text = "《再再启程》键盘减负 Mod 一键安装器 v1.1";
            Size = new Size(620, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);

            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 68;
            headerPanel.BackColor = Color.FromArgb(32, 41, 56);
            Controls.Add(headerPanel);

            Label titleLabel = new Label();
            titleLabel.Text = "《再再启程 One Way Tickets》双手键盘减负 Mod";
            titleLabel.ForeColor = Color.White;
            titleLabel.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(20, 12);
            headerPanel.Controls.Add(titleLabel);

            Label subTitleLabel = new Label();
            subTitleLabel.Text = "左手 ASDFG 控枪 & 抢修 · 右手 HJKL; 拎工具 & 一键最优派人 · 100% 保留鼠标操作";
            subTitleLabel.ForeColor = Color.FromArgb(190, 205, 225);
            subTitleLabel.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
            subTitleLabel.AutoSize = true;
            subTitleLabel.Location = new Point(22, 42);
            headerPanel.Controls.Add(subTitleLabel);

            Label dirTitle = new Label();
            dirTitle.Text = "Steam 游戏安装目录：";
            dirTitle.AutoSize = true;
            dirTitle.Location = new Point(22, 86);
            Controls.Add(dirTitle);

            _statusBadgeLabel = new Label();
            _statusBadgeLabel.AutoSize = true;
            _statusBadgeLabel.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            _statusBadgeLabel.Location = new Point(175, 86);
            Controls.Add(_statusBadgeLabel);

            _pathTextBox = new TextBox();
            _pathTextBox.Location = new Point(24, 112);
            _pathTextBox.Size = new Size(465, 26);
            _pathTextBox.ReadOnly = false;
            _pathTextBox.TextChanged += delegate { RefreshStatus(); };
            Controls.Add(_pathTextBox);

            Button browseBtn = new Button();
            browseBtn.Text = "浏览目录...";
            browseBtn.Location = new Point(498, 110);
            browseBtn.Size = new Size(88, 28);
            browseBtn.BackColor = Color.White;
            browseBtn.UseVisualStyleBackColor = false;
            browseBtn.Click += OnBrowseClicked;
            Controls.Add(browseBtn);

            _installButton = new Button();
            _installButton.Text = "一键安装 / 更新 Mod";
            _installButton.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            _installButton.Location = new Point(24, 158);
            _installButton.Size = new Size(265, 48);
            _installButton.BackColor = Color.FromArgb(37, 130, 82);
            _installButton.ForeColor = Color.White;
            _installButton.FlatStyle = FlatStyle.Flat;
            _installButton.FlatAppearance.BorderSize = 0;
            _installButton.Cursor = Cursors.Hand;
            _installButton.Click += OnInstallClicked;
            Controls.Add(_installButton);

            _uninstallButton = new Button();
            _uninstallButton.Text = "一键卸载 Mod";
            _uninstallButton.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular);
            _uninstallButton.Location = new Point(304, 158);
            _uninstallButton.Size = new Size(155, 48);
            _uninstallButton.BackColor = Color.FromArgb(195, 65, 65);
            _uninstallButton.ForeColor = Color.White;
            _uninstallButton.FlatStyle = FlatStyle.Flat;
            _uninstallButton.FlatAppearance.BorderSize = 0;
            _uninstallButton.Cursor = Cursors.Hand;
            _uninstallButton.Click += OnUninstallClicked;
            Controls.Add(_uninstallButton);

            _openConfigButton = new Button();
            _openConfigButton.Text = "修改键位 (INI)";
            _openConfigButton.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
            _openConfigButton.Location = new Point(470, 158);
            _openConfigButton.Size = new Size(116, 48);
            _openConfigButton.BackColor = Color.FromArgb(65, 85, 115);
            _openConfigButton.ForeColor = Color.White;
            _openConfigButton.FlatStyle = FlatStyle.Flat;
            _openConfigButton.FlatAppearance.BorderSize = 0;
            _openConfigButton.Cursor = Cursors.Hand;
            _openConfigButton.Click += OnOpenConfigClicked;
            Controls.Add(_openConfigButton);

            _logLabel = new Label();
            _logLabel.Location = new Point(24, 222);
            _logLabel.Size = new Size(562, 105);
            _logLabel.BackColor = Color.White;
            _logLabel.BorderStyle = BorderStyle.FixedSingle;
            _logLabel.Padding = new Padding(10);
            _logLabel.Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Regular);
            _logLabel.ForeColor = Color.FromArgb(55, 65, 81);
            Controls.Add(_logLabel);

            string detected = Program.ResolveGameRoot(null, false);
            if (!string.IsNullOrEmpty(detected))
            {
                _pathTextBox.Text = detected;
            }
            else
            {
                RefreshStatus();
            }
        }

        private void RefreshStatus()
        {
            string path = _pathTextBox.Text.Trim();
            if (!Program.IsValidGameRoot(path))
            {
                _statusBadgeLabel.Text = "[未定位到游戏目录，请点击右侧“浏览目录...”选择]";
                _statusBadgeLabel.ForeColor = Color.FromArgb(195, 65, 65);
                _installButton.Enabled = false;
                _uninstallButton.Enabled = false;
                _openConfigButton.Enabled = false;
                _logLabel.Text = "提示：未能自动从 Steam 库中找到《再再启程 One Way Tickets》。\n请点击“浏览目录...”手动选择包含 One Way Tickets.exe 的游戏根目录。";
                return;
            }

            _installButton.Enabled = true;
            bool installed;
            bool remainingFiles;
            try
            {
                installed = Program.IsModInstalled(path);
                remainingFiles = Program.HasModFiles(path);
            }
            catch (InvalidOperationException ex)
            {
                _statusBadgeLabel.Text = "[目录布局需要手动处理]";
                _statusBadgeLabel.ForeColor = Color.FromArgb(195, 65, 65);
                _installButton.Enabled = false;
                _uninstallButton.Enabled = false;
                _openConfigButton.Enabled = false;
                _logLabel.Text = ex.Message;
                return;
            }
            _uninstallButton.Enabled = installed || remainingFiles;
            _openConfigButton.Enabled = File.Exists(Path.Combine(path, "OneWayTickets.KeyboardMod.ini"));

            if (installed)
            {
                _statusBadgeLabel.Text = "[已安装 Mod · 独立加载]";
                _statusBadgeLabel.ForeColor = Color.FromArgb(37, 130, 82);
                _logLabel.Text = "状态：键盘 Mod 通过 BepInEx 独立加载，原版游戏文件保持原样。\n"
                    + "• 左手 ASDFG 控武器（CapsLock 全自动开火），QWERT 定点修，Z 刹车 / X 灵能冲刺 / C 风箱\n"
                    + "• 右手 HJKL; 拎工具箱（空格全车智能抢修），' 或 ] 一键最优派驻船员\n"
                    + "• 游戏中按 F9 显隐快捷键提示，按 F10 热重载改键配置。";
            }
            else if (remainingFiles)
            {
                _statusBadgeLabel.Text = "[Mod 未生效 · 检测到残留文件]";
                _statusBadgeLabel.ForeColor = Color.FromArgb(180, 115, 20);
                _logLabel.Text = "状态：独立加载器不完整或已禁用。\n"
                    + "• 点击【一键安装 / 更新 Mod】可检查并补全本安装器添加的文件。\n"
                    + "• 点击【一键卸载 Mod】可清理本 Mod 文件并保留共享加载器。";
            }
            else
            {
                _statusBadgeLabel.Text = "[已自动识别 Steam 游戏目录 · 待安装]";
                _statusBadgeLabel.ForeColor = Color.FromArgb(40, 100, 180);
                _logLabel.Text = "状态：已成功识别《再再启程》安装位置！\n"
                    + "请确保游戏处于关闭状态，然后点击上方绿色的【一键安装 / 更新 Mod】按钮即可。";
            }
        }

        private void OnBrowseClicked(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "请选择《再再启程 One Way Tickets》游戏根目录（包含 One Way Tickets.exe）";
                if (Directory.Exists(_pathTextBox.Text))
                {
                    dialog.SelectedPath = _pathTextBox.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _pathTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnInstallClicked(object sender, EventArgs e)
        {
            string path = _pathTextBox.Text.Trim();
            try
            {
                string result = Program.PerformInstall(path);
                RefreshStatus();
                _logLabel.Text = result.Replace("\n\n", "\n");
                MessageBox.Show(this, result, "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnUninstallClicked(object sender, EventArgs e)
        {
            string path = _pathTextBox.Text.Trim();
            try
            {
                string result = Program.PerformUninstall(path);
                RefreshStatus();
                _logLabel.Text = result;
                MessageBox.Show(this, result, "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnOpenConfigClicked(object sender, EventArgs e)
        {
            string iniPath = Path.Combine(_pathTextBox.Text.Trim(), "OneWayTickets.KeyboardMod.ini");
            if (File.Exists(iniPath))
            {
                System.Diagnostics.Process.Start("notepad.exe", iniPath);
            }
            else
            {
                MessageBox.Show(this, "请先点击【一键安装 / 更新 Mod】生成配置文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
