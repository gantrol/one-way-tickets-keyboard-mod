using System;
using System.Collections.Generic;
using Foundations;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.Characters;
using World.Cubicles;
using World.Devices;
using World.Devices.Device_AI;
using World.Helpers;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Handles Right-Hand Character controls (Y U I O P [ and 1..6 aliases, plus ' / ] Auto-Assign):
    /// - Y U I O P [ (or 1..6): Select front-line character 1..6
    /// - Selected character -> A..G (or Q..T): Dispatch character to device cubicle (cycles primary/secondary cubicle)
    /// - ' (Quote) or ] (RightBracket): One-key Smart Optimal Character Assignment across all weapon cubicles
    /// - Ctrl + ' (or Ctrl + ]): Toggle continuous Character Auto-Assign hosting
    /// - Double-tap character key or press Backspace / 0: Recall character from cubicle to rest
    /// - Alt + character key: Swap front-line character with backup character
    /// </summary>
    public sealed class KeyboardCharacterController
    {
        private readonly DeviceSlotResolver _slotResolver;
        private readonly float[] _lastCharKeyPressTime = new float[6];
        private readonly bool[] _consumedSlotKeyUntilRelease = new bool[DeviceSlotResolver.MaxWeaponSlots];

        private bool _autoAssignEnabled;
        private int _autoAssignTickCounter;

        public string LastActionStatus { get; private set; } = string.Empty;
        public float LastActionStatusTime { get; private set; }
        public int LastTargetSlotIndex { get; private set; } = -1;
        public float LastTargetSlotTime { get; private set; }
        public bool IsAutoAssignEnabled => _autoAssignEnabled;

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

        public void ToggleContinuousAutoAssign()
        {
            _autoAssignEnabled = !_autoAssignEnabled;
            string autoKey = ModConfig.FormatKeyLabel(ModConfig.Instance.CharacterAutoAssignKey);
            if (_autoAssignEnabled)
            {
                Mission mission = Singleton<Mission>.instance;
                if (mission != null && mission.try_get_mgr("CharacterMgr", out CharacterMgr charMgr) && charMgr != null)
                {
                    PerformOptimalCharacterAssignment(charMgr, false, out int assignedCount);
                    SetStatus($"[Ctrl+{autoKey}] 已开启【人物持续自动分配】 (已分配 {assignedCount} 人)");
                }
                else
                {
                    SetStatus($"[Ctrl+{autoKey}] 已开启【人物持续自动分配】");
                }
            }
            else
            {
                SetStatus($"[Ctrl+{autoKey}] 已关闭【人物持续自动分配】");
            }
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

            // Clear consumed slot flags when weapon/maintain keys are released
            for (int i = 0; i < _consumedSlotKeyUntilRelease.Length; i++)
            {
                Key wk = i < cfg.WeaponSlotKeys.Length ? cfg.WeaponSlotKeys[i] : Key.None;
                Key mk = i < cfg.SlotMaintainKeys.Length ? cfg.SlotMaintainKeys[i] : Key.None;
                bool wkDown = wk != Key.None && kb[wk].isPressed;
                bool mkDown = mk != Key.None && kb[mk].isPressed;
                if (!wkDown && !mkDown)
                {
                    _consumedSlotKeyUntilRelease[i] = false;
                }
            }

            bool altHeld = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            bool ctrlHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool shiftHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool wakeUpHeld = cfg.ToolWakeUpKey != Key.None && kb[cfg.ToolWakeUpKey].isPressed;

            // 1. Character Auto-Assign key (' or ])
            bool autoAssignPressed =
                (cfg.CharacterAutoAssignKey != Key.None && kb[cfg.CharacterAutoAssignKey].wasPressedThisFrame) ||
                (cfg.CharacterAutoAssignAltKey != Key.None && kb[cfg.CharacterAutoAssignAltKey].wasPressedThisFrame);

            if (autoAssignPressed && !altHeld)
            {
                string autoKeyLabel = ModConfig.FormatKeyLabel(cfg.CharacterAutoAssignKey);
                if (ctrlHeld)
                {
                    ToggleContinuousAutoAssign();
                }
                else
                {
                    int count = PerformOptimalCharacterAssignment(charMgr, true, out _);
                    if (count > 0)
                    {
                        SetStatus($"[{autoKeyLabel}:自动分配] 已按角色专长最优派驻 {count} 名角色到武器工位! (Ctrl+{autoKeyLabel} 持续托管)");
                    }
                    else
                    {
                        SetStatus($"[{autoKeyLabel}:自动分配] 当前无可用空闲角色或可用武器工位");
                    }
                }
                return;
            }

            // 2. Character 1..6 keys (Y U I O P [ and 1..6 aliases)
            if (!wakeUpHeld)
            {
                for (int i = 0; i < 6; i++)
                {
                    Key primaryKey = i < cfg.CharacterKeys.Length ? cfg.CharacterKeys[i] : Key.None;
                    Key altKey = i < cfg.CharacterAltKeys.Length ? cfg.CharacterAltKeys[i] : Key.None;
                    bool pressed = (primaryKey != Key.None && kb[primaryKey].wasPressedThisFrame) ||
                                   (altKey != Key.None && kb[altKey].wasPressedThisFrame);

                    if (pressed)
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

            // 3. Recall / Deselect keys: Backspace or 0
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

            // 4. Check if a character is currently selected and player presses A..G (or Q..T) to assign cubicle
            Character currentSelected = Cubicle_Helper.GetSelectCharacter();
            if (currentSelected != null && !altHeld && !ctrlHeld && !shiftHeld)
            {
                for (int slotIdx = 0; slotIdx < DeviceSlotResolver.MaxWeaponSlots; slotIdx++)
                {
                    Key wk = slotIdx < cfg.WeaponSlotKeys.Length ? cfg.WeaponSlotKeys[slotIdx] : Key.None;
                    Key mk = slotIdx < cfg.SlotMaintainKeys.Length ? cfg.SlotMaintainKeys[slotIdx] : Key.None;
                    bool pressed = (wk != Key.None && kb[wk].wasPressedThisFrame) ||
                                   (mk != Key.None && kb[mk].wasPressedThisFrame);

                    if (pressed)
                    {
                        if (TryDispatchSelectedCharacterToSlot(charMgr, currentSelected, slotIdx))
                        {
                            _consumedSlotKeyUntilRelease[slotIdx] = true;
                        }
                    }
                }
            }
        }

        public void Tick120()
        {
            if (!_autoAssignEnabled)
            {
                return;
            }

            _autoAssignTickCounter++;
            if (_autoAssignTickCounter < 30)
            {
                return;
            }
            _autoAssignTickCounter = 0;

            try
            {
                Mission mission = Singleton<Mission>.instance;
                if (mission != null && mission.try_get_mgr("CharacterMgr", out CharacterMgr charMgr) && charMgr != null)
                {
                    PerformOptimalCharacterAssignment(charMgr, false, out _);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Assigns available characters to weapon cubicles sorted by urgency and character ability.
        /// When forceReoptimize is true (on manual key press), re-optimizes all working + idle characters across all cubicles.
        /// When forceReoptimize is false (in continuous background tick), fills empty cubicles with idle characters
        /// and shifts solo workers on a device between Attack and Load/Carry/Sharp cubicles when ammo/sharpness is depleted.
        /// </summary>
        private int PerformOptimalCharacterAssignment(CharacterMgr charMgr, bool forceReoptimize, out int assignedCount)
        {
            assignedCount = 0;
            BattleContext bctx = Singleton<BattleContext>.instance;
            if (bctx != null && !bctx.active_cubicle)
            {
                return 0;
            }

            if (charMgr.characters == null || charMgr.characters.Count == 0)
            {
                return 0;
            }

            IReadOnlyList<ResolvedDeviceSlot> slots = _slotResolver.GetOrderedSlots();
            if (slots.Count == 0)
            {
                return 0;
            }

            // First: handle solo-worker station shifting on multi-cubicle devices (e.g., Gun out of ammo -> move worker to Load; Gun loaded -> move back to Attack)
            ShiftSoloWorkersBetweenCubicles(charMgr, slots, ref assignedCount);

            // Collect candidate characters
            List<Character> availableChars = new List<Character>(charMgr.characters.Count);
            for (int i = 0; i < charMgr.characters.Count; i++)
            {
                Character c = charMgr.characters[i];
                if (c == null || c.state != null || !c.StateCanWork())
                {
                    continue;
                }

                BasicCubicle existingCub = Cubicle_Helper.GetCubicle(c);
                if (forceReoptimize || existingCub == null)
                {
                    availableChars.Add(c);
                }
            }

            if (availableChars.Count == 0)
            {
                return assignedCount;
            }

            if (forceReoptimize)
            {
                // Clear existing cubicles for the characters we are about to optimally re-assign
                for (int i = 0; i < availableChars.Count; i++)
                {
                    BasicCubicle existing = Cubicle_Helper.GetCubicle(availableChars[i]);
                    if (existing != null)
                    {
                        Cubicle_Helper.EmptyCubicle(existing);
                    }
                }
            }

            // Build prioritized list of target cubicles across all active devices
            List<DeviceCubicleTarget> targets = new List<DeviceCubicleTarget>(10);

            // Pass 1: Urgent cubicles (e.g. Load/Carry cubicle when weapon is out of ammo, or Attack cubicle when weapon has ammo)
            for (int i = 0; i < slots.Count && i < DeviceSlotResolver.MaxWeaponSlots; i++)
            {
                Device dev = slots[i].Device;
                if (dev == null || !dev.is_validate || dev.cubicle_list == null || dev.cubicle_list.Count == 0)
                {
                    continue;
                }

                BasicCubicle primaryOrUrgent = GetMostUrgentCubicleForDevice(dev);
                if (primaryOrUrgent != null && primaryOrUrgent.worker == null)
                {
                    targets.Add(new DeviceCubicleTarget { SlotIndex = i, Device = dev, Cubicle = primaryOrUrgent });
                }
            }

            // Pass 2: Remaining empty cubicles on all devices
            for (int i = 0; i < slots.Count && i < DeviceSlotResolver.MaxWeaponSlots; i++)
            {
                Device dev = slots[i].Device;
                if (dev == null || !dev.is_validate || dev.cubicle_list == null)
                {
                    continue;
                }

                for (int cIdx = 0; cIdx < dev.cubicle_list.Count; cIdx++)
                {
                    BasicCubicle cub = dev.cubicle_list[cIdx];
                    if (cub == null || cub.worker != null)
                    {
                        continue;
                    }

                    bool alreadyAdded = false;
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (targets[t].Cubicle == cub)
                        {
                            alreadyAdded = true;
                            break;
                        }
                    }
                    if (!alreadyAdded)
                    {
                        targets.Add(new DeviceCubicleTarget { SlotIndex = i, Device = dev, Cubicle = cub });
                    }
                }
            }

            // Match each cubicle target with the available character who has the highest GetAbility(device)
            for (int t = 0; t < targets.Count && availableChars.Count > 0; t++)
            {
                DeviceCubicleTarget target = targets[t];
                if (target.Cubicle.worker != null)
                {
                    continue;
                }

                int bestCharIdx = -1;
                float bestAbility = -999f;

                for (int cIdx = 0; cIdx < availableChars.Count; cIdx++)
                {
                    Character candidate = availableChars[cIdx];
                    if (!candidate.CanWork(target.Device))
                    {
                        continue;
                    }

                    float ability = candidate.GetAbility(target.Device);
                    if (ability > bestAbility)
                    {
                        bestAbility = ability;
                        bestCharIdx = cIdx;
                    }
                }

                if (bestCharIdx >= 0)
                {
                    Character chosen = availableChars[bestCharIdx];
                    availableChars.RemoveAt(bestCharIdx);

                    if (AssignCharacterDirectly(charMgr, chosen, target.Cubicle))
                    {
                        assignedCount++;
                        LastTargetSlotIndex = target.SlotIndex;
                        LastTargetSlotTime = Time.unscaledTime;
                    }
                }
            }

            charMgr.CancelSelectCharacter();
            return assignedCount;
        }

        private void ShiftSoloWorkersBetweenCubicles(CharacterMgr charMgr, IReadOnlyList<ResolvedDeviceSlot> slots, ref int assignedCount)
        {
            for (int i = 0; i < slots.Count && i < DeviceSlotResolver.MaxWeaponSlots; i++)
            {
                Device dev = slots[i].Device;
                if (dev == null || !dev.is_validate || dev.cubicle_list == null || dev.cubicle_list.Count < 2)
                {
                    continue;
                }

                int workingCount = 0;
                Character soloWorker = null;
                BasicCubicle currentCub = null;

                for (int cIdx = 0; cIdx < dev.cubicle_list.Count; cIdx++)
                {
                    BasicCubicle cub = dev.cubicle_list[cIdx];
                    if (cub != null && cub.worker != null && cub.worker.state == null && cub.worker.StateCanWork())
                    {
                        workingCount++;
                        soloWorker = cub.worker;
                        currentCub = cub;
                    }
                }

                if (workingCount != 1 || soloWorker == null)
                {
                    continue;
                }

                BasicCubicle urgentCub = GetMostUrgentCubicleForDevice(dev);
                if (urgentCub != null && urgentCub != currentCub && urgentCub.worker == null)
                {
                    if (AssignCharacterDirectly(charMgr, soloWorker, urgentCub))
                    {
                        assignedCount++;
                    }
                }
            }
        }

        private static BasicCubicle GetMostUrgentCubicleForDevice(Device dev)
        {
            if (dev.cubicle_list == null || dev.cubicle_list.Count == 0)
            {
                return null;
            }

            if (dev.cubicle_list.Count == 1)
            {
                return dev.cubicle_list[0];
            }

            bool needsReload = false;
            bool needsCarry = false;
            bool needsSharp = false;

            if (dev is DScript_Shooter_Gatlin gatlin)
            {
                if (gatlin.current_ammo <= 0)
                {
                    needsCarry = gatlin.carry_count <= 0;
                    needsReload = gatlin.carry_count > 0;
                }
            }
            else if (dev is DScript_Shooter shooter)
            {
                if (shooter.current_ammo <= 0)
                {
                    needsReload = true;
                }
            }
            else if (dev is DScript_Melee_Claymore claymore)
            {
                if (claymore.Get_Sharpness() <= 0.05f)
                {
                    needsSharp = true;
                }
            }

            if (needsCarry)
            {
                foreach (BasicCubicle cub in dev.cubicle_list)
                {
                    if (cub is DeviceCarryCubicle) return cub;
                }
            }
            if (needsReload)
            {
                foreach (BasicCubicle cub in dev.cubicle_list)
                {
                    if (cub is DeviceLoadCubicle) return cub;
                }
            }
            if (needsSharp)
            {
                foreach (BasicCubicle cub in dev.cubicle_list)
                {
                    if (cub is DeviceSharpCubicle) return cub;
                }
            }

            foreach (BasicCubicle cub in dev.cubicle_list)
            {
                if (cub is DeviceAttackCubicle) return cub;
            }

            return dev.cubicle_list[0];
        }

        private static bool AssignCharacterDirectly(CharacterMgr charMgr, Character character, BasicCubicle cubicle)
        {
            try
            {
                Cubicle_Helper.SetSelectCharacter(character);
                Cubicle_Helper.SetCubicle(cubicle);
                return cubicle.worker == character;
            }
            catch
            {
                try { charMgr.CancelSelectCharacter(); } catch { }
                return false;
            }
        }

        private struct DeviceCubicleTarget
        {
            public int SlotIndex;
            public Device Device;
            public BasicCubicle Cubicle;
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

            string charKeyLabel = charIndex < ModConfig.Instance.CharacterKeys.Length
                ? ModConfig.FormatKeyLabel(ModConfig.Instance.CharacterKeys[charIndex])
                : (charIndex + 1).ToString();

            // Double-tap -> Recall character from cubicle to rest
            if (elapsed <= ModConfig.Instance.DoubleTapRecallSeconds)
            {
                RecallCharacter(charMgr, targetChar, charIndex);
                return;
            }

            // Single tap -> Select character
            Cubicle_Helper.SetSelectCharacter(targetChar);
            SetStatus($"已选中 {charIndex + 1}号角色[{charKeyLabel}] (按左手 A-G 派驻工位，双击 {charKeyLabel} 撤回)");
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

            string keyLabel = slotIndex < ModConfig.Instance.WeaponSlotKeys.Length
                ? ModConfig.FormatKeyLabel(ModConfig.Instance.WeaponSlotKeys[slotIndex])
                : (slotIndex + 1).ToString();

            LastTargetSlotIndex = slotIndex;
            LastTargetSlotTime = Time.unscaledTime;

            if (!selected.StateCanWork())
            {
                string wakeKey = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolWakeUpKey);
                SetStatus($"[{keyLabel}] 该角色当前处于昏迷状态！(按 {wakeKey} 或 Space 唤醒)");
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

            // Otherwise pick the most urgent empty cubicle first
            BasicCubicle urgent = GetMostUrgentCubicleForDevice(device);
            if (urgent != null && urgent.worker == null)
            {
                int idx = list.IndexOf(urgent);
                chosenIndex = idx >= 0 ? idx : 0;
                return urgent;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].worker == null)
                {
                    chosenIndex = i;
                    return list[i];
                }
            }

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
