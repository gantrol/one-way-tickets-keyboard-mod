using System;
using System.Collections.Generic;
using Foundations;
using Foundations.Tickers;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.Characters;
using World.Helpers;
using World.Player_Tool;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Persistent MonoBehaviour that coordinates two-handed keyboard input (via Unity InputSystem),
    /// 120 TPS deterministic simulation ticks, tool pull-out animations, and the HUD overlay anchored near the Toolbox.
    /// </summary>
    public sealed class KeyboardModBehaviour : MonoBehaviour
    {
        private const float TickInterval = 1f / 120f;

        private DeviceSlotResolver _slotResolver;
        private KeyboardCharacterController _charController;
        private KeyboardWeaponController _weaponController;
        private KeyboardToolboxController _toolboxController;
        private KeyboardDrivingController _drivingController;

        private float _tickAccumulator;
        private GUIStyle _badgeStyle;
        private GUIStyle _activeBadgeStyle;
        private GUIStyle _autoBadgeButtonStyle;
        private GUIStyle _statusBarStyle;
        private GUIStyle _toastPopupStyle;
        private GUIStyle _autoCancelBannerStyle;
        private Texture2D _darkBgTex;
        private Texture2D _activeBgTex;
        private Texture2D _autoBgTex;

        private string _systemStatusMessage = string.Empty;
        private float _systemStatusTime;

        private void Awake()
        {
            _slotResolver = new DeviceSlotResolver();
            _charController = new KeyboardCharacterController(_slotResolver);
            _weaponController = new KeyboardWeaponController(_slotResolver, _charController);
            _toolboxController = new KeyboardToolboxController(_slotResolver);
            _drivingController = new KeyboardDrivingController();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            ModConfig cfg = ModConfig.Instance;

            // System hotkeys: F9 (toggle HUD) and F10 (reload config)
            if (cfg.ToggleHudOverlayKey != Key.None && kb[cfg.ToggleHudOverlayKey].wasPressedThisFrame)
            {
                cfg.ShowHudOverlay = !cfg.ShowHudOverlay;
                _systemStatusMessage = cfg.ShowHudOverlay ? "已开启键盘快捷键提示 (F9)" : "已隐藏键盘快捷键提示 (F9)";
                _systemStatusTime = Time.unscaledTime;
            }

            if (cfg.ReloadConfigKey != Key.None && kb[cfg.ReloadConfigKey].wasPressedThisFrame)
            {
                ModConfig.Load();
                _systemStatusMessage = "已热重载 OneWayTickets.KeyboardMod.ini 配置 (F10)";
                _systemStatusTime = Time.unscaledTime;
            }

            // Only process gameplay keys when inside the active World combat/travel state (not in safe area or settlement)
            WorldSceneRoot sceneRoot = MonoBehaviourSingleton<WorldSceneRoot>.instance;
            if (sceneRoot == null || !IsInCombatScene(sceneRoot))
            {
                _tickAccumulator = 0f;
                return;
            }

            try
            {
                // 1. Right-Hand Character input (Y..[ or 1..6 selection, ' / ] auto-assign, and A..G / Q..T dispatch)
                _charController.UpdateInput(kb);

                // 2. Left-Hand Weapon bar input (A..G, Shift+A..G, Alt+A..G, Ctrl+A..G, Tab)
                _weaponController.UpdateInput(kb);

                // 3. Toolbox input (Left-Hand Q..T per-slot fix + Right-Hand H J K L ; + Space global fix)
                _toolboxController.UpdateInput(kb);

                // 4. Left-Hand Driving & Wheel input (Z = Brake, X = Psionic Boost / Bellows, C = Wheel Slot)
                _drivingController.UpdateInput(kb);

                // 5. Run 120 TPS simulation steps when game ticker is active and unpaused
                Ticker ticker = Singleton<Ticker>.instance;
                bool tickerActive = ticker == null || (ticker.can_start_tick && !ticker.is_end);
                if (tickerActive && Time.timeScale > 0f)
                {
                    _tickAccumulator += Time.deltaTime;
                    int steps = 0;
                    while (_tickAccumulator >= TickInterval && steps < 6)
                    {
                        _tickAccumulator -= TickInterval;
                        steps++;
                        _charController.Tick120();
                        _weaponController.Tick120();
                        _toolboxController.Tick120();
                        _drivingController.Tick120();
                    }
                    if (_tickAccumulator > TickInterval * 4f)
                    {
                        _tickAccumulator = 0f;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KeyboardMod] Update error: {ex.Message}");
            }
        }

        private bool IsInCombatScene(WorldSceneRoot sceneRoot)
        {
            if (sceneRoot == null)
            {
                return false;
            }

            WorldContext ctx = Singleton<WorldContext>.instance;
            if (ctx != null && (ctx.in_safe_area || ctx.in_safe_area2 || ctx.is_in_settlement))
            {
                return false;
            }

            if (sceneRoot.safe_area != null && sceneRoot.safe_area.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (sceneRoot.settlement_panel != null && sceneRoot.settlement_panel.gameObject.activeInHierarchy)
            {
                return false;
            }

            return true;
        }

        private void OnGUI()
        {
            ModConfig cfg = ModConfig.Instance;
            if (!cfg.ShowHudOverlay)
            {
                return;
            }

            WorldSceneRoot sceneRoot = MonoBehaviourSingleton<WorldSceneRoot>.instance;
            if (sceneRoot == null)
            {
                return;
            }

            bool inCombat = IsInCombatScene(sceneRoot);
            string activeToast = GetLatestToastMessage();

            if (!inCombat)
            {
                if (!string.IsNullOrEmpty(activeToast))
                {
                    EnsureGuiStyles();
                    DrawToolboxStatusArea(sceneRoot, cfg, activeToast, false);
                }
                return;
            }

            EnsureGuiStyles();

            DrawToolboxStatusArea(sceneRoot, cfg, activeToast, true);
            DrawDeviceSlotBadges(sceneRoot, cfg);
        }

        /// <summary>
        /// Draws the key response feedback box and compact shortcut hints right above the Toolbox on the right side
        /// of the caravan dashboard (never overlapping the top route progress bar or top-left relic panel).
        /// </summary>
        private void DrawToolboxStatusArea(WorldSceneRoot sceneRoot, ModConfig cfg, string activeToast, bool inCombat)
        {
            Character selectedChar = inCombat ? Cubicle_Helper.GetSelectCharacter() : null;
            string charHint = selectedChar != null ? " 【已选人：按 A-G 派驻 / Bksp 撤回】" : string.Empty;
            string smartHint = (inCombat && _toolboxController.IsGlobalSmartMaintaining) ? " 【空格：全车抢修中】" : string.Empty;
            string brakeHint = (inCombat && _drivingController.IsBraking) ? " 【Z：刹车中】" : string.Empty;

            string wrenchKey = ModConfig.FormatKeyLabel(cfg.ToolWrenchKey);
            string extKey = ModConfig.FormatKeyLabel(cfg.ToolExtinguisherKey);
            string sawKey = ModConfig.FormatKeyLabel(cfg.ToolSawKey);
            string ragKey = ModConfig.FormatKeyLabel(cfg.ToolRagKey);
            string wakeKey = ModConfig.FormatKeyLabel(cfg.ToolWakeUpKey);
            string autoCharKey = ModConfig.FormatKeyLabel(cfg.CharacterAutoAssignKey);
            string brakeKey = ModConfig.FormatKeyLabel(cfg.BrakeKey);
            string boostKey = ModConfig.FormatKeyLabel(cfg.PsionicBoostKey);
            string bellowsKey = ModConfig.FormatKeyLabel(cfg.BellowsKey);
            string bellowsHint = (inCombat && _drivingController.IsBlowering) ? $" 【{bellowsKey}：风箱鼓风中】" : string.Empty;

            string barText = string.IsNullOrEmpty(activeToast)
                ? $"工具[{wrenchKey}扳 {extKey}灭 {sawKey}锯 {ragKey}擦 {wakeKey}醒 Space全修] 车[{brakeKey}刹 {boostKey}灵能 {bellowsKey}风箱] 人[Y~[选 {autoCharKey}分配]{charHint}{smartHint}{brakeHint}{bellowsHint}"
                : $"⌨ {activeToast}{charHint}{smartHint}{brakeHint}{bellowsHint}";

            Vector2 anchorGuiPos = GetToolboxAreaAnchorGuiPos(sceneRoot);
            float boxWidth = Mathf.Clamp(Screen.width * 0.28f, 380f, 520f);
            float x = Mathf.Clamp(anchorGuiPos.x - boxWidth * 0.62f, Screen.width * 0.52f, Screen.width - boxWidth - 12f);
            float y = Mathf.Clamp(anchorGuiPos.y, Screen.height * 0.48f, Screen.height * 0.66f);

            Rect barRect = new Rect(x, y, boxWidth, 26f);
            GUI.Box(barRect, barText, string.IsNullOrEmpty(activeToast) ? _statusBarStyle : _toastPopupStyle);

            // Interactive [AUTO] status pill right above the status bar in the same Toolbox zone
            bool anyWeaponAuto = inCombat && _weaponController.HasAnyAutoFireEnabled();
            bool charAuto = inCombat && _charController.IsAutoAssignEnabled;
            if (anyWeaponAuto || charAuto)
            {
                string globalAutoKey = ModConfig.FormatKeyLabel(cfg.GlobalAutoFireToggleKey);
                Rect bannerRect = new Rect(x, y - 26f, boxWidth, 24f);
                string autoParts = anyWeaponAuto && charAuto
                    ? $"🟢 武器[AUTO] + 人物自动派驻中 — 点击此处或按 [{globalAutoKey}] 关闭"
                    : anyWeaponAuto
                        ? $"🟢 武器自动托管中 (随时可按键/鼠标接管) — 点击或按 [{globalAutoKey}] 取消"
                        : $"🟢 人物持续自动分配中 — 点击或按 [Ctrl+{autoCharKey}] 关闭";

                if (GUI.Button(bannerRect, autoParts, _autoCancelBannerStyle))
                {
                    if (anyWeaponAuto)
                    {
                        _weaponController.CancelAllAutoFire("已关闭全部武器【自动托管】");
                    }
                    else if (charAuto)
                    {
                        _charController.ToggleContinuousAutoAssign();
                    }
                }
            }
        }

        private Vector2 GetToolboxAreaAnchorGuiPos(WorldSceneRoot sceneRoot)
        {
            float scaleY = Screen.height / 1080f;
            try
            {
                PlayerToolMgrView ptView = _toolboxController.AnimationController.GetPlayerToolMgrView();
                if (ptView != null && ptView.box_image != null && ptView.box_image.gameObject.activeInHierarchy)
                {
                    Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(sceneRoot.uiCamera, ptView.box_image.transform.position);
                    if (screenPos.x > Screen.width * 0.5f && screenPos.y > 40f && screenPos.y < Screen.height - 40f)
                    {
                        float guiY = (Screen.height - screenPos.y) - (138f * scaleY);
                        return new Vector2(screenPos.x, guiY);
                    }
                }
            }
            catch
            {
            }

            // Default fallback right above the Toolbox / Character 6 area on the right side
            return new Vector2(Screen.width * 0.86f, Screen.height * 0.60f);
        }

        private void DrawDeviceSlotBadges(WorldSceneRoot sceneRoot, ModConfig cfg)
        {
            IReadOnlyList<ResolvedDeviceSlot> slots = _slotResolver.GetOrderedSlots();
            if (slots.Count == 0)
            {
                return;
            }

            Camera uiCam = sceneRoot.uiCamera;
            float now = Time.unscaledTime;

            for (int i = 0; i < slots.Count && i < DeviceSlotResolver.MaxWeaponSlots; i++)
            {
                ResolvedDeviceSlot slot = slots[i];
                if (slot.PanelTransform == null || !slot.PanelTransform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, slot.PanelTransform.position);
                if (screenPos.x <= 20f || screenPos.x >= Screen.width - 20f || screenPos.y < 85f || screenPos.y > Screen.height - 60f)
                {
                    continue;
                }

                float guiY = (Screen.height - screenPos.y) - 95f;
                if (guiY < 56f || guiY > Screen.height - 40f)
                {
                    continue;
                }

                string wKey = i < cfg.WeaponSlotKeys.Length ? ModConfig.FormatKeyLabel(cfg.WeaponSlotKeys[i]) : "?";
                string mKey = i < cfg.SlotMaintainKeys.Length ? ModConfig.FormatKeyLabel(cfg.SlotMaintainKeys[i]) : "?";

                bool isKeyActiveOnSlot =
                    _weaponController.IsSlotHoldingAnyAction(i) ||
                    _toolboxController.IsSlotMaintaining(i) ||
                    (_weaponController.LastTargetSlotIndex == i && now - _weaponController.LastTargetSlotTime < 0.45f) ||
                    (_toolboxController.LastTargetSlotIndex == i && now - _toolboxController.LastTargetSlotTime < 0.45f) ||
                    (_charController.LastTargetSlotIndex == i && now - _charController.LastTargetSlotTime < 0.45f);

                // Fixed empty slot badge
                if (slot.Device == null || slot.UiView == null || !slot.UiView.gameObject.activeInHierarchy)
                {
                    GUIStyle emptyStyle = isKeyActiveOnSlot ? _activeBadgeStyle : _badgeStyle;
                    if (!isKeyActiveOnSlot)
                    {
                        _badgeStyle.normal.textColor = new Color(0.62f, 0.65f, 0.72f);
                    }
                    Rect emptyRect = new Rect(screenPos.x - 56f, guiY, 112f, 22f);
                    string emptyText = isKeyActiveOnSlot ? $"▶ {i + 1}号[{wKey}]:空槽" : $"{i + 1}号[{wKey}] 空槽";
                    GUI.Box(emptyRect, emptyText, emptyStyle);
                    continue;
                }

                bool isAuto = _weaponController.IsSlotAutoFireEnabled(i);
                bool hasEmergency = !slot.Device.is_validate ||
                                    (slot.Device.device_emergency != null && !slot.Device.device_emergency.removed);

                if (isKeyActiveOnSlot)
                {
                    Rect activeRect = new Rect(screenPos.x - 74f, guiY, 148f, 22f);
                    string activeAction = _toolboxController.IsSlotMaintaining(i) || (_toolboxController.LastTargetSlotIndex == i && now - _toolboxController.LastTargetSlotTime < 0.45f)
                        ? $"▶ {i + 1}号 抢修[{mKey}]!"
                        : $"▶ {i + 1}号 操控[{wKey}]!";
                    GUI.Box(activeRect, activeAction, _activeBadgeStyle);
                }
                else if (hasEmergency)
                {
                    _badgeStyle.normal.textColor = new Color(1f, 0.45f, 0.25f);
                    Rect badgeRect = new Rect(screenPos.x - 72f, guiY, 144f, 22f);
                    GUI.Box(badgeRect, $"{i + 1}号[{wKey}] ⚠抢修:[{mKey}]", _badgeStyle);
                }
                else if (isAuto)
                {
                    Rect badgeRect = new Rect(screenPos.x - 88f, guiY, 176f, 22f);
                    string autoLabel = $"🟢{i + 1}号[{wKey}:自动] Ctrl+{wKey}关|修:{mKey}";
                    if (GUI.Button(badgeRect, autoLabel, _autoBadgeButtonStyle))
                    {
                        _weaponController.ToggleSlotAutoFire(i);
                    }
                }
                else
                {
                    _badgeStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);
                    Rect badgeRect = new Rect(screenPos.x - 68f, guiY, 136f, 22f);
                    GUI.Box(badgeRect, $"{i + 1}号[{wKey}] 修:[{mKey}]", _badgeStyle);
                }
            }
        }

        private string GetLatestToastMessage()
        {
            float now = Time.unscaledTime;
            string bestMsg = string.Empty;
            float bestTime = 0f;

            if (!string.IsNullOrEmpty(_systemStatusMessage) && now - _systemStatusTime < 3.5f && _systemStatusTime > bestTime)
            {
                bestMsg = _systemStatusMessage;
                bestTime = _systemStatusTime;
            }

            if (!string.IsNullOrEmpty(_charController.LastActionStatus) && now - _charController.LastActionStatusTime < 3.5f && _charController.LastActionStatusTime > bestTime)
            {
                bestMsg = _charController.LastActionStatus;
                bestTime = _charController.LastActionStatusTime;
            }

            if (!string.IsNullOrEmpty(_weaponController.LastActionStatus) && now - _weaponController.LastActionStatusTime < 3.5f && _weaponController.LastActionStatusTime > bestTime)
            {
                bestMsg = _weaponController.LastActionStatus;
                bestTime = _weaponController.LastActionStatusTime;
            }

            if (!string.IsNullOrEmpty(_toolboxController.LastActionStatus) && now - _toolboxController.LastActionStatusTime < 3.5f && _toolboxController.LastActionStatusTime > bestTime)
            {
                bestMsg = _toolboxController.LastActionStatus;
                bestTime = _toolboxController.LastActionStatusTime;
            }

            if (!string.IsNullOrEmpty(_drivingController.LastActionStatus) && now - _drivingController.LastActionStatusTime < 3.5f && _drivingController.LastActionStatusTime > bestTime)
            {
                bestMsg = _drivingController.LastActionStatus;
                bestTime = _drivingController.LastActionStatusTime;
            }

            return bestMsg;
        }

        private void EnsureGuiStyles()
        {
            if (_darkBgTex == null)
            {
                _darkBgTex = new Texture2D(1, 1);
                _darkBgTex.SetPixel(0, 0, new Color(0.08f, 0.1f, 0.14f, 0.86f));
                _darkBgTex.Apply();
            }

            if (_activeBgTex == null)
            {
                _activeBgTex = new Texture2D(1, 1);
                _activeBgTex.SetPixel(0, 0, new Color(0.32f, 0.24f, 0.06f, 0.94f));
                _activeBgTex.Apply();
            }

            if (_autoBgTex == null)
            {
                _autoBgTex = new Texture2D(1, 1);
                _autoBgTex.SetPixel(0, 0, new Color(0.11f, 0.28f, 0.16f, 0.90f));
                _autoBgTex.Apply();
            }

            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(4, 4, 2, 2)
                };
                _badgeStyle.normal.background = _darkBgTex;
            }

            if (_activeBadgeStyle == null)
            {
                _activeBadgeStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(4, 4, 2, 2)
                };
                _activeBadgeStyle.normal.background = _activeBgTex;
                _activeBadgeStyle.normal.textColor = new Color(1f, 0.92f, 0.35f);
            }

            if (_autoBadgeButtonStyle == null)
            {
                _autoBadgeButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(4, 4, 2, 2)
                };
                _autoBadgeButtonStyle.normal.background = _autoBgTex;
                _autoBadgeButtonStyle.hover.background = _autoBgTex;
                _autoBadgeButtonStyle.active.background = _darkBgTex;
                _autoBadgeButtonStyle.normal.textColor = new Color(0.45f, 1f, 0.55f);
                _autoBadgeButtonStyle.hover.textColor = new Color(1f, 0.95f, 0.4f);
            }

            if (_statusBarStyle == null)
            {
                _statusBarStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(8, 8, 3, 3)
                };
                _statusBarStyle.normal.background = _darkBgTex;
                _statusBarStyle.normal.textColor = new Color(0.95f, 0.92f, 0.72f);
            }

            if (_toastPopupStyle == null)
            {
                _toastPopupStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(8, 8, 3, 3)
                };
                _toastPopupStyle.normal.background = _activeBgTex;
                _toastPopupStyle.normal.textColor = new Color(1f, 0.95f, 0.45f);
            }

            if (_autoCancelBannerStyle == null)
            {
                _autoCancelBannerStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(8, 8, 3, 3)
                };
                _autoCancelBannerStyle.normal.background = _autoBgTex;
                _autoCancelBannerStyle.hover.background = _autoBgTex;
                _autoCancelBannerStyle.active.background = _darkBgTex;
                _autoCancelBannerStyle.normal.textColor = new Color(0.55f, 1f, 0.65f);
                _autoCancelBannerStyle.hover.textColor = new Color(1f, 0.95f, 0.45f);
            }
        }
    }
}
