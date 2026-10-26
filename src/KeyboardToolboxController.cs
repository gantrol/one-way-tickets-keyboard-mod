using System;
using System.Collections.Generic;
using AutoCodes;
using Commons;
using Foundations;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.Adherents;
using World.Caravans;
using World.Characters;
using World.Characters.CharacterStates;
using World.Devices;
using World.Devices.DeviceEmergencies;
using World.Devices.DeviceEmergencies.DeviceEmergenciesView;
using World.Devices.DeviceUiViews;
using World.Helpers;
using World.Player_Tool;
using World.Player_Tool.ToolsLogic;
using World.Player_Tool.ToolsLogic.Rags;
using World.Player_Tool.ToolsLogic.Saws;
using World.Player_Tool.ToolsLogic.WakeUp;
using World.Widgets;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Handles Row 3 (A S D F G H) and Row 4 (Z X C V B + Space) Toolbox controls:
    /// - Row 3 (A..H): Direct per-slot maintenance aligned under fixed slots Q..Y
    /// - Row 4 (Z X C V B): Specific tool category maintenance across the caravan, respecting each tool's upgrade stage
    ///   (e.g., Rag lv0 single-drop wipe -> BigRag/Mop lv1-2 hold per-device wipe -> Drier lv3 global blow-dry;
    ///    Saw lv0 -> Scissors/ChainSaw lv1-2 -> CircleSaw lv3;
    ///    InflatingHammer/ScreamingChicken -> Clock/Tweeter -> AED global wake-up)
    /// - Space: Global Smart Maintenance
    /// </summary>
    public sealed class KeyboardToolboxController
    {
        private readonly DeviceSlotResolver _slotResolver;

        private readonly bool[] _holdingSlotMaintain = new bool[6];
        private bool _holdingWrench;
        private bool _holdingExtinguisher;
        private bool _holdingSaw;
        private bool _holdingRag;
        private bool _holdingWakeUp;
        private bool _holdingGlobalSmart;

        private int _sawCooldownTicks;
        private int _ragCooldownTicks;
        private int _stuporCooldownTicks;
        private int _wakeUpTapCooldownTicks;
        private int _sfxCooldownTicks;

        private readonly Dictionary<Device, float> _fallbackFixProgress = new Dictionary<Device, float>();
        private PlayerToolMgrView _cachedToolMgrView;
        private float _lastToolViewSearchTime;

        public string LastActionStatus { get; private set; } = string.Empty;
        public float LastActionStatusTime { get; private set; }
        public int LastTargetSlotIndex { get; private set; } = -1;
        public float LastTargetSlotTime { get; private set; }

        public KeyboardToolboxController(DeviceSlotResolver slotResolver)
        {
            _slotResolver = slotResolver;
        }

        public bool IsSlotMaintaining(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _holdingSlotMaintain.Length)
            {
                return false;
            }
            return _holdingSlotMaintain[slotIndex];
        }

        public bool IsGlobalSmartMaintaining => _holdingGlobalSmart;

        public void UpdateInput(Keyboard kb)
        {
            if (kb == null)
            {
                return;
            }

            ModConfig cfg = ModConfig.Instance;
            bool ctrlHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool altHeld = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;

            // Row 3: Per-slot direct maintenance keys (A S D F G H)
            for (int i = 0; i < cfg.SlotMaintainKeys.Length && i < 6; i++)
            {
                Key k = cfg.SlotMaintainKeys[i];
                if (k == Key.None || ctrlHeld || altHeld)
                {
                    _holdingSlotMaintain[i] = false;
                    continue;
                }

                _holdingSlotMaintain[i] = kb[k].isPressed;
                if (kb[k].wasPressedThisFrame)
                {
                    OnSlotMaintainPressed(i);
                }
            }

            // Row 4: Specific tool keys (Z X C V B) + Global Smart Maintain (Space)
            bool zDown = !ctrlHeld && !altHeld && cfg.ToolWrenchKey != Key.None && kb[cfg.ToolWrenchKey].wasPressedThisFrame;
            bool xDown = !ctrlHeld && !altHeld && cfg.ToolExtinguisherKey != Key.None && kb[cfg.ToolExtinguisherKey].wasPressedThisFrame;
            bool cDown = !ctrlHeld && !altHeld && cfg.ToolSawKey != Key.None && kb[cfg.ToolSawKey].wasPressedThisFrame;
            bool vDown = !ctrlHeld && !altHeld && cfg.ToolRagKey != Key.None && kb[cfg.ToolRagKey].wasPressedThisFrame;
            bool bDown = !ctrlHeld && !altHeld && cfg.ToolWakeUpKey != Key.None && kb[cfg.ToolWakeUpKey].wasPressedThisFrame;
            bool spaceDown = !ctrlHeld && !altHeld && cfg.GlobalSmartMaintainKey != Key.None && kb[cfg.GlobalSmartMaintainKey].wasPressedThisFrame;

            _holdingWrench = !ctrlHeld && !altHeld && cfg.ToolWrenchKey != Key.None && kb[cfg.ToolWrenchKey].isPressed;
            _holdingExtinguisher = !ctrlHeld && !altHeld && cfg.ToolExtinguisherKey != Key.None && kb[cfg.ToolExtinguisherKey].isPressed;
            _holdingSaw = !ctrlHeld && !altHeld && cfg.ToolSawKey != Key.None && kb[cfg.ToolSawKey].isPressed;
            _holdingRag = !ctrlHeld && !altHeld && cfg.ToolRagKey != Key.None && kb[cfg.ToolRagKey].isPressed;
            _holdingWakeUp = !ctrlHeld && !altHeld && cfg.ToolWakeUpKey != Key.None && kb[cfg.ToolWakeUpKey].isPressed;
            _holdingGlobalSmart = !ctrlHeld && !altHeld && cfg.GlobalSmartMaintainKey != Key.None && kb[cfg.GlobalSmartMaintainKey].isPressed;

            // B + 1..6: Targeted character wake-up on press
            if (_holdingWakeUp)
            {
                for (int i = 0; i < cfg.CharacterKeys.Length && i < 6; i++)
                {
                    Key ck = cfg.CharacterKeys[i];
                    if (ck != Key.None && kb[ck].wasPressedThisFrame)
                    {
                        TryWakeUpCharacterByIndex(i, true);
                    }
                }
            }

            IReadOnlyList<ResolvedDeviceSlot> slots = null;
            if (zDown || xDown || cDown || vDown || bDown || spaceDown)
            {
                slots = _slotResolver.GetOrderedSlots();
            }

            if (zDown)
            {
                _stuporCooldownTicks = 0;
                OnWrenchKeyPressed(slots);
            }
            if (xDown)
            {
                OnExtinguisherKeyPressed(slots);
            }
            if (cDown)
            {
                _sawCooldownTicks = 0;
                OnSawKeyPressed(slots);
            }
            if (vDown)
            {
                _ragCooldownTicks = 0;
                OnRagKeyPressed(slots);
            }
            if (bDown)
            {
                _wakeUpTapCooldownTicks = 0;
                OnWakeUpKeyPressed();
            }
            if (spaceDown)
            {
                _sawCooldownTicks = 0;
                _ragCooldownTicks = 0;
                _stuporCooldownTicks = 0;
                _wakeUpTapCooldownTicks = 0;
                OnGlobalSmartKeyPressed(slots);
            }
        }

        /// <summary>
        /// Executed every 120 TPS physics/logic tick via Foundations.Tickers.Ticker.
        /// Respects tool stages: Lv0 click/tap tools require pressing (or slower cadence), while upgraded tools support continuous hold or global effect.
        /// </summary>
        public void Tick120()
        {
            if (_sawCooldownTicks > 0) _sawCooldownTicks--;
            if (_ragCooldownTicks > 0) _ragCooldownTicks--;
            if (_stuporCooldownTicks > 0) _stuporCooldownTicks--;
            if (_wakeUpTapCooldownTicks > 0) _wakeUpTapCooldownTicks--;
            if (_sfxCooldownTicks > 0) _sfxCooldownTicks--;

            IReadOnlyList<ResolvedDeviceSlot> slots = _slotResolver.GetOrderedSlots();

            // 1. Per-slot maintenance (A S D F G H)
            for (int i = 0; i < slots.Count && i < 6; i++)
            {
                if (_holdingSlotMaintain[i])
                {
                    MaintainSingleSlotTick(slots[i], false);
                }
            }

            // 2. Specific tool maintenance (Z X C V B)
            if (_holdingWrench)
            {
                if (!MaintainBrokenDevicesTick(slots, out _))
                {
                    if (!MaintainStuporDevicesTick(slots, out _))
                    {
                        TryUseCaravanRepairKitOnDamaged(slots, out _);
                    }
                }
            }
            if (_holdingExtinguisher)
            {
                MaintainFiresTick(slots, out _);
            }
            if (_holdingSaw)
            {
                MaintainVinesTick(slots, false, out _);
            }
            if (_holdingRag)
            {
                MaintainWetAndAcidTick(slots, false, out _);
            }
            if (_holdingWakeUp)
            {
                TryWakeUpAllComaCharacters(false, out _);
            }

            // 3. Global Smart Maintenance (Space)
            if (_holdingGlobalSmart)
            {
                TryWakeUpAllComaCharacters(false, out _);
                MaintainFiresTick(slots, out _);
                MaintainVinesTick(slots, false, out _);
                MaintainWetAndAcidTick(slots, false, out _);
                if (!MaintainBrokenDevicesTick(slots, out _))
                {
                    TryUseCaravanRepairKitOnDamaged(slots, out _);
                }
                MaintainStuporDevicesTick(slots, out _);
            }
        }

        private void OnSlotMaintainPressed(int slotIndex)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.SlotMaintainKeys[slotIndex]);
            MarkSlotFeedback(slotIndex);

            if (!_slotResolver.TryGetSlot(slotIndex, out ResolvedDeviceSlot slot))
            {
                SetStatus($"[{keyLabel}] 当前车体无 {slotIndex + 1} 号槽位");
                return;
            }

            if (slot.Device == null)
            {
                SetStatus($"[{keyLabel}] {slotIndex + 1} 号槽位为空（未安装设备）");
                return;
            }

            Device device = slot.Device;
            string devName = GetDeviceDisplayName(device);

            _sawCooldownTicks = 0;
            _ragCooldownTicks = 0;
            _stuporCooldownTicks = 0;
            MaintainSingleSlotTick(slot, true);

            if (!device.is_validate)
            {
                string wrenchName = GetToolDisplayName(GetWrenchToolId(), "扳手");
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 使用【{wrenchName}】修缮损毁设备 (请按住 {keyLabel})");
                return;
            }

            DeviceEmergency em = device.device_emergency;
            if (em != null && !em.removed)
            {
                if (em is DeviceStupor)
                {
                    string wrenchName = GetToolDisplayName(GetWrenchToolId(), "扳手");
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 使用【{wrenchName}】拍醒停滞设备！");
                }
                else if (em is DeviceFired fired)
                {
                    string extName = GetToolDisplayName(GetExtinguisherToolId(), "灭火器");
                    int pct = fired.fire_value_max > 0f ? Mathf.CeilToInt((fired.fire_value / fired.fire_value_max) * 100f) : 0;
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 使用【{extName}】灭火 (余火 {pct}%，请按住 {keyLabel})");
                }
                else if (em is DeviceBind bind)
                {
                    string sawName = GetToolDisplayName(GetSawToolId(), "除藤工具");
                    int remain = bind.vine_list != null ? bind.vine_list.Count : 0;
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 使用【{sawName}】清理藤蔓 (剩余 {remain} 根)");
                }
                else if (em is DeviceWet wet)
                {
                    string ragName = GetRagStageDescription();
                    int remain = wet.waterdrops != null ? wet.waterdrops.Count : 0;
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 使用【{ragName}】擦除水滴 (剩余 {remain} 滴)");
                }
                else if (em is DeviceAcid acid)
                {
                    string ragName = GetRagStageDescription();
                    int remain = acid.acids != null ? acid.acids.Count : 0;
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 使用【{ragName}】清理酸液 (剩余 {remain} 处)");
                }
                return;
            }

            // If device is not broken and has no emergency, check if it has lost HP and can use the caravan repair kit
            if (device.current_hp < device.max_hp)
            {
                if (TryUseCaravanRepairKitOnSlot(slot))
                {
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 已使用【车载维修包】恢复耐久 ({device.current_hp}/{device.max_hp})！");
                }
                else
                {
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 设备未彻底损毁（扳手仅修 0 血损毁，维修包当前冷却中）");
                }
                return;
            }

            SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 状态完好 ({device.current_hp}/{device.max_hp})，无需抢修");
        }

        private void OnWrenchKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolWrenchKey);
            string wrenchName = GetToolDisplayName(GetWrenchToolId(), "扳手");

            if (MaintainBrokenDevicesTick(slots, out int brokenSlot))
            {
                MarkSlotFeedback(brokenSlot);
                string devName = GetSlotDeviceName(slots, brokenSlot);
                SetStatus($"[{keyLabel}:{wrenchName}] 正在修缮 {brokenSlot + 1}号位【{devName}】 (请按住 {keyLabel})");
                return;
            }
            if (MaintainStuporDevicesTick(slots, out int stuporSlot))
            {
                MarkSlotFeedback(stuporSlot);
                string devName = GetSlotDeviceName(slots, stuporSlot);
                SetStatus($"[{keyLabel}:{wrenchName}] 已拍醒 {stuporSlot + 1}号位【{devName}】！");
                return;
            }
            if (TryUseCaravanRepairKitOnDamaged(slots, out string repairMsg))
            {
                SetStatus($"[{keyLabel}:车载维修包] {repairMsg}");
                return;
            }
            SetStatus($"[{keyLabel}:{wrenchName}] 当前无 0 血损毁或停滞设备需要修缮");
        }

        private void OnExtinguisherKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolExtinguisherKey);
            string extName = GetToolDisplayName(GetExtinguisherToolId(), "灭火器");

            if (MaintainFiresTick(slots, out int fireSlot))
            {
                MarkSlotFeedback(fireSlot);
                string devName = GetSlotDeviceName(slots, fireSlot);
                SetStatus($"[{keyLabel}:{extName}] 正在扑灭 {fireSlot + 1}号位【{devName}】火焰 (请按住 {keyLabel})");
            }
            else
            {
                SetStatus($"[{keyLabel}:{extName}] 当前没有着火的设备");
            }
        }

        private void OnSawKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolSawKey);
            string sawStage = GetSawStageDescription();

            if (MaintainVinesTick(slots, true, out int vineSlot))
            {
                MarkSlotFeedback(vineSlot);
                string devName = GetSlotDeviceName(slots, vineSlot);
                SetStatus($"[{keyLabel}:{sawStage}] 已对 {vineSlot + 1}号位【{devName}】清除藤蔓！");
            }
            else
            {
                SetStatus($"[{keyLabel}:{sawStage}] 当前没有被藤蔓缠绕的设备");
            }
        }

        private void OnRagKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolRagKey);
            string ragStage = GetRagStageDescription();

            if (MaintainWetAndAcidTick(slots, true, out int wipedSlot))
            {
                MarkSlotFeedback(wipedSlot);
                if (IsGlobalDrierStage())
                {
                    SetStatus($"[{keyLabel}:{ragStage}] 吹风筒生效：已同步吹干全车设备水滴/酸液！");
                }
                else
                {
                    string devName = GetSlotDeviceName(slots, wipedSlot);
                    SetStatus($"[{keyLabel}:{ragStage}] 已擦拭 {wipedSlot + 1}号位【{devName}】上的水滴/酸液！");
                }
            }
            else
            {
                SetStatus($"[{keyLabel}:{ragStage}] 当前没有进水或被酸液腐蚀的设备");
            }
        }

        private void OnWakeUpKeyPressed()
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolWakeUpKey);
            string wakeStage = GetWakeStageDescription();

            if (TryWakeUpAllComaCharacters(true, out int wokeCharIndex))
            {
                if (IsGlobalAedStage())
                {
                    SetStatus($"[{keyLabel}:{wakeStage}] 除颤仪全车电击：正在唤醒所有昏迷角色！");
                }
                else
                {
                    SetStatus($"[{keyLabel}:{wakeStage}] 正在唤醒 {wokeCharIndex + 1} 号昏迷角色！");
                }
            }
            else
            {
                SetStatus($"[{keyLabel}:{wakeStage}] 当前没有处于昏迷状态的角色");
            }
        }

        private void OnGlobalSmartKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            List<string> actions = new List<string>(4);

            if (TryWakeUpAllComaCharacters(true, out int wokeIdx))
            {
                actions.Add($"唤醒{wokeIdx + 1}号角色({GetWakeStageDescription()})");
            }
            if (MaintainFiresTick(slots, out int fireSlot))
            {
                MarkSlotFeedback(fireSlot);
                actions.Add($"扑灭{fireSlot + 1}号火情");
            }
            if (MaintainVinesTick(slots, true, out int vineSlot))
            {
                MarkSlotFeedback(vineSlot);
                actions.Add($"清理{vineSlot + 1}号藤蔓({GetSawStageDescription()})");
            }
            if (MaintainWetAndAcidTick(slots, true, out int wetSlot))
            {
                MarkSlotFeedback(wetSlot);
                actions.Add(IsGlobalDrierStage() ? $"全车吹干({GetRagStageDescription()})" : $"擦拭{wetSlot + 1}号液体({GetRagStageDescription()})");
            }
            if (MaintainBrokenDevicesTick(slots, out int brokenSlot))
            {
                MarkSlotFeedback(brokenSlot);
                actions.Add($"扳手抢修{brokenSlot + 1}号损毁设备");
            }
            if (MaintainStuporDevicesTick(slots, out int stuporSlot))
            {
                MarkSlotFeedback(stuporSlot);
                actions.Add($"拍醒{stuporSlot + 1}号停滞设备");
            }
            if (actions.Count == 0 && TryUseCaravanRepairKitOnDamaged(slots, out string repairMsg))
            {
                actions.Add(repairMsg);
            }

            if (actions.Count > 0)
            {
                SetStatus($"[Space:全局抢修] {string.Join(" + ", actions)}");
            }
            else
            {
                SetStatus("[Space:全局抢修] 全车设备与角色状态良好，暂无异常");
            }
        }

        private void MaintainSingleSlotTick(ResolvedDeviceSlot slot, bool isKeyDown)
        {
            Device device = slot.Device;
            if (device == null)
            {
                return;
            }

            // Priority 1: Broken device (!is_validate)
            if (!device.is_validate)
            {
                RepairDeviceTick(slot);
                return;
            }

            DeviceEmergency em = device.device_emergency;
            if (em != null && !em.removed)
            {
                if (em is DeviceFired fired)
                {
                    ExtinguishDeviceTick(slot.Index, fired);
                    return;
                }
                if (em is DeviceBind bind)
                {
                    if (_sawCooldownTicks <= 0 && (isKeyDown || CanHoldSawContinuously() || _sawCooldownTicks == 0))
                    {
                        CutVineOnDeviceByStage(slot, bind, isKeyDown);
                    }
                    return;
                }
                if (em is DeviceWet || em is DeviceAcid)
                {
                    if (_ragCooldownTicks <= 0)
                    {
                        WipeSlotByStage(slot, isKeyDown);
                    }
                    return;
                }
                if (em is DeviceStupor stupor)
                {
                    if (_stuporCooldownTicks <= 0)
                    {
                        stupor.Fix();
                        _stuporCooldownTicks = 15;
                        PlayToolOneShot(GetWrenchToolId());
                    }
                    return;
                }
            }

            // Priority 3: If device has lost HP and caravan repair kit is available, use it on key press
            if (isKeyDown && device.current_hp < device.max_hp)
            {
                TryUseCaravanRepairKitOnSlot(slot);
            }
        }

        private bool MaintainBrokenDevicesTick(IReadOnlyList<ResolvedDeviceSlot> slots, out int targetSlotIndex)
        {
            targetSlotIndex = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Device != null && !slots[i].Device.is_validate)
                {
                    targetSlotIndex = slots[i].Index;
                    RepairDeviceTick(slots[i]);
                    return true;
                }
            }
            return false;
        }

        private bool MaintainFiresTick(IReadOnlyList<ResolvedDeviceSlot> slots, out int firstFireSlotIndex)
        {
            firstFireSlotIndex = -1;
            bool any = false;
            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d != null && d.device_emergency is DeviceFired fired && !fired.removed)
                {
                    if (firstFireSlotIndex < 0)
                    {
                        firstFireSlotIndex = slots[i].Index;
                    }
                    ExtinguishDeviceTick(slots[i].Index, fired);
                    any = true;
                }
            }
            return any;
        }

        private bool MaintainVinesTick(IReadOnlyList<ResolvedDeviceSlot> slots, bool isKeyDown, out int cutSlotIndex)
        {
            cutSlotIndex = -1;
            if (_sawCooldownTicks > 0)
            {
                return false;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d != null && d.device_emergency is DeviceBind bind && !bind.removed && bind.vine_list != null && bind.vine_list.Count > 0)
                {
                    if (CutVineOnDeviceByStage(slots[i], bind, isKeyDown))
                    {
                        cutSlotIndex = slots[i].Index;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Stage-aware Rag / Mop / BigRag / Drier logic:
        /// - Stage 0 (Rag): Single-target precise wipe (wipes 1 drop on 1 device per interval; slower when held vs tapped)
        /// - Stage 1-2 (BigRag / Mop): Continuous hold scrubbing on the target device
        /// - Stage 3+ (Drier / Hairdryer): Blows across ALL devices on the caravan simultaneously!
        /// </summary>
        private bool MaintainWetAndAcidTick(IReadOnlyList<ResolvedDeviceSlot> slots, bool isKeyDown, out int wipedSlotIndex)
        {
            wipedSlotIndex = -1;
            if (_ragCooldownTicks > 0)
            {
                return false;
            }

            bool isGlobalDrier = IsGlobalDrierStage();
            bool any = false;

            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d == null || d.device_emergency == null || d.device_emergency.removed)
                {
                    continue;
                }

                if ((d.device_emergency is DeviceAcid acid && acid.acids != null && acid.acids.Count > 0) ||
                    (d.device_emergency is DeviceWet wet && wet.waterdrops != null && wet.waterdrops.Count > 0))
                {
                    if (WipeSlotByStage(slots[i], isKeyDown))
                    {
                        if (wipedSlotIndex < 0)
                        {
                            wipedSlotIndex = slots[i].Index;
                        }
                        any = true;
                        // Only Drier (stage 3) blows dry all devices simultaneously; Rag/BigRag/Mop focus on one device at a time
                        if (!isGlobalDrier)
                        {
                            break;
                        }
                    }
                }
            }

            return any;
        }

        private bool WipeSlotByStage(ResolvedDeviceSlot slot, bool isKeyDown)
        {
            Device d = slot.Device;
            if (d?.device_emergency == null || d.device_emergency.removed)
            {
                return false;
            }

            Config cfg = Config.current;
            int wipeToolId = GetWipeToolId();
            int ragLevel = GetToolLevel(wipeToolId);
            ToolLogic activeLogic = GetActiveToolLogic(wipeToolId);

            float baseInterval = cfg != null ? cfg.rag_work_interval : 0.2f;
            float odds = Singleton<BattleContext>.instance != null
                ? Mathf.Max(0.35f, Singleton<BattleContext>.instance.rag_effective_odds)
                : 1f;

            // Determine stage behavior from actual ToolLogic instance or tool level:
            // - Rag (Lv0): requires precise click/wipe per drop (longer cooldown if held, immediate on tap)
            // - BigRag / Mop (Lv1/Lv2): continuous hold scrubbing on a single device
            // - Drier (Lv3): global blow-dry across all devices
            bool isBasicRag = (activeLogic is Rag && !(activeLogic is BigRag) && !(activeLogic is Mop) && !(activeLogic is Drier)) || ragLevel <= 0;
            bool isDrier = (activeLogic is Drier) || ragLevel >= 3;

            int cdTicks;
            if (isDrier)
            {
                cdTicks = Mathf.Max(4, Mathf.RoundToInt(baseInterval * 120f * 0.65f));
            }
            else if (isBasicRag)
            {
                // Basic rag: tapping is snappy, holding simulates having to aim and wipe each drop one by one
                float mult = isKeyDown ? 0.9f : 1.35f;
                cdTicks = Mathf.Max(6, Mathf.RoundToInt(baseInterval * 120f * mult));
            }
            else
            {
                // BigRag / Mop
                float mult = ragLevel >= 2 ? 0.75f : 0.9f;
                cdTicks = Mathf.Max(5, Mathf.RoundToInt(baseInterval * 120f * mult));
            }

            bool wiped = false;
            if (d.device_emergency is DeviceAcid acid && acid.acids != null && acid.acids.Count > 0)
            {
                if (UnityEngine.Random.Range(0f, 1f) <= odds || isKeyDown)
                {
                    SingleAcid targetAcid = acid.acids[acid.acids.Count - 1];
                    acid.RemoveAcid(targetAcid);
                }
                wiped = true;
            }
            else if (d.device_emergency is DeviceWet wet && wet.waterdrops != null && wet.waterdrops.Count > 0)
            {
                if (UnityEngine.Random.Range(0f, 1f) <= odds || isKeyDown)
                {
                    SingleWaterdrop drop = wet.waterdrops[wet.waterdrops.Count - 1];
                    wet.RemoveWaterDrop(drop);
                }
                wiped = true;
            }

            if (wiped)
            {
                _ragCooldownTicks = cdTicks;
                PlayToolOneShot(wipeToolId);
            }

            return wiped;
        }

        private bool MaintainStuporDevicesTick(IReadOnlyList<ResolvedDeviceSlot> slots, out int stuporSlotIndex)
        {
            stuporSlotIndex = -1;
            if (_stuporCooldownTicks > 0)
            {
                return false;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d != null && d.device_emergency is DeviceStupor stupor && !stupor.removed)
                {
                    stupor.Fix();
                    _stuporCooldownTicks = 15;
                    stuporSlotIndex = slots[i].Index;
                    PlayToolOneShot(GetWrenchToolId());
                    return true;
                }
            }
            return false;
        }

        private bool TryUseCaravanRepairKitOnSlot(ResolvedDeviceSlot slot)
        {
            Widget_Fix_Context fixCtx = Singleton<Widget_Fix_Context>.instance;
            if (fixCtx == null || fixCtx.current_fix_amount <= 0f || slot.UiView == null || slot.Device == null)
            {
                return false;
            }

            if (slot.Device.current_hp >= slot.Device.max_hp)
            {
                return false;
            }

            fixCtx.Fix(slot.UiView);
            return true;
        }

        private bool TryUseCaravanRepairKitOnDamaged(IReadOnlyList<ResolvedDeviceSlot> slots, out string message)
        {
            message = string.Empty;
            Widget_Fix_Context fixCtx = Singleton<Widget_Fix_Context>.instance;
            if (fixCtx == null || fixCtx.current_fix_amount <= 0f)
            {
                return false;
            }

            int bestIdx = -1;
            float lowestRatio = 1f;
            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d != null && slots[i].UiView != null && d.max_hp > 0 && d.current_hp < d.max_hp)
                {
                    float ratio = (float)d.current_hp / d.max_hp;
                    if (ratio < lowestRatio)
                    {
                        lowestRatio = ratio;
                        bestIdx = i;
                    }
                }
            }

            if (bestIdx >= 0)
            {
                ResolvedDeviceSlot target = slots[bestIdx];
                fixCtx.Fix(target.UiView);
                MarkSlotFeedback(target.Index);
                string devName = GetDeviceDisplayName(target.Device);
                message = $"已恢复 {target.Index + 1}号位【{devName}】耐久 ({target.Device.current_hp}/{target.Device.max_hp})";
                return true;
            }

            WorldContext wctx = Singleton<WorldContext>.instance;
            if (wctx != null && wctx.caravan_hp < wctx.caravan_hp_max)
            {
                CaravanFixArea caravanArea = UnityEngine.Object.FindObjectOfType<CaravanFixArea>();
                if (caravanArea != null)
                {
                    fixCtx.Fix(caravanArea);
                    message = $"已恢复车体耐久 ({wctx.caravan_hp}/{wctx.caravan_hp_max})";
                    return true;
                }
            }

            return false;
        }

        private void RepairDeviceTick(ResolvedDeviceSlot slot)
        {
            Device device = slot.Device;
            if (device == null || device.is_validate)
            {
                return;
            }

            Config cfg = Config.current;
            float velocity = cfg != null ? cfg.wrench_velocity : 10f;
            float effInit = cfg != null ? cfg.wrench_efficiency_init : 1.25f;
            float ctxBonus = Singleton<BattleContext>.instance != null
                ? (1f + Singleton<BattleContext>.instance.wrench_efficiency_init / 1000f)
                : 1f;

            int wrenchId = GetWrenchToolId();
            int wrenchLevel = GetToolLevel(wrenchId);
            ToolLogic wrenchLogic = GetActiveToolLogic(wrenchId);

            // Stage-aware wrench multiplier:
            // Lv0: single-direction manual wrench
            // Lv1+: double_direction / auto_rotate / click_to_auto_fix
            float levelMult = 1f + Mathf.Max(0, wrenchLevel) * 0.25f;
            if (wrenchLogic is FixWrench fw)
            {
                if (fw.double_direction) levelMult *= 1.15f;
                if (fw.auto_rotate || fw.click_to_auto_fix) levelMult *= 1.15f;
            }

            float fixDelta = velocity * effInit * ctxBonus * levelMult;

            DeviceFixMask fixMask = null;
            if (slot.UiView != null)
            {
                if (slot.UiView.mask != null)
                {
                    fixMask = slot.UiView.mask.GetComponentInChildren<DeviceFixMask>(true);
                }
                if (fixMask == null)
                {
                    fixMask = slot.UiView.GetComponentInChildren<DeviceFixMask>(true);
                }
            }

            PlayToolOneShot(wrenchId);

            if (fixMask != null)
            {
                bool finished = fixMask.Fix(fixDelta);
                if (finished)
                {
                    MarkSlotFeedback(slot.Index);
                    SetStatus($"已完成 {slot.Index + 1}号位【{GetDeviceDisplayName(device)}】修缮！");
                }
            }
            else
            {
                _fallbackFixProgress.TryGetValue(device, out float cur);
                cur += fixDelta;
                if (cur >= 1500f)
                {
                    _fallbackFixProgress.Remove(device);
                    device.FixDisable();
                    MarkSlotFeedback(slot.Index);
                    SetStatus($"已完成 {slot.Index + 1}号位【{GetDeviceDisplayName(device)}】修缮！");
                }
                else
                {
                    _fallbackFixProgress[device] = cur;
                }
            }
        }

        private void ExtinguishDeviceTick(int slotIndex, DeviceFired fired)
        {
            if (fired == null || fired.removed)
            {
                return;
            }

            float efficiency = GetExtinguisherEfficiency();
            bool extinguished = fired.ChangeFire(-efficiency * 0.008333334f);
            PlayToolOneShot(GetExtinguisherToolId());
            if (extinguished)
            {
                MarkSlotFeedback(slotIndex);
                SetStatus($"已彻底扑灭 {slotIndex + 1}号槽位设备火焰！");
            }
        }

        /// <summary>
        /// Stage-aware Saw / Scissors / ChainSaw / CircleSaw vine cutting:
        /// - Saw (Lv0) / Scissors (Lv1) / ChainSaw (Lv2): Strictly cuts the top-most vine (upper_vines.Count == 0)
        ///   so the device never takes wrong-order vine_damage, and deals proportional real damage to the Adherent.
        /// - CircleSaw (Lv3): Direct high-speed removal on the device while still preferring top-most vines for zero self-damage.
        /// </summary>
        private bool CutVineOnDeviceByStage(ResolvedDeviceSlot slot, DeviceBind bind, bool isKeyDown)
        {
            if (bind == null || bind.removed)
            {
                return false;
            }

            Config cfg = Config.current;
            int sawToolId = GetSawToolId();
            int sawLevel = GetToolLevel(sawToolId);
            ToolLogic sawLogic = GetActiveToolLogic(sawToolId);

            float baseInterval = cfg != null ? cfg.saw_work_interval : 0.25f;
            float levelFactor;
            if (sawLogic is CircleSaw || sawLevel >= 3)
            {
                levelFactor = 0.55f;
            }
            else if (sawLogic is ChainSaw || sawLevel == 2)
            {
                levelFactor = 0.75f;
            }
            else if (sawLogic is Scissors || sawLevel == 1)
            {
                levelFactor = isKeyDown ? 0.8f : 1.05f;
            }
            else
            {
                // Hand Saw (Lv0): requires back-and-forth sawing time
                levelFactor = isKeyDown ? 1.0f : 1.25f;
            }

            int cdTicks = Mathf.Max(5, Mathf.RoundToInt(baseInterval * 120f * levelFactor));

            // 1. Prefer SingleBindView.RemoveSelf() on top-most vine (upper_vines.Count == 0)
            if (slot.UiView != null)
            {
                DeviceBindView bindView = slot.UiView.GetComponentInChildren<DeviceBindView>(true);
                if (bindView != null && bindView.vine_list != null)
                {
                    for (int i = bindView.vine_list.Count - 1; i >= 0; i--)
                    {
                        SingleBindView sbv = bindView.vine_list[i];
                        if (sbv != null && sbv.data != null && (sbv.data.upper_vines == null || sbv.data.upper_vines.Count == 0))
                        {
                            sbv.RemoveSelf();
                            _sawCooldownTicks = cdTicks;
                            PlayToolOneShot(sawToolId);
                            return true;
                        }
                    }
                }
            }

            // 2. Direct model fallback via adherents_vines_dic + BattleUtility.do_real_hurt + bind.RemoveVine
            if (bind.adherents_vines_dic != null)
            {
                foreach (KeyValuePair<Adherent, List<vine>> kvp in bind.adherents_vines_dic)
                {
                    Adherent ad = kvp.Key;
                    List<vine> vines = kvp.Value;
                    if (ad == null || vines == null || vines.Count == 0)
                    {
                        continue;
                    }

                    for (int i = vines.Count - 1; i >= 0; i--)
                    {
                        vine v = vines[i];
                        if (v != null && (v.upper_vines == null || v.upper_vines.Count == 0))
                        {
                            int ropeNum = ad.desc != null && ad.desc.DP_rope_num > 0 ? ad.desc.DP_rope_num : 1;
                            int maxHp = ad.desc != null ? ad.desc.hp_max : 10;
                            int dmg = Mathf.CeilToInt((float)(maxHp / ropeNum));
                            BattleUtility.do_real_hurt(ad, dmg);
                            bind.RemoveVine(ad, v);
                            _sawCooldownTicks = cdTicks;
                            PlayToolOneShot(sawToolId);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool CanHoldSawContinuously()
        {
            int sawId = GetSawToolId();
            int level = GetToolLevel(sawId);
            ToolLogic logic = GetActiveToolLogic(sawId);
            return level >= 2 || logic is ChainSaw || logic is CircleSaw || logic is Saw;
        }

        private void TryWakeUpCharacterByIndex(int charIndex, bool isKeyDown)
        {
            Mission mission = Singleton<Mission>.instance;
            if (mission == null || !mission.try_get_mgr("CharacterMgr", out CharacterMgr charMgr) || charMgr?.characters == null)
            {
                return;
            }

            if (charIndex < 0 || charIndex >= charMgr.characters.Count)
            {
                return;
            }

            Character c = charMgr.characters[charIndex];
            string wakeStage = GetWakeStageDescription();
            if (c != null && c.state is Coma)
            {
                ApplyWakeUpTrigger(c, isKeyDown);
                SetStatus($"[B+{charIndex + 1}:{wakeStage}] 正在唤醒 {charIndex + 1} 号昏迷角色...");
            }
            else if (isKeyDown)
            {
                SetStatus($"[B+{charIndex + 1}:{wakeStage}] {charIndex + 1} 号角色当前清醒，无需唤醒");
            }
        }

        private bool TryWakeUpAllComaCharacters(bool isKeyDown, out int firstWokeIndex)
        {
            firstWokeIndex = -1;
            Mission mission = Singleton<Mission>.instance;
            if (mission == null || !mission.try_get_mgr("CharacterMgr", out CharacterMgr charMgr) || charMgr?.characters == null)
            {
                return false;
            }

            int comaToolId = GetComaToolId();
            int wakeToolLevel = GetToolLevel(comaToolId);
            ToolLogic wakeLogic = GetActiveToolLogic(comaToolId);

            bool isClickWakeTool = (wakeLogic is InflatingHammer || wakeLogic is ScreamingChicken) || (wakeLogic == null && wakeToolLevel <= 0);
            bool isGlobalAed = (wakeLogic is AED) || wakeToolLevel >= 3;

            // For click tools (InflatingHammer / ScreamingChicken), trigger immediately on keydown or every 18 ticks while held
            if (isClickWakeTool && !isKeyDown && _wakeUpTapCooldownTicks > 0)
            {
                return false;
            }

            bool wokeAny = false;
            for (int i = 0; i < charMgr.characters.Count; i++)
            {
                Character c = charMgr.characters[i];
                if (c != null && c.state is Coma)
                {
                    if (firstWokeIndex < 0)
                    {
                        firstWokeIndex = i;
                    }
                    ApplyWakeUpTrigger(c, isKeyDown || isClickWakeTool);
                    wokeAny = true;

                    // Only AED (stage 3) wakes ALL comatose characters simultaneously; earlier stages wake one character at a time
                    if (!isGlobalAed)
                    {
                        break;
                    }
                }
            }

            if (wokeAny && isClickWakeTool)
            {
                _wakeUpTapCooldownTicks = 18;
            }
            return wokeAny;
        }

        private void ApplyWakeUpTrigger(Character c, bool useTapEffect)
        {
            if (c?.state == null)
            {
                return;
            }

            Config cfg = Config.current;
            int comaToolId = GetComaToolId();
            ToolLogic wakeLogic = GetActiveToolLogic(comaToolId);

            int effect;
            if (wakeLogic is InflatingHammer || (wakeLogic == null && useTapEffect && GetToolLevel(comaToolId) <= 0))
            {
                effect = cfg != null ? cfg.hammer_lv_0_effect : 30;
            }
            else
            {
                effect = cfg != null ? cfg.hammer_lv_1_effect : 2;
            }

            c.state.trigger(effect);
            if (useTapEffect)
            {
                PlayToolOneShot(comaToolId);
            }
        }

        public string GetRagStageDescription()
        {
            int toolId = GetWipeToolId();
            string localized = GetToolDisplayName(toolId, string.Empty);
            ToolLogic logic = GetActiveToolLogic(toolId);
            int level = GetToolLevel(toolId);

            if (logic is Drier || level >= 3)
            {
                return !string.IsNullOrEmpty(localized) ? $"{localized}(全车吹干)" : "吹风筒(全车吹干)";
            }
            if (logic is Mop || level == 2)
            {
                return !string.IsNullOrEmpty(localized) ? $"{localized}(按住拖擦)" : "拖把(按住拖擦)";
            }
            if (logic is BigRag || level == 1)
            {
                return !string.IsNullOrEmpty(localized) ? $"{localized}(按住擦拭)" : "大抹布(按住擦拭)";
            }
            return !string.IsNullOrEmpty(localized) ? $"{localized}(单点擦拭)" : "抹布(单点擦拭)";
        }

        public string GetSawStageDescription()
        {
            int toolId = GetSawToolId();
            string localized = GetToolDisplayName(toolId, string.Empty);
            ToolLogic logic = GetActiveToolLogic(toolId);
            int level = GetToolLevel(toolId);

            if (logic is CircleSaw || level >= 3)
            {
                return !string.IsNullOrEmpty(localized) ? $"{localized}(极速圆锯)" : "圆锯";
            }
            if (logic is ChainSaw || level == 2)
            {
                return !string.IsNullOrEmpty(localized) ? $"{localized}(电锯连切)" : "电锯";
            }
            if (logic is Scissors || level == 1)
            {
                return !string.IsNullOrEmpty(localized) ? $"{localized}(剪刀快剪)" : "剪刀";
            }
            return !string.IsNullOrEmpty(localized) ? localized : "手锯";
        }

        public string GetWakeStageDescription()
        {
            int toolId = GetComaToolId();
            string localized = GetToolDisplayName(toolId, string.Empty);
            ToolLogic logic = GetActiveToolLogic(toolId);
            int level = GetToolLevel(toolId);

            if (logic is AED || level >= 3)
            {
                return !string.IsNullOrEmpty(localized) ? $"{localized}(全车除颤)" : "除颤仪(全车)";
            }
            if (logic is Tweeter)
            {
                return !string.IsNullOrEmpty(localized) ? localized : "扩音喇叭";
            }
            if (logic is Clock)
            {
                return !string.IsNullOrEmpty(localized) ? localized : "闹钟";
            }
            if (logic is ScreamingChicken)
            {
                return !string.IsNullOrEmpty(localized) ? localized : "惨叫鸡";
            }
            return !string.IsNullOrEmpty(localized) ? localized : "充气锤";
        }

        private bool IsGlobalDrierStage()
        {
            int toolId = GetWipeToolId();
            ToolLogic logic = GetActiveToolLogic(toolId);
            return (logic is Drier) || GetToolLevel(toolId) >= 3;
        }

        private bool IsGlobalAedStage()
        {
            int toolId = GetComaToolId();
            ToolLogic logic = GetActiveToolLogic(toolId);
            return (logic is AED) || GetToolLevel(toolId) >= 3;
        }

        private static int GetWrenchToolId() => Config.current != null ? Config.current.highlight_fix_id : 1;
        private static int GetExtinguisherToolId() => Config.current != null ? Config.current.highlight_extinguish_id : 2;
        private static int GetSawToolId() => Config.current != null ? Config.current.highlight_bind_id : 3;
        private static int GetWipeToolId() => Config.current != null ? Config.current.highlight_wipe_id : 4;
        private static int GetComaToolId() => Config.current != null ? Config.current.highlight_coma_id : 5;

        private int GetToolLevel(int toolId)
        {
            Mission mission = Singleton<Mission>.instance;
            if (mission != null && mission.try_get_mgr("PlayerToolMgr", out PlayerToolMgr toolMgr) && toolMgr?.tools != null)
            {
                if (toolMgr.tools.TryGetValue((uint)toolId, out int level))
                {
                    return level;
                }
            }
            return 0;
        }

        private ToolLogic GetActiveToolLogic(int toolId)
        {
            PlayerToolMgrView view = GetToolMgrView();
            if (view != null && view.tool_view_list != null)
            {
                for (int i = 0; i < view.tool_view_list.Count; i++)
                {
                    ToolView tv = view.tool_view_list[i];
                    if (tv != null && tv.gameObject.activeInHierarchy && tv.tool_data != null && tv.tool_data.id == (uint)toolId)
                    {
                        return tv.logic;
                    }
                }
            }
            return null;
        }

        private string GetToolDisplayName(int toolId, string fallback)
        {
            PlayerToolMgrView view = GetToolMgrView();
            if (view != null && view.tool_view_list != null)
            {
                for (int i = 0; i < view.tool_view_list.Count; i++)
                {
                    ToolView tv = view.tool_view_list[i];
                    if (tv != null && tv.tool_data != null && tv.tool_data.id == (uint)toolId)
                    {
                        try
                        {
                            string loc = Localization_Utility.get_localization(tv.tool_data.tool_name);
                            if (!string.IsNullOrEmpty(loc))
                            {
                                return loc;
                            }
                        }
                        catch
                        {
                            // Fallback below
                        }
                    }
                }
            }

            int level = GetToolLevel(toolId);
            if (player_tools.TryGetValue($"{toolId},{level}", out player_tool pt) && pt != null)
            {
                try
                {
                    string loc = Localization_Utility.get_localization(pt.tool_name);
                    if (!string.IsNullOrEmpty(loc))
                    {
                        return loc;
                    }
                }
                catch
                {
                    // Fallback below
                }
            }

            return fallback;
        }

        private static string GetSlotDeviceName(IReadOnlyList<ResolvedDeviceSlot> slots, int slotIndex)
        {
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Index == slotIndex && slots[i].Device != null)
                    {
                        return GetDeviceDisplayName(slots[i].Device);
                    }
                }
            }
            return $"{slotIndex + 1}号设备";
        }

        public static string GetDeviceDisplayName(Device device)
        {
            if (device?.desc != null && !string.IsNullOrEmpty(device.desc.name))
            {
                try
                {
                    string loc = Localization_Utility.get_localization(device.desc.name);
                    if (!string.IsNullOrEmpty(loc))
                    {
                        return loc;
                    }
                }
                catch
                {
                    // Ignore localization errors
                }
            }
            return device != null ? device.GetType().Name.Replace("DScript_", "") : "空槽位";
        }

        private float GetExtinguisherEfficiency()
        {
            PlayerToolMgrView view = GetToolMgrView();
            if (view != null && view.tool_view_list != null)
            {
                foreach (ToolView tv in view.tool_view_list)
                {
                    if (tv != null && tv.logic is Extinguisher ext && ext.extinguish_efficiency > 0f)
                    {
                        return ext.extinguish_efficiency;
                    }
                }
            }
            return 35f;
        }

        private void PlayToolOneShot(int toolId)
        {
            if (_sfxCooldownTicks > 0)
            {
                return;
            }

            PlayerToolMgrView view = GetToolMgrView();
            if (view != null && view.tool_view_list != null)
            {
                foreach (ToolView tv in view.tool_view_list)
                {
                    if (tv != null && tv.tool_data != null && tv.tool_data.id == (uint)toolId && !string.IsNullOrEmpty(tv.tool_data.se_tool))
                    {
                        AudioSystem audio = MonoBehaviourSingleton<AudioSystem>.instance;
                        if (audio != null)
                        {
                            audio.PlayOneShot(SFXType.Battle, tv.tool_data.se_tool);
                            _sfxCooldownTicks = 15;
                        }
                        break;
                    }
                }
            }
        }

        private PlayerToolMgrView GetToolMgrView()
        {
            if (_cachedToolMgrView == null && Time.unscaledTime - _lastToolViewSearchTime > 1.0f)
            {
                _lastToolViewSearchTime = Time.unscaledTime;
                _cachedToolMgrView = UnityEngine.Object.FindObjectOfType<PlayerToolMgrView>();
            }
            return _cachedToolMgrView;
        }

        private void MarkSlotFeedback(int slotIndex)
        {
            if (slotIndex >= 0)
            {
                LastTargetSlotIndex = slotIndex;
                LastTargetSlotTime = Time.unscaledTime;
            }
        }

        private void SetStatus(string msg)
        {
            LastActionStatus = msg;
            LastActionStatusTime = Time.unscaledTime;
        }
    }
}
