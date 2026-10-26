using System;
using System.Collections.Generic;
using Foundations;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.Characters;
using World.Cubicles;
using World.Devices;
using World.Helpers;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Handles Row 1 keyboard shortcuts:
    /// - 1..6: Select front-line character 1..6
    /// - 1..6 -> Q..Y: Dispatch selected character to device cubicle (cycles primary/secondary cubicle)
    /// - Double-tap 1..6 or press 0 / Backspace: Recall character from cubicle to rest
    /// - Alt + 1..6: Swap front-line character with backup character
    /// </summary>
    public sealed class KeyboardCharacterController
    {
        private readonly DeviceSlotResolver _slotResolver;
        private readonly float[] _lastCharKeyPressTime = new float[6];
        private readonly bool[] _consumedSlotKeyUntilRelease = new bool[6];

        public string LastActionStatus { get; private set; } = string.Empty;
        public float LastActionStatusTime { get; private set; }
        public int LastTargetSlotIndex { get; private set; } = -1;
        public float LastTargetSlotTime { get; private set; }

        public KeyboardCharacterController(DeviceSlotResolver slotResolver)
        {
            _slotResolver = slotResolver;
        }

        public bool IsSlotKeyConsumedByDispatch(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _consumedSlotKeyUntilRelease.Length)
            {
                return false;
            }
            return _consumedSlotKeyUntilRelease[slotIndex];
        }

        public void UpdateInput(Keyboard kb)
        {
            if (kb == null)
            {
                return;
            }

            Mission mission = Singleton<Mission>.instance;
            if (mission == null || !mission.try_get_mgr("CharacterMgr", out CharacterMgr charMgr) || charMgr == null)
            {
                return;
            }

            ModConfig cfg = ModConfig.Instance;

            // Clear consumed slot flags when key is released
            for (int i = 0; i < cfg.WeaponSlotKeys.Length && i < _consumedSlotKeyUntilRelease.Length; i++)
            {
                Key wk = cfg.WeaponSlotKeys[i];
                if (wk != Key.None && !kb[wk].isPressed)
                {
                    _consumedSlotKeyUntilRelease[i] = false;
                }
            }

            bool altHeld = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            bool ctrlHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool shiftHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool wakeUpHeld = cfg.ToolWakeUpKey != Key.None && kb[cfg.ToolWakeUpKey].isPressed;

            // If B (WakeUp key) is held, 1..6 are used for targeted character wake-up
            if (!wakeUpHeld)
            {
                for (int i = 0; i < cfg.CharacterKeys.Length && i < 6; i++)
                {
                    Key ck = cfg.CharacterKeys[i];
                    if (ck != Key.None && kb[ck].wasPressedThisFrame)
                    {
                        if (altHeld)
                        {
                            TrySwapBackupCharacter(charMgr, i);
                        }
                        else if (!ctrlHeld)
                        {
                            HandleCharacterKeyPress(charMgr, i);
                        }
                    }
                }
            }

            // Recall / Deselect keys: 0 or Backspace
            bool recallPressed =
                (cfg.CharacterRecallKey != Key.None && kb[cfg.CharacterRecallKey].wasPressedThisFrame) ||
                (cfg.CharacterRecallAltKey != Key.None && kb[cfg.CharacterRecallAltKey].wasPressedThisFrame);

            if (recallPressed)
            {
                Character selected = Cubicle_Helper.GetSelectCharacter();
                if (selected != null)
                {
                    RecallCharacter(charMgr, selected, -1);
                }
            }

            // Check if a character is currently selected and player presses Q..Y to assign cubicle
            Character currentSelected = Cubicle_Helper.GetSelectCharacter();
            if (currentSelected != null && !altHeld && !ctrlHeld && !shiftHeld)
            {
                for (int slotIdx = 0; slotIdx < cfg.WeaponSlotKeys.Length && slotIdx < 6; slotIdx++)
                {
                    Key wk = cfg.WeaponSlotKeys[slotIdx];
                    if (wk != Key.None && kb[wk].wasPressedThisFrame)
                    {
                        if (TryDispatchSelectedCharacterToSlot(charMgr, currentSelected, slotIdx))
                        {
                            _consumedSlotKeyUntilRelease[slotIdx] = true;
                        }
                    }
                }
            }
        }

        private void HandleCharacterKeyPress(CharacterMgr charMgr, int charIndex)
        {
            if (charMgr.characters == null || charIndex < 0 || charIndex >= charMgr.characters.Count)
            {
                return;
            }

            Character targetChar = charMgr.characters[charIndex];
            if (targetChar == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            float elapsed = now - _lastCharKeyPressTime[charIndex];
            _lastCharKeyPressTime[charIndex] = now;

            // Double-tap 1..6 -> Recall character from cubicle to rest
            if (elapsed <= ModConfig.Instance.DoubleTapRecallSeconds)
            {
                RecallCharacter(charMgr, targetChar, charIndex);
                return;
            }

            // Single tap -> Select character (or toggle off if already selected and not double-tapped)
            Character currentSelected = Cubicle_Helper.GetSelectCharacter();
            if (currentSelected == targetChar)
            {
                Cubicle_Helper.SetSelectCharacter(targetChar);
                SetStatus($"已选中 {charIndex + 1} 号角色 (按 Q-Y 派驻工位，再按一次撤回)");
            }
            else
            {
                Cubicle_Helper.SetSelectCharacter(targetChar);
                SetStatus($"已选中 {charIndex + 1} 号角色 (按 Q-Y 派驻工位，双击 {charIndex + 1} 或按 0 撤回)");
            }
        }

        private void RecallCharacter(CharacterMgr charMgr, Character character, int charIndex)
        {
            BasicCubicle cubicle = Cubicle_Helper.GetCubicle(character);
            if (cubicle != null)
            {
                Cubicle_Helper.EmptyCubicle(cubicle);
                string label = charIndex >= 0 ? $"{charIndex + 1} 号角色" : "选中角色";
                SetStatus($"已撤回 {label} 休息");
            }
            else
            {
                SetStatus("已取消角色选择");
            }

            charMgr.CancelSelectCharacter();
        }

        private bool TryDispatchSelectedCharacterToSlot(CharacterMgr charMgr, Character selected, int slotIndex)
        {
            BattleContext bctx = Singleton<BattleContext>.instance;
            if (bctx != null && !bctx.active_cubicle)
            {
                return false;
            }

            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.WeaponSlotKeys[slotIndex]);
            LastTargetSlotIndex = slotIndex;
            LastTargetSlotTime = Time.unscaledTime;

            if (!selected.StateCanWork())
            {
                SetStatus($"[{keyLabel}] 该角色当前处于昏迷/无法工作状态！(按 B 或 Space 唤醒)");
                return true;
            }

            if (!_slotResolver.TryGetSlot(slotIndex, out ResolvedDeviceSlot slot))
            {
                SetStatus($"[{keyLabel}] 当前车体无 {slotIndex + 1} 号槽位");
                return true;
            }

            if (slot.Device == null)
            {
                SetStatus($"[{keyLabel}] {slotIndex + 1} 号槽位为空（未安装设备），无法派驻角色");
                return true;
            }

            Device device = slot.Device;
            string devName = KeyboardToolboxController.GetDeviceDisplayName(device);
            if (device.cubicle_list == null || device.cubicle_list.Count == 0)
            {
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 该设备无需/无可用工位");
                if (ModConfig.Instance.AutoClearCharacterSelectionAfterAssign)
                {
                    charMgr.CancelSelectCharacter();
                }
                return true;
            }

            if (!selected.CanWork(device))
            {
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 该角色无法在此设备工作");
                return true;
            }

            BasicCubicle targetCubicle = ChooseTargetCubicle(device, selected, out int cubicleIdx);
            if (targetCubicle == null)
            {
                return false;
            }

            Cubicle_Helper.SetSelectCharacter(selected);
            Cubicle_Helper.SetCubicle(targetCubicle);

            if (ModConfig.Instance.AutoClearCharacterSelectionAfterAssign)
            {
                charMgr.CancelSelectCharacter();
            }

            string subLabel = device.cubicle_list.Count > 1 ? $" (工位 {cubicleIdx + 1}/{device.cubicle_list.Count})" : string.Empty;
            SetStatus($"[派驻 -> {slotIndex + 1}号:{devName} ({keyLabel})]{subLabel}");
            return true;
        }

        private static BasicCubicle ChooseTargetCubicle(Device device, Character selected, out int chosenIndex)
        {
            List<BasicCubicle> list = device.cubicle_list;
            chosenIndex = 0;

            if (list.Count == 1)
            {
                return list[0];
            }

            // If character is already in one of this device's cubicles, cycle to the next cubicle on this device
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].worker == selected)
                {
                    chosenIndex = (i + 1) % list.Count;
                    return list[chosenIndex];
                }
            }

            // Otherwise pick the first empty cubicle
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].worker == null)
                {
                    chosenIndex = i;
                    return list[i];
                }
            }

            // If all cubicles are occupied, assign to the primary cubicle (index 0)
            chosenIndex = 0;
            return list[0];
        }

        private void TrySwapBackupCharacter(CharacterMgr charMgr, int frontIndex)
        {
            if (charMgr.characters == null || charMgr.backup == null || charMgr.backup.backup_characters == null)
            {
                return;
            }

            if (charMgr.backup.backup_characters.Count == 0)
            {
                SetStatus("当前没有替补席角色可供轮换");
                return;
            }

            if (frontIndex < 0 || frontIndex >= charMgr.characters.Count)
            {
                // If pressing Alt + slot beyond current front count, bring first backup to front
                Character backupToFront = charMgr.backup.backup_characters[0];
                if (backupToFront != null)
                {
                    Character_Helper.TakeCharacterToFront(backupToFront, false);
                    SetStatus("已从替补席调入角色至前排");
                }
                return;
            }

            Character frontChar = charMgr.characters[frontIndex];
            Character backupChar = charMgr.backup.backup_characters[0];
            if (frontChar != null && backupChar != null)
            {
                BasicCubicle existingCubicle = Cubicle_Helper.GetCubicle(frontChar);
                if (existingCubicle != null)
                {
                    Cubicle_Helper.EmptyCubicle(existingCubicle);
                }

                Character_Helper.TakeCharacterToBackup(frontChar, true);
                Character_Helper.TakeCharacterToFront(backupChar, true);

                if (existingCubicle != null && backupChar.StateCanWork())
                {
                    try
                    {
                        Cubicle_Helper.SetSelectCharacter(backupChar);
                        Cubicle_Helper.SetCubicle(existingCubicle);
                        charMgr.CancelSelectCharacter();
                    }
                    catch
                    {
                        charMgr.CancelSelectCharacter();
                    }
                }

                SetStatus($"已轮换 {frontIndex + 1} 号角色与替补角色");
            }
        }

        private void SetStatus(string msg)
        {
            LastActionStatus = msg;
            LastActionStatusTime = Time.unscaledTime;
        }
    }
}
