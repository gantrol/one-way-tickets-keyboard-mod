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
    /// Handles Left-Hand Lower Row (Z X C) caravan driving and wheel controls:
    /// - Z (BrakeKey): Hold to pull the caravan brake lever (CaravanLever2); release to let go.
    /// - X (PsionicBoostKey): Tap/Hold to trigger Psionic Sprint Charge (SpeedChargeView) + pump furnace bellows (BellowView / Widget_Blower_Context) + push stuck caravan.
    /// - C (WheelSlotKey): Reserved wheel slot operation (JetWheel boost / PushCar / Bellows).
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
        public bool IsBoosting => _isKeyboardBlowering;

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

            // 2. Psionic Boost / Sprint + Bellows Key (X)
            bool boostPressed = cfg.PsionicBoostKey != Key.None && kb[cfg.PsionicBoostKey].isPressed;
            bool boostJustPressed = cfg.PsionicBoostKey != Key.None && kb[cfg.PsionicBoostKey].wasPressedThisFrame;
            HandleBoostInput(boostPressed, boostJustPressed, ModConfig.FormatKeyLabel(cfg.PsionicBoostKey));

            // 3. Wheel Slot Reserved Key (C)
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

        private void HandleBoostInput(bool isPressed, bool wasPressedThisFrame, string keyLabel)
        {
            try
            {
                if (wasPressedThisFrame)
                {
                    bool triggeredSprint = TryTriggerSpeedCharge(out string sprintDetail);
                    bool triggeredBlower = TryStartBlower();
                    bool triggeredPush = TryPushCaravanIfStopped();
                    bool triggeredJet = TryTriggerWheelDevice(out string wheelDetail);

                    if (triggeredSprint)
                    {
                        SetStatus($"[{keyLabel}:灵能加速] 已触发灵能冲刺加速! {(triggeredBlower ? "+ 炉火鼓风中" : string.Empty)}");
                    }
                    else if (triggeredBlower)
                    {
                        SetStatus($"[{keyLabel}:加速] 正在鼓风提升炉火功率! {(string.IsNullOrEmpty(sprintDetail) ? string.Empty : $"({sprintDetail})")}");
                    }
                    else if (triggeredJet)
                    {
                        SetStatus($"[{keyLabel}:加速] {wheelDetail}");
                    }
                    else if (triggeredPush)
                    {
                        SetStatus($"[{keyLabel}:加速] 已推动篷车起步加速!");
                    }
                    else
                    {
                        SetStatus($"[{keyLabel}:灵能加速] {(string.IsNullOrEmpty(sprintDetail) ? "当前加速冷却中或未解锁" : sprintDetail)}");
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

                if (TryStartBlower())
                {
                    SetStatus($"[{keyLabel}:车轮槽] 当前车轮为被动型，已触发风箱助推!");
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
