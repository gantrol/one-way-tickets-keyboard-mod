using System;
using System.Collections.Generic;
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
using World.Player_Tool;
using World.Player_Tool.ToolsLogic;
using World.Widgets;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Handles Row 3 (A S D F G H) and Row 4 (Z X C V B + Space) Toolbox controls:
    /// - Row 3 (A..H): Direct per-slot maintenance aligned under Q..Y
    ///   (Repairs broken device, extinguishes fire, cuts top-most vine without HP penalty, wipes water/acid, un-jams stupor, or heals damaged HP via repair kit)
    /// - Row 4 (Z X C V B): Specific tool category maintenance across the caravan
    ///   (Z=Wrench/Repair, X=Extinguisher, C=Saw/Scissors, V=Rag/Mop, B=WakeUp comatose characters, B+1..6=Targeted WakeUp)
    /// - Space: Global Smart Maintenance (automatically handles all active emergencies, comatose characters, and broken/damaged devices)
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

            // Immediate execution on key-down so single taps (not just holds) always work and show clear feedback
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
                    MaintainSingleSlotTick(slots[i]);
                }
            }

            // 2. Specific tool maintenance (Z X C V B)
            if (_holdingWrench)
            {
                if (!MaintainBrokenDevicesTick(slots))
                {
                    if (!MaintainStuporDevicesTick(slots))
                    {
                        TryUseCaravanRepairKitOnDamaged(slots, out _);
                    }
                }
            }
            if (_holdingExtinguisher)
            {
                MaintainFiresTick(slots);
            }
            if (_holdingSaw)
            {
                MaintainVinesTick(slots);
            }
            if (_holdingRag)
            {
                MaintainWetAndAcidTick(slots);
            }
            if (_holdingWakeUp)
            {
                TryWakeUpAllComaCharacters(false);
            }

            // 3. Global Smart Maintenance (Space)
            if (_holdingGlobalSmart)
            {
                TryWakeUpAllComaCharacters(false);
                MaintainFiresTick(slots);
                MaintainVinesTick(slots);
                MaintainWetAndAcidTick(slots);
                if (!MaintainBrokenDevicesTick(slots))
                {
                    TryUseCaravanRepairKitOnDamaged(slots, out _);
                }
                MaintainStuporDevicesTick(slots);
            }
        }

        private void OnSlotMaintainPressed(int slotIndex)
        {
            if (!_slotResolver.TryGetSlot(slotIndex, out ResolvedDeviceSlot slot) || slot.Device == null)
            {
                SetStatus($"[{ModConfig.FormatKeyLabel(ModConfig.Instance.SlotMaintainKeys[slotIndex])}] {slotIndex + 1} 号槽位无设备");
                return;
            }

            Device device = slot.Device;
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.SlotMaintainKeys[slotIndex]);

            // Immediately execute 1 maintenance step on press
            _sawCooldownTicks = 0;
            _ragCooldownTicks = 0;
            _stuporCooldownTicks = 0;
            MaintainSingleSlotTick(slot);

            if (!device.is_validate)
            {
                SetStatus($"[{keyLabel}] 正在用扳手修缮 {slotIndex + 1} 号位破损设备 (请按住 {keyLabel})...");
                return;
            }

            DeviceEmergency em = device.device_emergency;
            if (em != null && !em.removed)
            {
                if (em is DeviceStupor)
                {
                    SetStatus($"[{keyLabel}] 已拍醒 {slotIndex + 1} 号位停滞设备！");
                }
                else if (em is DeviceFired)
                {
                    SetStatus($"[{keyLabel}] 正在扑灭 {slotIndex + 1} 号位火焰 (请按住 {keyLabel})...");
                }
                else if (em is DeviceBind)
                {
                    SetStatus($"[{keyLabel}] 正在安全剪除 {slotIndex + 1} 号位表层藤蔓...");
                }
                else if (em is DeviceWet || em is DeviceAcid)
                {
                    SetStatus($"[{keyLabel}] 正在擦除 {slotIndex + 1} 号位水滴/酸液...");
                }
                return;
            }

            // If device is not broken and has no emergency, check if it has lost HP and can use the caravan repair kit
            if (device.current_hp < device.max_hp)
            {
                if (TryUseCaravanRepairKitOnSlot(slot))
                {
                    SetStatus($"[{keyLabel}] 已使用维修包恢复 {slotIndex + 1} 号位设备耐久 ({device.current_hp}/{device.max_hp})！");
                }
                else
                {
                    SetStatus($"[{keyLabel}] {slotIndex + 1} 号位设备未彻底破损（扳手仅修 0 血损毁设备，维修包当前冷却中）");
                }
                return;
            }

            SetStatus($"[{keyLabel}] {slotIndex + 1} 号位设备状态完好 ({device.current_hp}/{device.max_hp})，无需抢修");
        }

        private void OnWrenchKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolWrenchKey);
            if (MaintainBrokenDevicesTick(slots))
            {
                SetStatus($"[{keyLabel}:扳手] 正在修缮损毁设备 (请按住 {keyLabel})...");
                return;
            }
            if (MaintainStuporDevicesTick(slots))
            {
                SetStatus($"[{keyLabel}:扳手] 已拍醒停滞设备！");
                return;
            }
            if (TryUseCaravanRepairKitOnDamaged(slots, out string repairMsg))
            {
                SetStatus($"[{keyLabel}:维修] {repairMsg}");
                return;
            }
            SetStatus($"[{keyLabel}:扳手] 当前无损毁设备需要扳手修缮");
        }

        private void OnExtinguisherKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolExtinguisherKey);
            if (MaintainFiresTick(slots))
            {
                SetStatus($"[{keyLabel}:灭火器] 正在扑灭全车设备火焰 (请按住 {keyLabel})...");
            }
            else
            {
                SetStatus($"[{keyLabel}:灭火器] 当前没有着火的设备");
            }
        }

        private void OnSawKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolSawKey);
            if (MaintainVinesTick(slots))
            {
                SetStatus($"[{keyLabel}:锯子/剪刀] 已安全剪除表层藤蔓！");
            }
            else
            {
                SetStatus($"[{keyLabel}:锯子/剪刀] 当前没有被藤蔓缠绕的设备");
            }
        }

        private void OnRagKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolRagKey);
            if (MaintainWetAndAcidTick(slots))
            {
                SetStatus($"[{keyLabel}:抹布] 已清理设备上的水滴/酸液！");
            }
            else
            {
                SetStatus($"[{keyLabel}:抹布] 当前没有进水或被酸液腐蚀的设备");
            }
        }

        private void OnWakeUpKeyPressed()
        {
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.ToolWakeUpKey);
            if (TryWakeUpAllComaCharacters(true))
            {
                SetStatus($"[{keyLabel}:唤醒] 正在唤醒昏迷角色！");
            }
            else
            {
                SetStatus($"[{keyLabel}:唤醒] 当前没有处于昏迷状态的角色");
            }
        }

        private void OnGlobalSmartKeyPressed(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            bool anyAction = false;
            if (TryWakeUpAllComaCharacters(true)) anyAction = true;
            if (MaintainFiresTick(slots)) anyAction = true;
            if (MaintainVinesTick(slots)) anyAction = true;
            if (MaintainWetAndAcidTick(slots)) anyAction = true;
            if (MaintainBrokenDevicesTick(slots)) anyAction = true;
            if (MaintainStuporDevicesTick(slots)) anyAction = true;
            if (!anyAction && TryUseCaravanRepairKitOnDamaged(slots, out _)) anyAction = true;

            if (anyAction)
            {
                SetStatus("[Space:全局抢修] 正在自动处理全车异常与修缮...");
            }
            else
            {
                SetStatus("[Space:全局抢修] 全车设备与角色状态良好，暂无异常");
            }
        }

        private void MaintainSingleSlotTick(ResolvedDeviceSlot slot)
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
                    ExtinguishDeviceTick(fired);
                    return;
                }
                if (em is DeviceBind bind)
                {
                    if (_sawCooldownTicks <= 0)
                    {
                        CutTopVineOnDevice(slot, bind);
                    }
                    return;
                }
                if (em is DeviceWet wet)
                {
                    if (_ragCooldownTicks <= 0)
                    {
                        WipeWaterOnDevice(wet);
                    }
                    return;
                }
                if (em is DeviceAcid acid)
                {
                    if (_ragCooldownTicks <= 0)
                    {
                        WipeAcidOnDevice(acid);
                    }
                    return;
                }
                if (em is DeviceStupor stupor)
                {
                    if (_stuporCooldownTicks <= 0)
                    {
                        stupor.Fix();
                        _stuporCooldownTicks = 15;
                    }
                    return;
                }
            }

            // Priority 3: If device has lost HP and caravan repair kit is available, use it
            if (device.current_hp < device.max_hp)
            {
                TryUseCaravanRepairKitOnSlot(slot);
            }
        }

        private bool MaintainBrokenDevicesTick(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Device != null && !slots[i].Device.is_validate)
                {
                    RepairDeviceTick(slots[i]);
                    // Wrench repairs one broken device at a time (matching manual wrench behavior)
                    return true;
                }
            }
            return false;
        }

        private bool MaintainFiresTick(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            bool any = false;
            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d != null && d.device_emergency is DeviceFired fired && !fired.removed)
                {
                    ExtinguishDeviceTick(fired);
                    any = true;
                }
            }
            return any;
        }

        private bool MaintainVinesTick(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            if (_sawCooldownTicks > 0)
            {
                return false;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d != null && d.device_emergency is DeviceBind bind && !bind.removed && bind.vine_list != null && bind.vine_list.Count > 0)
                {
                    if (CutTopVineOnDevice(slots[i], bind))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool MaintainWetAndAcidTick(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
            if (_ragCooldownTicks > 0)
            {
                return false;
            }

            int ragLevel = GetToolLevel(Config.current != null ? Config.current.highlight_wipe_id : 4);
            bool isGlobalDrier = ragLevel >= 3; // Drier wipes all devices simultaneously
            bool any = false;

            for (int i = 0; i < slots.Count; i++)
            {
                Device d = slots[i].Device;
                if (d == null || d.device_emergency == null || d.device_emergency.removed)
                {
                    continue;
                }

                if (d.device_emergency is DeviceAcid acid && acid.acids != null && acid.acids.Count > 0)
                {
                    WipeAcidOnDevice(acid);
                    any = true;
                    if (!isGlobalDrier) break;
                }
                else if (d.device_emergency is DeviceWet wet && wet.waterdrops != null && wet.waterdrops.Count > 0)
                {
                    WipeWaterOnDevice(wet);
                    any = true;
                    if (!isGlobalDrier) break;
                }
            }
            return any;
        }

        private bool MaintainStuporDevicesTick(IReadOnlyList<ResolvedDeviceSlot> slots)
        {
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

            // Find the device with the lowest HP ratio
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
                message = $"已使用车载维修包恢复 {bestIdx + 1} 号位设备耐久 ({target.Device.current_hp}/{target.Device.max_hp})";
                return true;
            }

            // Or heal caravan HP if damaged
            WorldContext wctx = Singleton<WorldContext>.instance;
            if (wctx != null && wctx.caravan_hp < wctx.caravan_hp_max)
            {
                CaravanFixArea caravanArea = UnityEngine.Object.FindObjectOfType<CaravanFixArea>();
                if (caravanArea != null)
                {
                    fixCtx.Fix(caravanArea);
                    message = $"已使用车载维修包恢复车体耐久 ({wctx.caravan_hp}/{wctx.caravan_hp_max})";
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

            int wrenchLevel = GetToolLevel(cfg != null ? cfg.highlight_fix_id : 1);
            float levelMult = 1f + Mathf.Max(0, wrenchLevel) * 0.25f;

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

            if (fixMask != null)
            {
                bool finished = fixMask.Fix(fixDelta);
                if (finished)
                {
                    SetStatus($"已完成 {slot.Index + 1} 号位设备修缮！");
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
                    SetStatus($"已完成 {slot.Index + 1} 号位设备修缮！");
                }
                else
                {
                    _fallbackFixProgress[device] = cur;
                }
            }
        }

        private void ExtinguishDeviceTick(DeviceFired fired)
        {
            if (fired == null || fired.removed)
            {
                return;
            }

            float efficiency = GetExtinguisherEfficiency();
            bool extinguished = fired.ChangeFire(-efficiency * 0.008333334f);
            if (extinguished)
            {
                SetStatus("已彻底扑灭设备火焰！");
            }
        }

        private bool CutTopVineOnDevice(ResolvedDeviceSlot slot, DeviceBind bind)
        {
            if (bind == null || bind.removed)
            {
                return false;
            }

            Config cfg = Config.current;
            int sawLevel = GetToolLevel(cfg != null ? cfg.highlight_bind_id : 3);
            float baseInterval = cfg != null ? cfg.saw_work_interval : 0.25f;
            float levelFactor = sawLevel >= 2 ? 0.65f : (sawLevel == 1 ? 0.85f : 1.0f);
            int cdTicks = Mathf.Max(5, Mathf.RoundToInt(baseInterval * 120f * levelFactor));

            // 1. Prefer SingleBindView.RemoveSelf() if UI view is active
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
                            PlayToolOneShot(cfg != null ? cfg.highlight_bind_id : 3);
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
                            PlayToolOneShot(cfg != null ? cfg.highlight_bind_id : 3);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void WipeWaterOnDevice(DeviceWet wet)
        {
            if (wet == null || wet.removed || wet.waterdrops == null || wet.waterdrops.Count == 0)
            {
                return;
            }

            Config cfg = Config.current;
            int ragLevel = GetToolLevel(cfg != null ? cfg.highlight_wipe_id : 4);
            float baseInterval = cfg != null ? cfg.rag_work_interval : 0.2f;
            float levelFactor = ragLevel >= 2 ? 0.65f : (ragLevel == 1 ? 0.85f : 1.0f);

            SingleWaterdrop drop = wet.waterdrops[wet.waterdrops.Count - 1];
            wet.RemoveWaterDrop(drop);
            _ragCooldownTicks = Mathf.Max(4, Mathf.RoundToInt(baseInterval * 120f * levelFactor));
        }

        private void WipeAcidOnDevice(DeviceAcid acid)
        {
            if (acid == null || acid.removed || acid.acids == null || acid.acids.Count == 0)
            {
                return;
            }

            Config cfg = Config.current;
            int ragLevel = GetToolLevel(cfg != null ? cfg.highlight_wipe_id : 4);
            float baseInterval = cfg != null ? cfg.rag_work_interval : 0.2f;
            float levelFactor = ragLevel >= 2 ? 0.65f : (ragLevel == 1 ? 0.85f : 1.0f);

            SingleAcid singleAcid = acid.acids[acid.acids.Count - 1];
            acid.RemoveAcid(singleAcid);
            _ragCooldownTicks = Mathf.Max(4, Mathf.RoundToInt(baseInterval * 120f * levelFactor));
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
            if (c != null && c.state is Coma)
            {
                ApplyWakeUpTrigger(c, isKeyDown);
                SetStatus($"正在唤醒 {charIndex + 1} 号昏迷角色...");
            }
        }

        private bool TryWakeUpAllComaCharacters(bool isKeyDown)
        {
            Mission mission = Singleton<Mission>.instance;
            if (mission == null || !mission.try_get_mgr("CharacterMgr", out CharacterMgr charMgr) || charMgr?.characters == null)
            {
                return false;
            }

            Config cfg = Config.current;
            int wakeToolLevel = GetToolLevel(cfg != null ? cfg.highlight_coma_id : 5);

            // For level 0 click tools (Hammer/ScreamingChicken), trigger on keydown or every 18 ticks while held
            if (wakeToolLevel <= 0 && !isKeyDown && _wakeUpTapCooldownTicks > 0)
            {
                return false;
            }

            bool wokeAny = false;
            for (int i = 0; i < charMgr.characters.Count; i++)
            {
                Character c = charMgr.characters[i];
                if (c != null && c.state is Coma)
                {
                    ApplyWakeUpTrigger(c, isKeyDown || wakeToolLevel <= 0);
                    wokeAny = true;
                    // Only AED (level >= 3) wakes all characters simultaneously in 1 tick; others wake the first comatose character
                    if (wakeToolLevel < 3)
                    {
                        break;
                    }
                }
            }

            if (wokeAny && wakeToolLevel <= 0)
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
            int effect = useTapEffect
                ? (cfg != null ? cfg.hammer_lv_0_effect : 30)
                : (cfg != null ? cfg.hammer_lv_1_effect : 2);

            c.state.trigger(effect);
            if (useTapEffect)
            {
                PlayToolOneShot(cfg != null ? cfg.highlight_coma_id : 5);
            }
        }

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

        private void SetStatus(string msg)
        {
            LastActionStatus = msg;
            LastActionStatusTime = Time.unscaledTime;
        }
    }
}
