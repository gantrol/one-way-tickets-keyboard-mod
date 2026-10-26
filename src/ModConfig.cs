using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Manages configuration and two-handed keybindings for the One Way Tickets Keyboard Mod.
    /// Reads and writes 'OneWayTickets.KeyboardMod.ini' in the game root directory.
    /// </summary>
    public sealed class ModConfig
    {
        public static ModConfig Instance { get; private set; } = new ModConfig();

        // =====================================================================
        // LEFT HAND
        // =====================================================================
        // Left Hand Home Row: Weapon / Device slot keys (A S D F G for fixed slots 1-5) + Global Auto-Fire toggle (CapsLock)
        public Key[] WeaponSlotKeys { get; private set; } = new Key[]
        {
            Key.A, Key.S, Key.D, Key.F, Key.G
        };
        public Key GlobalAutoFireToggleKey { get; private set; } = Key.CapsLock;

        // Left Hand Upper Row: Per-Slot Toolbox Direct Maintenance keys (Q W E R T, aligned above A S D F G)
        public Key[] SlotMaintainKeys { get; private set; } = new Key[]
        {
            Key.Q, Key.W, Key.E, Key.R, Key.T
        };

        // Left Hand Lower Row: Driving & Wheel controls (Z = Brake, X = Psionic Acceleration, C = Furnace Bellows, V = Wheel Slot Reserved)
        public Key BrakeKey { get; private set; } = Key.Z;
        public Key PsionicBoostKey { get; private set; } = Key.X;
        public Key BellowsKey { get; private set; } = Key.C;
        public Key WheelSlotKey { get; private set; } = Key.V;

        // =====================================================================
        // RIGHT HAND
        // =====================================================================
        // Right Hand Home Row: Dedicated Toolbox keys (H J K L ;) + Global Smart Maintenance (Space)
        public Key ToolWrenchKey { get; private set; } = Key.H;
        public Key ToolExtinguisherKey { get; private set; } = Key.J;
        public Key ToolSawKey { get; private set; } = Key.K;
        public Key ToolRagKey { get; private set; } = Key.L;
        public Key ToolWakeUpKey { get; private set; } = Key.Semicolon;
        public Key GlobalSmartMaintainKey { get; private set; } = Key.Space;

        // Right Hand Upper Row: Character keys (Y U I O P [) + Secondary Number aliases (1-6)
        public Key[] CharacterKeys { get; private set; } = new Key[]
        {
            Key.Y, Key.U, Key.I, Key.O, Key.P, Key.LeftBracket
        };
        public Key[] CharacterAltKeys { get; private set; } = new Key[]
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6
        };
        // Character Auto-Assign keys (' on home row and ] on upper row)
        public Key CharacterAutoAssignKey { get; private set; } = Key.Quote;
        public Key CharacterAutoAssignAltKey { get; private set; } = Key.RightBracket;
        // Recall keys (Backspace / 0)
        public Key CharacterRecallKey { get; private set; } = Key.Backspace;
        public Key CharacterRecallAltKey { get; private set; } = Key.Digit0;

        // System / HUD keys
        public Key ToggleHudOverlayKey { get; private set; } = Key.F9;
        public Key ReloadConfigKey { get; private set; } = Key.F10;

        // Behavioral settings
        public bool ShowHudOverlay { get; set; } = true;
        public bool EnableToolPullOutAnimation { get; set; } = true;
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
                case "WeaponSlotKeys":
                    WeaponSlotKeys = ParseKeyArray(val, WeaponSlotKeys);
                    break;
                case "GlobalAutoFireToggleKey":
                    GlobalAutoFireToggleKey = ParseKey(val, GlobalAutoFireToggleKey);
                    break;
                case "SlotMaintainKeys":
                    SlotMaintainKeys = ParseKeyArray(val, SlotMaintainKeys);
                    break;
                case "BrakeKey":
                    BrakeKey = ParseKey(val, BrakeKey);
                    break;
                case "PsionicBoostKey":
                    PsionicBoostKey = ParseKey(val, PsionicBoostKey);
                    break;
                case "BellowsKey":
                    BellowsKey = ParseKey(val, BellowsKey);
                    break;
                case "WheelSlotKey":
                    WheelSlotKey = ParseKey(val, WheelSlotKey);
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
                case "CharacterKeys":
                    CharacterKeys = ParseKeyArray(val, CharacterKeys);
                    break;
                case "CharacterAltKeys":
                    CharacterAltKeys = ParseKeyArray(val, CharacterAltKeys);
                    break;
                case "CharacterAutoAssignKey":
                    CharacterAutoAssignKey = ParseKey(val, CharacterAutoAssignKey);
                    break;
                case "CharacterAutoAssignAltKey":
                    CharacterAutoAssignAltKey = ParseKey(val, CharacterAutoAssignAltKey);
                    break;
                case "CharacterRecallKey":
                    CharacterRecallKey = ParseKey(val, CharacterRecallKey);
                    break;
                case "CharacterRecallAltKey":
                    CharacterRecallAltKey = ParseKey(val, CharacterRecallAltKey);
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
                case "EnableToolPullOutAnimation":
                    if (bool.TryParse(val, out bool anim)) EnableToolPullOutAnimation = anim;
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
            if (cleaned == ";") return Key.Semicolon;
            if (cleaned == "'") return Key.Quote;
            if (cleaned == "[") return Key.LeftBracket;
            if (cleaned == "]") return Key.RightBracket;
            if (cleaned == ",") return Key.Comma;
            if (cleaned == ".") return Key.Period;
            if (cleaned == "/") return Key.Slash;
            if (string.Equals(cleaned, "Caps", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleaned, "CapsLock", StringComparison.OrdinalIgnoreCase))
            {
                return Key.CapsLock;
            }
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
            List<Key> result = new List<Key>(fallback.Length);
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
            switch (k)
            {
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Slash: return "/";
                case Key.Backspace: return "Bksp";
                case Key.CapsLock: return "CapsLock";
            }
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
            sb.AppendLine("# 《再再启程 One Way Tickets》 双手人体工学键盘 Mod 配置文件");
            sb.AppendLine("# 修改后可在游戏中按 F10 热重载配置，按 F9 切换屏幕快捷键提示开关");
            sb.AppendLine("# 鼠标原生操作 100% 保留不变，以下所有快捷键均为双手键盘增强操作");
            sb.AppendLine("# ============================================================================");
            sb.AppendLine();
            sb.AppendLine("[LeftHand_WeaponsAndDriving]");
            sb.AppendLine("# 左手主排 (A S D F G + CapsLock)：固定 1-5 号武器槽位主操作（最常用放主排）");
            sb.AppendLine("#   - 点按/按住 A-G：主操作 + 智能闭环（攻击/开火/空仓装填/蓄力）");
            sb.AppendLine("#   - CapsLock（或 Ctrl + A-G）：切换武器【自动托管 [AUTO]】");
            sb.AppendLine("#   - 长按/点按 Shift + A-G：独立副操作 2 | 长按/点按 Alt + A-G：独立副操作 3");
            sb.AppendLine("WeaponSlotKeys = A, S, D, F, G");
            sb.AppendLine("GlobalAutoFireToggleKey = CapsLock");
            sb.AppendLine();
            sb.AppendLine("# 左手上排 (Q W E R T)：固定 1-5 号槽位一键定点抢修（与下方 A-G 一一对应）");
            sb.AppendLine("SlotMaintainKeys = Q, W, E, R, T");
            sb.AppendLine();
            sb.AppendLine("# 左手下排 (Z X C V)：刹车(Z)、灵能加速(X)、风箱(C)、车轮槽保留键(V)");
            sb.AppendLine("BrakeKey = Z");
            sb.AppendLine("PsionicBoostKey = X");
            sb.AppendLine("BellowsKey = C");
            sb.AppendLine("WheelSlotKey = V");
            sb.AppendLine("ManualPlayerAbility = 1.0");
            sb.AppendLine();
            sb.AppendLine("[RightHand_Toolbox]");
            sb.AppendLine("# 右手主排 (H J K L ;)：工具箱专项操作（最常用放主排，自动拎出工具并放回）");
            sb.AppendLine("#   - H：扳手 / 车载维修包");
            sb.AppendLine("#   - J：灭火器");
            sb.AppendLine("#   - K：手锯 / 剪刀 / 电锯（自动优先切最外层藤蔓）");
            sb.AppendLine("#   - L：抹布 / 拖把 / 吹风筒（吹风筒阶段自动全车吹干）");
            sb.AppendLine("#   - Semicolon (;)：唤醒工具 / 除颤仪 AED（AED 阶段自动全车唤醒）");
            sb.AppendLine("#   - Space（空格）：全局智能一键抢修");
            sb.AppendLine("ToolWrenchKey = H");
            sb.AppendLine("ToolExtinguisherKey = J");
            sb.AppendLine("ToolSawKey = K");
            sb.AppendLine("ToolRagKey = L");
            sb.AppendLine("ToolWakeUpKey = Semicolon");
            sb.AppendLine("GlobalSmartMaintainKey = Space");
            sb.AppendLine("EnableToolPullOutAnimation = true");
            sb.AppendLine();
            sb.AppendLine("[RightHand_Characters]");
            sb.AppendLine("# 右手上排 (Y U I O P [)：1-6 号角色选择与派驻（同时兼容数字键 1-6）");
            sb.AppendLine("#   - 按 Y-[（或 1-6）选中角色 -> 再按左手 A-G（或 Q-T）派驻到对应槽位工位");
            sb.AppendLine("#   - 按 '（分号右侧引号键）或 ]：一键智能最优分配所有人物（Ctrl+' 切换持续自动分配托管）");
            sb.AppendLine("#   - 双击角色键或按 Backspace / 0：撤回角色休息 | Alt + 角色键：与替补轮换");
            sb.AppendLine("CharacterKeys = Y, U, I, O, P, LeftBracket");
            sb.AppendLine("CharacterAltKeys = Digit1, Digit2, Digit3, Digit4, Digit5, Digit6");
            sb.AppendLine("CharacterAutoAssignKey = Quote");
            sb.AppendLine("CharacterAutoAssignAltKey = RightBracket");
            sb.AppendLine("CharacterRecallKey = Backspace");
            sb.AppendLine("CharacterRecallAltKey = Digit0");
            sb.AppendLine("AutoClearCharacterSelectionAfterAssign = true");
            sb.AppendLine("DoubleTapRecallSeconds = 0.35");
            sb.AppendLine();
            sb.AppendLine("[HUD]");
            sb.AppendLine("ShowHudOverlay = true");
            sb.AppendLine("ToggleHudOverlayKey = F9");
            sb.AppendLine("ReloadConfigKey = F10");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}
