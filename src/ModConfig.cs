using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Manages configuration and keybindings for the One Way Tickets Keyboard Mod.
    /// Reads and writes 'OneWayTickets.KeyboardMod.ini' in the game root directory.
    /// </summary>
    public sealed class ModConfig
    {
        public static ModConfig Instance { get; private set; } = new ModConfig();

        // Row 1: Character keys (1-6) + Recall key (0 / Backspace)
        public Key[] CharacterKeys { get; private set; } = new Key[]
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6
        };
        public Key CharacterRecallKey { get; private set; } = Key.Digit0;
        public Key CharacterRecallAltKey { get; private set; } = Key.Backspace;

        // Row 2: Weapon / Device slot keys (Q W E R T Y) + Global Auto-Fire toggle (Tab)
        public Key[] WeaponSlotKeys { get; private set; } = new Key[]
        {
            Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y
        };
        public Key GlobalAutoFireToggleKey { get; private set; } = Key.Tab;

        // Row 3: Per-Slot Toolbox Direct Maintenance keys (A S D F G H, aligned under Q W E R T Y)
        public Key[] SlotMaintainKeys { get; private set; } = new Key[]
        {
            Key.A, Key.S, Key.D, Key.F, Key.G, Key.H
        };

        // Row 4: Specific Tool keys (Z X C V B) + Global Smart Maintenance (Space)
        public Key ToolWrenchKey { get; private set; } = Key.Z;
        public Key ToolExtinguisherKey { get; private set; } = Key.X;
        public Key ToolSawKey { get; private set; } = Key.C;
        public Key ToolRagKey { get; private set; } = Key.V;
        public Key ToolWakeUpKey { get; private set; } = Key.B;
        public Key GlobalSmartMaintainKey { get; private set; } = Key.Space;

        // System / HUD keys
        public Key ToggleHudOverlayKey { get; private set; } = Key.F9;
        public Key ReloadConfigKey { get; private set; } = Key.F10;

        // Behavioral settings
        public bool ShowHudOverlay { get; set; } = true;
        public bool AutoClearCharacterSelectionAfterAssign { get; set; } = true;
        public float DoubleTapRecallSeconds { get; set; } = 0.35f;
        public float ManualPlayerAbility { get; set; } = 1.0f;

        private string _configFilePath;

        public static void Load()
        {
            if (Instance == null)
            {
                Instance = new ModConfig();
            }
            Instance.LoadInternal();
        }

        public void LoadInternal()
        {
            try
            {
                string gameRoot = Path.GetDirectoryName(Application.dataPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                _configFilePath = Path.Combine(gameRoot, "OneWayTickets.KeyboardMod.ini");

                if (!File.Exists(_configFilePath))
                {
                    SaveDefaultConfig(_configFilePath);
                    return;
                }

                string[] lines = File.ReadAllLines(_configFilePath, Encoding.UTF8);
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                    {
                        continue;
                    }

                    int eqIdx = line.IndexOf('=');
                    if (eqIdx <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, eqIdx).Trim();
                    string val = line.Substring(eqIdx + 1).Trim();
                    ApplySetting(key, val);
                }

                Debug.Log($"[KeyboardMod] Loaded config from {_configFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KeyboardMod] Failed to load config: {ex.Message}");
            }
        }

        private void ApplySetting(string key, string val)
        {
            switch (key)
            {
                case "CharacterKeys":
                    CharacterKeys = ParseKeyArray(val, CharacterKeys);
                    break;
                case "CharacterRecallKey":
                    CharacterRecallKey = ParseKey(val, CharacterRecallKey);
                    break;
                case "CharacterRecallAltKey":
                    CharacterRecallAltKey = ParseKey(val, CharacterRecallAltKey);
                    break;
                case "WeaponSlotKeys":
                    WeaponSlotKeys = ParseKeyArray(val, WeaponSlotKeys);
                    break;
                case "GlobalAutoFireToggleKey":
                    GlobalAutoFireToggleKey = ParseKey(val, GlobalAutoFireToggleKey);
                    break;
                case "SlotMaintainKeys":
                    SlotMaintainKeys = ParseKeyArray(val, SlotMaintainKeys);
                    break;
                case "ToolWrenchKey":
                    ToolWrenchKey = ParseKey(val, ToolWrenchKey);
                    break;
                case "ToolExtinguisherKey":
                    ToolExtinguisherKey = ParseKey(val, ToolExtinguisherKey);
                    break;
                case "ToolSawKey":
                    ToolSawKey = ParseKey(val, ToolSawKey);
                    break;
                case "ToolRagKey":
                    ToolRagKey = ParseKey(val, ToolRagKey);
                    break;
                case "ToolWakeUpKey":
                    ToolWakeUpKey = ParseKey(val, ToolWakeUpKey);
                    break;
                case "GlobalSmartMaintainKey":
                    GlobalSmartMaintainKey = ParseKey(val, GlobalSmartMaintainKey);
                    break;
                case "ToggleHudOverlayKey":
                    ToggleHudOverlayKey = ParseKey(val, ToggleHudOverlayKey);
                    break;
                case "ReloadConfigKey":
                    ReloadConfigKey = ParseKey(val, ReloadConfigKey);
                    break;
                case "ShowHudOverlay":
                    if (bool.TryParse(val, out bool showHud)) ShowHudOverlay = showHud;
                    break;
                case "AutoClearCharacterSelectionAfterAssign":
                    if (bool.TryParse(val, out bool autoClear)) AutoClearCharacterSelectionAfterAssign = autoClear;
                    break;
                case "DoubleTapRecallSeconds":
                    if (float.TryParse(val, out float dtSec)) DoubleTapRecallSeconds = Mathf.Clamp(dtSec, 0.1f, 1.0f);
                    break;
                case "ManualPlayerAbility":
                    if (float.TryParse(val, out float ability)) ManualPlayerAbility = Mathf.Clamp(ability, 0.1f, 10.0f);
                    break;
            }
        }

        private static Key ParseKey(string value, Key fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            string cleaned = value.Trim();
            if (cleaned.Length == 1 && char.IsDigit(cleaned[0]))
            {
                cleaned = "Digit" + cleaned;
            }
            if (Enum.TryParse(cleaned, true, out Key parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private static Key[] ParseKeyArray(string csv, Key[] fallback)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return fallback;
            }
            string[] parts = csv.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<Key> result = new List<Key>( fallback.Length );
            for (int i = 0; i < fallback.Length; i++)
            {
                if (i < parts.Length)
                {
                    result.Add(ParseKey(parts[i], fallback[i]));
                }
                else
                {
                    result.Add(fallback[i]);
                }
            }
            return result.ToArray();
        }

        public static string FormatKeyLabel(Key k)
        {
            string s = k.ToString();
            if (s.StartsWith("Digit") && s.Length == 6)
            {
                return s.Substring(5);
            }
            return s;
        }

        private static void SaveDefaultConfig(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# ============================================================================");
            sb.AppendLine("# 《再再启程 One Way Tickets》 全键盘矩阵控制 Mod 配置文件");
            sb.AppendLine("# 修改后可在游戏中按 F10 热重载配置，按 F9 切换屏幕快捷键提示开关");
            sb.AppendLine("# 鼠标原生操作 100% 保留不变，以下所有快捷键均为纯键盘增强操作");
            sb.AppendLine("# ============================================================================");
            sb.AppendLine();
            sb.AppendLine("[Characters]");
            sb.AppendLine("# 第一排：角色选择键 (1-6) 与撤回键 (0 / Backspace)");
            sb.AppendLine("# 用法：按 1-6 选中角色 -> 再按 Q-Y 派驻到对应武器工位；双击 1-6 或按 0/Backspace 撤回；Alt+1-6 替补轮换");
            sb.AppendLine("CharacterKeys = Digit1, Digit2, Digit3, Digit4, Digit5, Digit6");
            sb.AppendLine("CharacterRecallKey = Digit0");
            sb.AppendLine("CharacterRecallAltKey = Backspace");
            sb.AppendLine("AutoClearCharacterSelectionAfterAssign = true");
            sb.AppendLine("DoubleTapRecallSeconds = 0.35");
            sb.AppendLine();
            sb.AppendLine("[Weapons]");
            sb.AppendLine("# 第二排：武器/设备槽位键 (Q W E R T Y，从左到右对应 1-6 号位)");
            sb.AppendLine("# 用法：");
            sb.AppendLine("#   - 按住 Q-Y：主操作 + 智能循环（自动索敌旋转 + 开火攻击 + 空仓自动装填 + 蓄力）");
            sb.AppendLine("#   - Ctrl + Q-Y：切换该槽位武器【后台自动开火/自动装填托管 [AUTO]】");
            sb.AppendLine("#   - Tab：一键切换所有武器【全局自动托管】");
            sb.AppendLine("#   - Shift + Q-Y：独立操作 2（手动提前装填 / 投矛远程投掷 / 大剑磨刀 / 箭袋切换 / 战鼓右槌）");
            sb.AppendLine("#   - Alt + Q-Y：独立操作 3（加特林搬运弹药 / 火炮与绞盘手摇旋转 / 左轮退弹）");
            sb.AppendLine("WeaponSlotKeys = Q, W, E, R, T, Y");
            sb.AppendLine("GlobalAutoFireToggleKey = Tab");
            sb.AppendLine("ManualPlayerAbility = 1.0");
            sb.AppendLine();
            sb.AppendLine("[Toolbox]");
            sb.AppendLine("# 第三排：按槽位直接维护键 (A S D F G H，与上方 Q W E R T Y 垂直对齐)");
            sb.AppendLine("# 用法：按住 A-H 直接对上方对应槽位设备执行【修缮破损 / 灭火 / 安全剪除最上层藤蔓 / 擦水擦酸 / 拍醒停滞】");
            sb.AppendLine("SlotMaintainKeys = A, S, D, F, G, H");
            sb.AppendLine();
            sb.AppendLine("# 第四排：按工具类别维护键 (Z X C V B) 与全局智能抢修键 (Space)");
            sb.AppendLine("#   - Z (扳手)：自动修缮全车破损设备");
            sb.AppendLine("#   - X (灭火器)：自动扑灭全车着火设备");
            sb.AppendLine("#   - C (锯子/剪刀)：自动安全切除全车最表层藤蔓（不触发底层藤蔓扣血惩罚）");
            sb.AppendLine("#   - V (抹布/拖把)：自动擦除全车水滴与酸液");
            sb.AppendLine("#   - B (唤醒工具)：自动唤醒全车昏迷角色（也可按住 B + 1-6 指定唤醒某位角色）");
            sb.AppendLine("#   - Space (空格)：全局智能抢修（按住空格自动按紧急度处理全车所有异常与昏迷！）");
            sb.AppendLine("ToolWrenchKey = Z");
            sb.AppendLine("ToolExtinguisherKey = X");
            sb.AppendLine("ToolSawKey = C");
            sb.AppendLine("ToolRagKey = V");
            sb.AppendLine("ToolWakeUpKey = B");
            sb.AppendLine("GlobalSmartMaintainKey = Space");
            sb.AppendLine();
            sb.AppendLine("[System]");
            sb.AppendLine("ToggleHudOverlayKey = F9");
            sb.AppendLine("ReloadConfigKey = F10");
            sb.AppendLine("ShowHudOverlay = true");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}
