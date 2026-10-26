using System;
using System.Reflection;
using Foundations;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.CaravanBoards;
using World.Caravans;
using World.Devices;
using World.Devices.Device_AI;
using World.Widgets;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Handles Left-Hand Lower Row (Z X C V) caravan driving and wheel controls:
    /// - Z (BrakeKey): Hold to pull the caravan brake lever (CaravanLever2); release to let go.
    /// - X (PsionicBoostKey): Dedicated to Psionic Sprint Charge (SpeedChargeView).
    /// - C (BellowsKey): Hold to pump furnace bellows (BellowView / Widget_Blower_Context) and push stuck caravan.
    /// - V (WheelSlotKey): Reserved wheel slot operation (JetWheel boost / PushCar).
    /// </summary>
    public sealed class KeyboardDrivingController
    {
        private bool _isKeyboardBraking;
        private bool _isKeyboardBlowering;
        private CaravanUiView _cachedCaravanUiView;
        private float _lastViewLookupTime;

        private static readonly FieldInfo _leverDraggingField = typeof(CaravanLever2).GetField(
            "m_is_lever_draging",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo _sprintCdRemainTicksField = typeof(SpeedChargeView).GetField(
            "m_spring_cd_remain_ticks",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public string LastActionStatus { get; private set; } = string.Empty;
        public float LastActionStatusTime { get; private set; }
        public bool IsBraking => _isKeyboardBraking;
        public bool IsBlowering => _isKeyboardBlowering;

        public void UpdateInput(Keyboard kb)
        {
            if (kb == null)
            {
                return;
            }

            ModConfig cfg = ModConfig.Instance;
            bool ctrlHeld = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool altHeld = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            if (ctrlHeld || altHeld)
            {
                return;
            }

            // 1. Brake Key (Z)
            bool brakePressed = cfg.BrakeKey != Key.None && kb[cfg.BrakeKey].isPressed;
            bool brakeJustPressed = cfg.BrakeKey != Key.None && kb[cfg.BrakeKey].wasPressedThisFrame;
            HandleBrakeInput(brakePressed, brakeJustPressed, ModConfig.FormatKeyLabel(cfg.BrakeKey));

            // 2. Psionic Acceleration Key (X) - strictly separated from Bellows
            if (cfg.PsionicBoostKey != Key.None && kb[cfg.PsionicBoostKey].wasPressedThisFrame)
            {
                HandlePsionicBoostInput(ModConfig.FormatKeyLabel(cfg.PsionicBoostKey));
            }

            // 3. Furnace Bellows Key (C) - strictly separated from Psionic Acceleration
            bool bellowsPressed = cfg.BellowsKey != Key.None && kb[cfg.BellowsKey].isPressed;
            bool bellowsJustPressed = cfg.BellowsKey != Key.None && kb[cfg.BellowsKey].wasPressedThisFrame;
            HandleBellowsInput(bellowsPressed, bellowsJustPressed, ModConfig.FormatKeyLabel(cfg.BellowsKey));

            // 4. Wheel Slot Reserved Key (V)
            if (cfg.WheelSlotKey != Key.None && kb[cfg.WheelSlotKey].wasPressedThisFrame)
            {
                HandleWheelSlotInput(ModConfig.FormatKeyLabel(cfg.WheelSlotKey));
            }
        }

        public void Tick120()
        {
            try
            {
                if (_isKeyboardBraking)
                {
                    ApplyBrakeTick();
                }
            }
            catch
            {
                // Ignore if caravan UI structure changes across versions
            }
        }

        private void HandleBrakeInput(bool isPressed, bool wasPressedThisFrame, string keyLabel)
        {
            try
            {
                if (isPressed)
                {
                    _isKeyboardBraking = true;
                    ApplyBrakeTick();
                    if (wasPressedThisFrame)
                    {
                        SetStatus($"[{keyLabel}:刹车] 已拉起刹车拉杆 (松开 {keyLabel} 恢复行驶)");
                    }
                }
                else if (_isKeyboardBraking)
                {
                    _isKeyboardBraking = false;
                    ReleaseBrake();
                    SetStatus($"[{keyLabel}:刹车] 已松开刹车拉杆");
                }
            }
            catch
            {
                _isKeyboardBraking = false;
            }
        }

        private void ApplyBrakeTick()
        {
            WorldContext ctx = Singleton<WorldContext>.instance;
            if (ctx != null)
            {
                ctx.brake_lever_input = 1f;
            }

            CaravanUiView cuv = GetCaravanUiView();
            if (cuv != null && cuv.lever != null)
            {
                try { _leverDraggingField?.SetValue(cuv.lever, true); } catch { }
                if (cuv.lever.lever_slider != null)
                {
                    float maxVal = cuv.lever.lever_slider.maxValue > 0f ? cuv.lever.lever_slider.maxValue : 1f;
                    cuv.lever.lever_slider.value = maxVal;
                }
            }
        }

        private void ReleaseBrake()
        {
            WorldContext ctx = Singleton<WorldContext>.instance;
            if (ctx != null)
            {
                ctx.brake_lever_input = 0f;
            }

            CaravanUiView cuv = GetCaravanUiView();
            if (cuv != null && cuv.lever != null)
            {
                try { _leverDraggingField?.SetValue(cuv.lever, false); } catch { }
                if (cuv.lever.lever_slider != null)
                {
                    cuv.lever.lever_slider.value = 0f;
                }
                if (cuv.lever.lever_slider_for_view != null)
                {
                    cuv.lever.lever_slider_for_view.value = 0f;
                }
            }
        }

        private void HandlePsionicBoostInput(string keyLabel)
        {
            try
            {
                if (TryTriggerSpeedCharge(out string sprintDetail))
                {
                    SetStatus($"[{keyLabel}:灵能加速] 已触发灵能冲刺加速!");
                }
                else
                {
                    SetStatus($"[{keyLabel}:灵能加速] {(string.IsNullOrEmpty(sprintDetail) ? "灵能加速冷却中或未解锁" : sprintDetail)}");
                }
            }
            catch
            {
            }
        }

        private void HandleBellowsInput(bool isPressed, bool wasPressedThisFrame, string keyLabel)
        {
            try
            {
                if (wasPressedThisFrame)
                {
                    bool triggeredBlower = TryStartBlower();
                    bool triggeredPush = TryPushCaravanIfStopped();

                    if (triggeredBlower)
                    {
                        SetStatus($"[{keyLabel}:风箱] 正在鼓动炉火风箱加速! (按住 {keyLabel} 持续鼓风)");
                    }
                    else if (triggeredPush)
                    {
                        SetStatus($"[{keyLabel}:风箱/推车] 已推动篷车起步!");
                    }
                    else
                    {
                        SetStatus($"[{keyLabel}:风箱] 当前炉火风箱未就绪或冷却中");
                    }
                }

                if (isPressed)
                {
                    if (!_isKeyboardBlowering)
                    {
                        TryStartBlower();
                    }
                }
                else if (_isKeyboardBlowering)
                {
                    StopBlower();
                    SetStatus($"[{keyLabel}:风箱] 已停止鼓风");
                }
            }
            catch
            {
                _isKeyboardBlowering = false;
            }
        }

        private bool TryTriggerSpeedCharge(out string detail)
        {
            detail = string.Empty;
            try
            {
                WorldSceneRoot root = MonoBehaviourSingleton<WorldSceneRoot>.instance;
                SpeedChargeView scv = root != null ? root.speed_charge_view : null;
                if (scv == null)
                {
                    scv = UnityEngine.Object.FindObjectOfType<SpeedChargeView>();
                }

                if (scv == null || !scv.gameObject.activeInHierarchy)
                {
                    detail = "灵能冲刺未激活";
                    return false;
                }

                BattleContext bctx = Singleton<BattleContext>.instance;
                if (bctx != null && bctx.sprint_cd_sec < 0)
                {
                    detail = "尚未解锁灵能冲刺科技";
                    return false;
                }

                int remainTicks = 0;
                try
                {
                    if (_sprintCdRemainTicksField != null && _sprintCdRemainTicksField.GetValue(scv) is int ticks)
                    {
                        remainTicks = ticks;
                    }
                }
                catch { }

                if (remainTicks > 0)
                {
                    float remainSec = remainTicks / 120f;
                    detail = $"灵能冲刺冷却中 ({remainSec:F1}s)";
                    return false;
                }

                if (scv.obj_cd != null && scv.obj_cd.activeSelf)
                {
                    detail = "灵能冲刺本场次数已用尽或冷却中";
                    return false;
                }

                scv.@do();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryStartBlower()
        {
            try
            {
                Widget_Blower_Context blowerCtx = Singleton<Widget_Blower_Context>.instance;
                if (blowerCtx == null || !blowerCtx.is_active)
                {
                    return false;
                }

                blowerCtx.notify_on_start_blower();
                _isKeyboardBlowering = true;

                CaravanUiView cuv = GetCaravanUiView();
                if (cuv != null && cuv.blower != null && cuv.blower.anim != null)
                {
                    cuv.blower.anim.StopPlayback();
                    cuv.blower.anim.speed = 1f;
                    cuv.blower.anim.Play(0);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StopBlower()
        {
            _isKeyboardBlowering = false;
            try
            {
                Widget_Blower_Context blowerCtx = Singleton<Widget_Blower_Context>.instance;
                if (blowerCtx != null)
                {
                    blowerCtx.notify_on_end_blower();
                }

                CaravanUiView cuv = GetCaravanUiView();
                if (cuv != null && cuv.blower != null && cuv.blower.anim != null)
                {
                    cuv.blower.anim.StartPlayback();
                    cuv.blower.anim.speed = -2f;
                    cuv.blower.anim.Play(0);
                }
            }
            catch
            {
            }
        }

        private bool TryPushCaravanIfStopped()
        {
            try
            {
                Widget_PushCar_Context pushCtx = Singleton<Widget_PushCar_Context>.instance;
                if (pushCtx != null && pushCtx.AbleToPush() && pushCtx.AbleToPush_CheckCD)
                {
                    pushCtx.PushCaravan();
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private void HandleWheelSlotInput(string keyLabel)
        {
            try
            {
                if (TryTriggerWheelDevice(out string wheelDetail))
                {
                    SetStatus($"[{keyLabel}:车轮槽] {wheelDetail}");
                    return;
                }

                if (TryPushCaravanIfStopped())
                {
                    SetStatus($"[{keyLabel}:车轮槽] 已触发篷车推车起步!");
                    return;
                }

                SetStatus($"[{keyLabel}:车轮槽] 当前车轮无主动操作 (保留键)");
            }
            catch
            {
            }
        }

        private bool TryTriggerWheelDevice(out string detail)
        {
            detail = string.Empty;
            try
            {
                Mission mission = Singleton<Mission>.instance;
                if (mission == null || !mission.try_get_mgr("DeviceMgr", out DeviceMgr devMgr) || devMgr == null || devMgr.slots_device == null)
                {
                    return false;
                }

                foreach (DeviceSlot slot in devMgr.slots_device)
                {
                    if (slot == null || !DeviceSlotResolver.IsWheelSlot(slot._name) || slot.slot_device == null)
                    {
                        continue;
                    }

                    if (slot.slot_device is JetWheel jetWheel)
                    {
                        if (jetWheel.Current_Energy > 1f)
                        {
                            jetWheel.Accelerate();
                            detail = "喷气车轮推进加速!";
                            return true;
                        }
                        detail = "喷气车轮能量不足";
                        return false;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private CaravanUiView GetCaravanUiView()
        {
            if (_cachedCaravanUiView != null && _cachedCaravanUiView.gameObject != null && _cachedCaravanUiView.gameObject.activeInHierarchy)
            {
                return _cachedCaravanUiView;
            }

            float now = Time.unscaledTime;
            if (now - _lastViewLookupTime < 1.0f)
            {
                return _cachedCaravanUiView;
            }

            _lastViewLookupTime = now;
            _cachedCaravanUiView = UnityEngine.Object.FindObjectOfType<CaravanUiView>();
            return _cachedCaravanUiView;
        }

        private void SetStatus(string msg)
        {
            LastActionStatus = msg;
            LastActionStatusTime = Time.unscaledTime;
        }
    }
}
