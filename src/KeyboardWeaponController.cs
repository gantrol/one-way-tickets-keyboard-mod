using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using World.Devices;
using World.Devices.Device_AI;
using World.Devices.DeviceUiViews;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Handles Row 2 keyboard controls (Q W E R T + Tab for the 5 fixed weapon slots):
    /// - Hold Q..T or Ctrl+Q..T / Tab Toggle: Smart Loop + Action 1
    ///   (auto-aim rotation, primary attack, auto-reload when empty, charge/carry/accelerate)
    /// - Shift + Q..T: Independent Action 2
    ///   (manual early reload, Throwing Spear ranged throw, Claymore sharpen, Charge melee strike, Quiver switch, Drum right stick)
    /// - Alt + Q..T: Independent Action 3
    ///   (Gatlin carry ammo, Cannon/Windlass/Helicopter/Ram crank rotation, Revolver unload cylinder)
    /// </summary>
    public sealed class KeyboardWeaponController
    {
        private readonly DeviceSlotResolver _slotResolver;
        private readonly KeyboardCharacterController _charController;

        private readonly bool[] _autoFireToggles = new bool[DeviceSlotResolver.MaxWeaponSlots];
        private readonly bool[] _holdingPrimary = new bool[DeviceSlotResolver.MaxWeaponSlots];
        private readonly bool[] _holdingAction2 = new bool[DeviceSlotResolver.MaxWeaponSlots];
        private readonly bool[] _holdingAction3 = new bool[DeviceSlotResolver.MaxWeaponSlots];

        private readonly MethodInfo _rotateBoneMethod;
        private readonly PropertyInfo _playerInteractingProp;
        private readonly MethodInfo _newBasicMeleeAttackMethod;
        private readonly MethodInfo _catchingFlowerAttackMethod;

        public string LastActionStatus { get; private set; } = string.Empty;
        public float LastActionStatusTime { get; private set; }
        public int LastTargetSlotIndex { get; private set; } = -1;
        public float LastTargetSlotTime { get; private set; }

        public KeyboardWeaponController(DeviceSlotResolver slotResolver, KeyboardCharacterController charController)
        {
            _slotResolver = slotResolver;
            _charController = charController;

            _rotateBoneMethod = typeof(Device).GetMethod(
                "rotate_bone_to_target",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _playerInteractingProp = typeof(DevicePanelAttachment_Highlightable).GetProperty(
                "Player_Interacting",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _newBasicMeleeAttackMethod = typeof(NewBasicMelee).GetMethod(
                "FindTarget_Attack",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _catchingFlowerAttackMethod = typeof(Unique_Catching_Flower).GetMethod(
                "Attack_1",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        public bool IsSlotAutoFireEnabled(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _autoFireToggles.Length)
            {
                return false;
            }
            return _autoFireToggles[slotIndex];
        }

        public bool HasAnyAutoFireEnabled()
        {
            for (int i = 0; i < _autoFireToggles.Length; i++)
            {
                if (_autoFireToggles[i])
                {
                    return true;
                }
            }
            return false;
        }

        public void ToggleSlotAutoFire(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _autoFireToggles.Length)
            {
                return;
            }

            MarkSlotFeedback(slotIndex);
            ModConfig cfg = ModConfig.Instance;
            string keyLabel = slotIndex < cfg.WeaponSlotKeys.Length
                ? ModConfig.FormatKeyLabel(cfg.WeaponSlotKeys[slotIndex])
                : (slotIndex + 1).ToString();

            if (!_slotResolver.TryGetSlot(slotIndex, out ResolvedDeviceSlot slot) || slot.Device == null)
            {
                _autoFireToggles[slotIndex] = false;
                SetStatus($"[Ctrl+{keyLabel}] {slotIndex + 1} 号槽位为空（未安装设备）");
                return;
            }

            _autoFireToggles[slotIndex] = !_autoFireToggles[slotIndex];
            string devName = KeyboardToolboxController.GetDeviceDisplayName(slot.Device);
            string stateStr = _autoFireToggles[slotIndex] ? "已开启自动托管 [AUTO]" : "已取消自动托管";
            SetStatus($"[Ctrl+{keyLabel} -> {slotIndex + 1}号:{devName}] {stateStr}");
        }

        public void CancelAllAutoFire(string reasonMessage = "已取消全部武器【自动托管】")
        {
            bool hadAny = false;
            for (int i = 0; i < _autoFireToggles.Length; i++)
            {
                if (_autoFireToggles[i])
                {
                    hadAny = true;
                    _autoFireToggles[i] = false;
                }
            }

            if (hadAny && !string.IsNullOrEmpty(reasonMessage))
            {
                SetStatus(reasonMessage);
            }
        }

        public bool IsSlotHoldingAnyAction(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= DeviceSlotResolver.MaxWeaponSlots)
            {
                return false;
            }
            return _holdingPrimary[slotIndex] || _holdingAction2[slotIndex] || _holdingAction3[slotIndex];
        }

        public void UpdateInput(Keyboard kb)
        {
            if (kb == null)
            {
                return;
            }

            ModConfig cfg = ModConfig.Instance;
            bool ctrlHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool shiftHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool altHeld = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;

            // Global Auto-Fire Toggle (Tab)
            if (cfg.GlobalAutoFireToggleKey != Key.None && kb[cfg.GlobalAutoFireToggleKey].wasPressedThisFrame && !ctrlHeld && !altHeld)
            {
                bool anyOff = false;
                IReadOnlyList<ResolvedDeviceSlot> activeSlots = _slotResolver.GetOrderedSlots();
                int count = Mathf.Min(activeSlots.Count, _autoFireToggles.Length);
                if (count == 0) count = _autoFireToggles.Length;

                for (int i = 0; i < count; i++)
                {
                    if (!_autoFireToggles[i])
                    {
                        anyOff = true;
                        break;
                    }
                }

                for (int i = 0; i < _autoFireToggles.Length; i++)
                {
                    _autoFireToggles[i] = anyOff;
                }

                SetStatus(anyOff ? "已开启全武器槽位【自动开火/装填托管 [AUTO]】" : "已关闭全武器槽位【自动托管】");
            }

            for (int i = 0; i < cfg.WeaponSlotKeys.Length && i < DeviceSlotResolver.MaxWeaponSlots; i++)
            {
                Key key = cfg.WeaponSlotKeys[i];
                if (key == Key.None)
                {
                    _holdingPrimary[i] = false;
                    _holdingAction2[i] = false;
                    _holdingAction3[i] = false;
                    continue;
                }

                bool pressedThisFrame = kb[key].wasPressedThisFrame;
                bool isPressed = kb[key].isPressed;

                // If this key press was consumed by Character Dispatch (1..6 -> Q..Y), wait until release
                if (_charController != null && _charController.IsSlotKeyConsumedByDispatch(i))
                {
                    _holdingPrimary[i] = false;
                    _holdingAction2[i] = false;
                    _holdingAction3[i] = false;
                    continue;
                }

                // Ctrl + Q..Y: Toggle Auto-Fire for this slot
                if (ctrlHeld && pressedThisFrame)
                {
                    ToggleSlotAutoFire(i);
                    _holdingPrimary[i] = false;
                    _holdingAction2[i] = false;
                    _holdingAction3[i] = false;
                    continue;
                }

                if (shiftHeld)
                {
                    _holdingPrimary[i] = false;
                    _holdingAction2[i] = isPressed;
                    _holdingAction3[i] = false;

                    if (pressedThisFrame)
                    {
                        ExecuteSlotAction2OnPress(i);
                    }
                }
                else if (altHeld)
                {
                    _holdingPrimary[i] = false;
                    _holdingAction2[i] = false;
                    _holdingAction3[i] = isPressed;

                    if (pressedThisFrame)
                    {
                        ExecuteSlotAction3OnPress(i);
                    }
                }
                else if (!ctrlHeld)
                {
                    _holdingPrimary[i] = isPressed;
                    _holdingAction2[i] = false;
                    _holdingAction3[i] = false;

                    if (pressedThisFrame)
                    {
                        ExecuteSlotPrimaryOnPress(i);
                    }
                }
            }
        }

        /// <summary>
        /// Executed every 120 TPS physics/logic tick via Foundations.Tickers.Ticker.
        /// Manual control (keyboard hold or mouse drag on weapon UI) always takes priority over background [AUTO].
        /// </summary>
        public void Tick120()
        {
            IReadOnlyList<ResolvedDeviceSlot> slots = _slotResolver.GetOrderedSlots();
            float ability = ModConfig.Instance.ManualPlayerAbility;

            for (int i = 0; i < slots.Count && i < DeviceSlotResolver.MaxWeaponSlots; i++)
            {
                ResolvedDeviceSlot slot = slots[i];
                Device device = slot.Device;
                if (device == null || !device.is_validate)
                {
                    continue;
                }

                if (_holdingAction2[i])
                {
                    ExecuteSlotAction2Tick(slot, ability);
                }
                else if (_holdingAction3[i])
                {
                    ExecuteSlotAction3Tick(slot, ability);
                }
                else if (_holdingPrimary[i])
                {
                    // Explicit manual keyboard hold (works whether [AUTO] is on or off)
                    ExecuteSlotManualPrimaryTick(slot, ability);
                }
                else if (_autoFireToggles[i])
                {
                    // Yield background [AUTO] while the player is manually interacting with this weapon UI via mouse
                    if (IsPlayerMouseInteractingWithSlot(slot))
                    {
                        continue;
                    }
                    ExecuteSlotSmartLoopTick(slot, ability);
                }
            }
        }

        private bool IsPlayerMouseInteractingWithSlot(ResolvedDeviceSlot slot)
        {
            if (slot.UiView == null)
            {
                return false;
            }

            // Check if any interactive attachment (spring, crank, magazine, trigger, etc.) is currently grabbed by the player's mouse
            if (_playerInteractingProp != null)
            {
                DevicePanelAttachment_Highlightable[] attachments = slot.UiView.GetComponentsInChildren<DevicePanelAttachment_Highlightable>(false);
                if (attachments != null)
                {
                    for (int i = 0; i < attachments.Length; i++)
                    {
                        if (attachments[i] != null)
                        {
                            try
                            {
                                if (_playerInteractingProp.GetValue(attachments[i]) is bool interacting && interacting)
                                {
                                    return true;
                                }
                            }
                            catch
                            {
                                // Ignore
                            }
                        }
                    }
                }
            }

            // Also yield if left mouse button is held directly over this slot's panel rect
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && slot.PanelTransform is RectTransform rectTransform)
            {
                Camera uiCam = Foundations.MonoBehaviourSingleton<World.WorldSceneRoot>.instance != null
                    ? Foundations.MonoBehaviourSingleton<World.WorldSceneRoot>.instance.uiCamera
                    : null;
                Vector2 mousePos = mouse.position.ReadValue();
                if (RectTransformUtility.RectangleContainsScreenPoint(rectTransform, mousePos, uiCam))
                {
                    return true;
                }
            }

            return false;
        }

        private void ExecuteSlotPrimaryOnPress(int slotIndex)
        {
            MarkSlotFeedback(slotIndex);
            string keyLabel = slotIndex < ModConfig.Instance.WeaponSlotKeys.Length
                ? ModConfig.FormatKeyLabel(ModConfig.Instance.WeaponSlotKeys[slotIndex])
                : (slotIndex + 1).ToString();

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
            string devName = KeyboardToolboxController.GetDeviceDisplayName(device);
            string maintKeyLabel = slotIndex < ModConfig.Instance.SlotMaintainKeys.Length
                ? ModConfig.FormatKeyLabel(ModConfig.Instance.SlotMaintainKeys[slotIndex])
                : "A-H";

            if (!device.is_validate)
            {
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 设备已损毁！请按 [{maintKeyLabel}] 或 [Z] 扳手抢修");
                return;
            }

            // Direct manual shooter trigger / reload step (works even when [AUTO] is active and waiting on angle/target)
            if (device is DScript_Shooter shooter)
            {
                shooter.activated = true;
                InvokeRotateBoneToTarget(shooter, "roll_control");

                if (shooter.CanShoot())
                {
                    shooter.Try_Shooting(false, null);
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 手动开火 (弹药 {shooter.current_ammo})");
                }
                else
                {
                    if (shooter is DScript_Shooter_Gatlin gatlin && gatlin.current_ammo <= 0 && gatlin.carry_count <= 0)
                    {
                        gatlin.Try_Carrying();
                    }
                    shooter.Try_Start_Reloading();
                    shooter.Try_Reloading();
                    shooter.Load_Bullet_Into_Barrel();
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 手动装填/上膛 (按住 {keyLabel} 持续装填开火)");
                }
                return;
            }

            // Direct manual melee attacks (covering DScript_Melee, BasicMelee_Click, AnimSharpMelee_Click, NewBasicMelee, Unique_Catching_Flower, etc.)
            if (device is DScript_Melee_Throwing_Spear spear)
            {
                spear.Try_Melee_Attacking();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 近战刺击 (Shift+{keyLabel} 远程投矛)");
                return;
            }
            else if (device is DScript_Melee_Claymore claymore)
            {
                claymore.Try_Charging();
                if (claymore.CanAttack())
                {
                    claymore.Try_Attacking();
                }
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 蓄力挥砍 (Shift+{keyLabel} 磨刀)");
                return;
            }
            else if (device is DScript_Melee_Charge meleeCharge)
            {
                meleeCharge.Try_Charging();
                if (meleeCharge.CanAttack())
                {
                    meleeCharge.Try_Attacking();
                }
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 蓄力运转 (Shift+{keyLabel} 释放重击)");
                return;
            }
            else if (device is DScript_Melee melee)
            {
                melee.Try_Attacking();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 近战攻击！");
                return;
            }
            else if (device is BasicMelee_Click basicMeleeClick)
            {
                basicMeleeClick.UI_Controlled_Attack(basicMeleeClick.UI_Atk_Can_Horizontal, null);
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 近战挥击！");
                return;
            }
            else if (device is NewBasicMelee newBasicMelee)
            {
                try { _newBasicMeleeAttackMethod?.Invoke(newBasicMelee, null); } catch { }
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 近战攻击！");
                return;
            }
            else if (device is Unique_Catching_Flower flower)
            {
                try { _catchingFlowerAttackMethod?.Invoke(flower, null); } catch { }
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 捕食攻击！");
                return;
            }
            else if (device is Unique_Cactus cactus)
            {
                cactus.Accelerate();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 加速运转！");
                return;
            }
            else if (device is Unique_War_Drum warDrum)
            {
                warDrum.UI_Controlled_Beating(false);
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 擂鼓助威！");
                return;
            }

            // Helicopter manual attack
            if (device is DScript_Helicopter helicopter)
            {
                helicopter.TryAttacking();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 直升机出击！");
                return;
            }

            // Accelerator fuel tap
            if (device is DScript_Accelerator acc)
            {
                acc.TryAddFuel();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 添加燃料加速！");
                return;
            }

            // Backup ammo box manual tap
            if (device is DScript_Backup_Ammo backupAmmo)
            {
                backupAmmo.TryLoad();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 装填备用弹药！");
                return;
            }

            // Boomerang / CarBomb manual attack
            if (device is DScript_Special_Boomerang boomerang)
            {
                boomerang.Try_Attacking();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 投出回旋镖！");
                return;
            }
            if (device is DScript_Special_CarBomb carBomb)
            {
                carBomb.Try_Attacking();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 释放自爆小车！");
                return;
            }

            // BasicWheel Push_Car_btn or JetWheel Accelerate
            if (slot.UiView is JetWheelUiView jetWheelUi)
            {
                jetWheelUi.Accelerate();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 喷气车轮加速！");
                return;
            }
            else if (slot.UiView is BasicWheelUiView wheelUi && wheelUi.Push_Car_btn != null)
            {
                if (wheelUi.Push_Car_btn.interactable)
                {
                    wheelUi.Push_Car_btn.onClick?.Invoke();
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 推车前进！");
                }
                else
                {
                    SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 车轮运转中 (按住 {keyLabel} 持续加速)");
                }
                return;
            }
            else if (slot.UiView is ShieldGeneratorUiView shieldUi)
            {
                shieldUi.Raise_Shield();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 展开能量护盾！");
                return;
            }
            else if (slot.UiView is Ditto_UiView dittoUi)
            {
                dittoUi.btn_cast();
                SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 释放百变怪技能！");
                return;
            }

            // Unique War Drum left stick
            if (slot.UiView != null && slot.UiView.view_add_ons != null)
            {
                foreach (DeviceUIViewAddOn addOn in slot.UiView.view_add_ons)
                {
                    if (addOn is DeviceUIViewAddOn_Unique_Drum drum && drum.left_stick != null)
                    {
                        drum.left_stick.click_action?.Invoke();
                        SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 战鼓左槌敲击！");
                        return;
                    }
                }
            }

            SetStatus($"[{keyLabel} -> {slotIndex + 1}号:{devName}] 操控设备中");
        }

        private void ExecuteSlotManualPrimaryTick(ResolvedDeviceSlot slot, float ability)
        {
            Device device = slot.Device;

            // Run the smart loop at boosted manual speed AND force direct firing/attacking when ready
            ExecuteSlotSmartLoopTick(slot, ability * 1.35f);

            if (device is DScript_Shooter shooter && shooter.CanShoot())
            {
                shooter.Try_Shooting(false, null);
            }
            else if (device is DScript_Melee_Throwing_Spear spear)
            {
                spear.Try_Melee_Attacking();
            }
            else if (device is DScript_Melee melee && !(device is DScript_Melee_Charge) && !(device is DScript_Melee_Claymore))
            {
                melee.Try_Attacking();
            }
            else if (device is BasicMelee_Click basicMeleeClick)
            {
                basicMeleeClick.UI_Controlled_Attack(basicMeleeClick.UI_Atk_Can_Horizontal, null);
            }
            else if (device is DScript_Helicopter helicopter)
            {
                helicopter.TryAttacking();
            }
        }

        private void ExecuteSlotSmartLoopTick(ResolvedDeviceSlot slot, float ability)
        {
            Device device = slot.Device;

            // 1. Ensure Shooter turrets auto-rotate to aim at targets even when no worker is stationed
            if (device is DScript_Shooter shooter)
            {
                shooter.activated = true;
                InvokeRotateBoneToTarget(shooter, "roll_control");

                // Smart Loop: If ammo is empty, auto-reload; otherwise auto-attack
                if (shooter.current_ammo <= 0 || !shooter.CanShoot())
                {
                    shooter.TryToAutoReload(ability);
                }

                if (shooter is DScript_Shooter_Gatlin gatlin)
                {
                    // Gatlin needs ammo carried if ammo is 0 and carry_count == 0
                    if (gatlin.current_ammo <= 0 && gatlin.carry_count <= 0)
                    {
                        gatlin.TryToAutoCarry(ability);
                    }
                }

                shooter.TryToAutoAttack(ability);
            }
            else
            {
                // Non-shooter devices
                if (device is IAttack_New attackNew)
                {
                    attackNew.TryToAutoAttack(ability);
                }
                if (device is IAttack legacyAttack)
                {
                    legacyAttack.TryToAutoAttack();
                }

                if (device is IReload_New reloadNew)
                {
                    reloadNew.TryToAutoReload(ability);
                }
                if (device is ILoad legacyLoad)
                {
                    legacyLoad.TryToAutoLoad();
                }

                // Legacy BasicMelee_Click / AnimSharpMelee_Click: auto-sharpen when dull, and attack even when angle/distance check is strict
                if (device is BasicMelee_Click basicMeleeClick)
                {
                    if (basicMeleeClick.Sharpness_Current <= basicMeleeClick.Sharpness_Min + 0.05f)
                    {
                        if (device is AnimSharpMelee_Click animSharp)
                        {
                            animSharp.Sharpening();
                        }
                        else if (device is ISharp legacySharp)
                        {
                            legacySharp.TryToAutoSharp();
                        }
                    }
                }
            }

            // Charge melee weapons (Flail, CircularSaw, Blackhole, Fan, Rocket)
            if (device is ICharge chargeDevice)
            {
                chargeDevice.TryToAutoCharge();
            }
            if (device is DScript_Melee_Charge meleeCharge)
            {
                if (meleeCharge.CanAttack() && meleeCharge.charge_process >= 0.99f)
                {
                    meleeCharge.Try_Attacking();
                }
            }

            // Claymore: auto-sharpen if dull, charge and attack
            if (device is DScript_Melee_Claymore claymore)
            {
                if (claymore.Get_Sharpness() <= 0.05f)
                {
                    claymore.TryToAutoSharp();
                }
                claymore.Try_Charging();
                if (claymore.CanAttack() && claymore.charge_process >= 0.99f)
                {
                    claymore.Try_Attacking();
                }
            }

            // Rotate devices (Cannon, Windlass, Helicopter, Ram)
            if (device is IRotate_New rotateNew)
            {
                rotateNew.TryToAutoRotate(ability);
            }
            if (device is IRotate legacyRotate)
            {
                legacyRotate.TryToAutoRotate();
            }

            // Energy / Wheel / Shield acceleration
            if (device is IEnergy energyDevice)
            {
                energyDevice.TryToAccelerate();
            }
        }

        private void ExecuteSlotAction2OnPress(int slotIndex)
        {
            MarkSlotFeedback(slotIndex);
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.WeaponSlotKeys[slotIndex]);

            if (!_slotResolver.TryGetSlot(slotIndex, out ResolvedDeviceSlot slot) || slot.Device == null)
            {
                SetStatus($"[Shift+{keyLabel}] {slotIndex + 1} 号槽位为空（未安装设备）");
                return;
            }

            Device device = slot.Device;
            string devName = KeyboardToolboxController.GetDeviceDisplayName(device);
            if (!device.is_validate)
            {
                SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 设备已损毁，无法执行副操作");
                return;
            }

            // 1. Throwing Spear -> Ranged Spear Throw
            if (device is DScript_Melee_Throwing_Spear spear)
            {
                InvokeRotateBoneToTarget(spear, "roll_control");
                spear.Try_Shooting();
                SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 远程投矛！");
                return;
            }

            // 2. Special Quiver -> Toggle Activate
            if (device is DScript_Special_Quiver quiver)
            {
                if (quiver.CanSwitch())
                {
                    quiver.ChangeActivate(!quiver.is_activate);
                    SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 特殊箭袋：{(quiver.is_activate ? "已激活" : "已关闭")}");
                }
                else
                {
                    quiver.Try_Loading();
                    SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 特殊箭袋：装填中");
                }
                return;
            }

            // 3. Claymore -> Manual Sharpen
            if (device is DScript_Melee_Claymore claymore)
            {
                claymore.Try_Sharping(0.15f);
                SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 手动打磨锋利度");
                return;
            }
            if (device is AnimSharpMelee_Click animSharp)
            {
                animSharp.Sharpening();
                SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 手动打磨锋利度");
                return;
            }
            if (device is BasicMelee_Click basicMeleeClick)
            {
                basicMeleeClick.UI_Controlled_Sharpen(0.15f);
                SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 手动打磨锋利度");
                return;
            }

            // 4. Charge Melee -> Immediate Release Strike
            if (device is DScript_Melee_Charge meleeCharge)
            {
                meleeCharge.Try_Attacking();
                SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 蓄力近战：释放攻击！");
                return;
            }

            // 5. Shooters -> Manual Early Reload / Load Bullet Into Barrel
            if (device is DScript_Shooter shooter)
            {
                if (shooter is DScript_Shooter_Gatlin gatlin)
                {
                    gatlin.Try_Reloading();
                    SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 装填弹药箱");
                }
                else
                {
                    shooter.Try_Start_Reloading();
                    shooter.Try_Reloading();
                    shooter.Load_Bullet_Into_Barrel();
                    SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 手动提前装填/上膛");
                }
                return;
            }

            // 6. Unique War Drum -> Right Stick
            if (slot.UiView != null && slot.UiView.view_add_ons != null)
            {
                foreach (DeviceUIViewAddOn addOn in slot.UiView.view_add_ons)
                {
                    if (addOn is DeviceUIViewAddOn_Unique_Drum drum && drum.right_stick != null)
                    {
                        drum.right_stick.click_action?.Invoke();
                        SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 战鼓右槌敲击");
                        return;
                    }
                }
            }

            // 7. Backup Ammo -> Manual Load
            if (device is DScript_Backup_Ammo backupAmmo)
            {
                backupAmmo.TryLoad();
                SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 备用弹药箱：手动装填");
                return;
            }

            SetStatus($"[Shift+{keyLabel} -> {slotIndex + 1}号:{devName}] 该设备无独立副操作 (直接按 {keyLabel} 即可操控)");
        }

        private void ExecuteSlotAction2Tick(ResolvedDeviceSlot slot, float ability)
        {
            Device device = slot.Device;

            if (device is DScript_Melee_Throwing_Spear spear)
            {
                InvokeRotateBoneToTarget(spear, "roll_control");
                spear.Try_Shooting();
                return;
            }

            if (device is ISharpNew sharpNew)
            {
                sharpNew.TryToAutoSharp();
            }
            if (device is ISharp legacySharp)
            {
                legacySharp.TryToAutoSharp();
            }

            if (device is DScript_Shooter shooter)
            {
                shooter.Try_Start_Reloading();
                shooter.TryToAutoReload(ability * 1.5f);
                shooter.Load_Bullet_Into_Barrel();
                return;
            }

            if (device is IReload_New reloadNew)
            {
                reloadNew.TryToAutoReload(ability * 1.5f);
            }
            if (device is ILoad legacyLoad)
            {
                legacyLoad.TryToAutoLoad();
            }
        }

        private void ExecuteSlotAction3OnPress(int slotIndex)
        {
            MarkSlotFeedback(slotIndex);
            string keyLabel = ModConfig.FormatKeyLabel(ModConfig.Instance.WeaponSlotKeys[slotIndex]);

            if (!_slotResolver.TryGetSlot(slotIndex, out ResolvedDeviceSlot slot) || slot.Device == null)
            {
                SetStatus($"[Alt+{keyLabel}] {slotIndex + 1} 号槽位为空（未安装设备）");
                return;
            }

            Device device = slot.Device;
            string devName = KeyboardToolboxController.GetDeviceDisplayName(device);
            if (!device.is_validate)
            {
                SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 设备已损毁，无法执行第三操作");
                return;
            }

            if (device is DScript_Shooter_Gatlin gatlin)
            {
                gatlin.Try_Carrying();
                SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 搬运弹药箱 ({gatlin.carry_count}/{gatlin.carry_limit})");
                return;
            }

            if (device is DScript_Shooter_Cannon cannon)
            {
                cannon.Try_Rotating(1f);
                SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 手摇校准火炮俯仰角");
                return;
            }

            if (device is DScript_Shooter_Windlass windlass)
            {
                windlass.Try_Rotating(1f);
                SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 手摇绞盘拉弦");
                return;
            }

            if (device is DScript_Ram ram)
            {
                ram.Try_Rotating(1f);
                SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 手摇冲角转向");
                return;
            }

            if (device is DScript_Helicopter)
            {
                SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 直升机手摇赋能");
                return;
            }

            if (device is DScript_Shooter shooter)
            {
                shooter.Unload_Barrel();
                SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 退出膛内子弹");
                return;
            }

            SetStatus($"[Alt+{keyLabel} -> {slotIndex + 1}号:{devName}] 该设备无第三操作");
        }

        private void ExecuteSlotAction3Tick(ResolvedDeviceSlot slot, float ability)
        {
            Device device = slot.Device;

            if (device is DScript_Shooter_Gatlin gatlin)
            {
                gatlin.TryToAutoCarry(ability * 1.5f);
                return;
            }

            if (device is DScript_Shooter_Cannon cannon)
            {
                cannon.Try_Rotating(0.5f);
            }

            if (device is DScript_Shooter_Windlass windlass)
            {
                windlass.Try_Rotating(0.5f);
            }

            if (device is DScript_Ram ram)
            {
                ram.Try_Rotating(0.5f);
            }

            if (device is IRotate_New rotateNew)
            {
                rotateNew.TryToAutoRotate(ability * 1.5f);
            }
            if (device is IRotate legacyRotate)
            {
                legacyRotate.TryToAutoRotate();
            }
        }

        private void InvokeRotateBoneToTarget(Device device, string boneName)
        {
            if (_rotateBoneMethod == null || device == null)
            {
                return;
            }
            try
            {
                _rotateBoneMethod.Invoke(device, new object[] { boneName });
            }
            catch
            {
                // Ignore if bone does not exist on this specific device variant
            }
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
