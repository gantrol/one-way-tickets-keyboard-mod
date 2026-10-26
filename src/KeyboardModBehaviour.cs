using System;
using System.Collections.Generic;
using Foundations;
using Foundations.Tickers;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.Characters;
using World.Helpers;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Persistent MonoBehaviour that coordinates keyboard input (via Unity InputSystem),
    /// 120 TPS deterministic simulation ticks, and the toggleable HUD shortcut overlay.
    /// </summary>
    public sealed class KeyboardModBehaviour : MonoBehaviour
    {
        private const float TickInterval = 1f / 120f;

        private DeviceSlotResolver _slotResolver;
        private KeyboardCharacterController _charController;
        private KeyboardWeaponController _weaponController;
        private KeyboardToolboxController _toolboxController;

        private float _tickAccumulator;
        private GUIStyle _badgeStyle;
        private GUIStyle _autoBadgeButtonStyle;
        private GUIStyle _statusBarStyle;
        private GUIStyle _autoCancelBannerStyle;
        private Texture2D _darkBgTex;
        private Texture2D _autoBgTex;

        private string _systemStatusMessage = string.Empty;
        private float _systemStatusTime;

        private void Awake()
        {
            _slotResolver = new DeviceSlotResolver();
            _charController = new KeyboardCharacterController(_slotResolver);
            _weaponController = new KeyboardWeaponController(_slotResolver, _charController);
            _toolboxController = new KeyboardToolboxController(_slotResolver);
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
                // 1. Character input (runs first so 1..6 -> Q..Y dispatch can claim the Q..Y press)
                _charController.UpdateInput(kb);

                // 2. Weapon bar input (Q..Y, Shift+Q..Y, Alt+Q..Y, Ctrl+Q..Y, Tab)
                _weaponController.UpdateInput(kb);

                // 3. Toolbox input (A..H, Z/X/C/V/B, Space)
                _toolboxController.UpdateInput(kb);

                // 4. Run 120 TPS simulation steps when game ticker is active and unpaused
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
                        _weaponController.Tick120();
                        _toolboxController.Tick120();
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

            // In non-combat scenes (safe area / campfire / settlement), hide all combat HUD badges and status bars
            // unless the user just pressed F9/F10 to trigger a system toast.
            if (!inCombat)
            {
                if (!string.IsNullOrEmpty(activeToast))
                {
                    EnsureGuiStyles();
                    DrawTopStatusBar(cfg, activeToast, false);
                }
                return;
            }

            EnsureGuiStyles();

            DrawTopStatusBar(cfg, activeToast, true);
            DrawDeviceSlotBadges(sceneRoot, cfg);
        }

        private void DrawTopStatusBar(ModConfig cfg, string activeToast, bool inCombat)
        {
            Character selectedChar = inCombat ? Cubicle_Helper.GetSelectCharacter() : null;
            string charHint = selectedChar != null ? " 【已选中角色：按 Q-Y 派驻工位 / 按 0 撤回】" : string.Empty;
            string smartHint = (inCombat && _toolboxController.IsGlobalSmartMaintaining) ? " 【空格：全局抢修中】" : string.Empty;

            string barText = string.IsNullOrEmpty(activeToast)
                ? $"[键盘Mod] 1-6:选人派驻 | Q-Y:武器(Shift副/Alt三/Ctrl托管/Tab全托管) | A-H:抢修 | ZXCVB:工具 | Space:全修 | F9:隐藏{charHint}{smartHint}"
                : $"[键盘Mod] {activeToast}{charHint}{smartHint}";

            // Center horizontally so we never overlap the top-left Relic bar or top-right Caravan HP bar
            float maxWidth = Mathf.Max(420f, Screen.width - 680f);
            float width = Mathf.Min(maxWidth, 980f);
            float x = (Screen.width - width) * 0.5f;
            Rect barRect = new Rect(x, 6f, width, 24f);
            GUI.Box(barRect, barText, _statusBarStyle);

            // Prominent interactive banner when any weapon has [AUTO] enabled
            if (inCombat && _weaponController.HasAnyAutoFireEnabled())
            {
                float bannerWidth = Mathf.Min(width, 760f);
                float bannerX = (Screen.width - bannerWidth) * 0.5f;
                Rect bannerRect = new Rect(bannerX, 32f, bannerWidth, 24f);
                string bannerText = "🟢 自动托管运行中 — 随时可按键/鼠标手动操控武器 | 按 [Tab] 或点击此处一键取消全托管 (Ctrl+Q~Y 单槽取消)";
                if (GUI.Button(bannerRect, bannerText, _autoCancelBannerStyle))
                {
                    _weaponController.CancelAllAutoFire("已通过顶部提示栏关闭全部武器【自动托管】");
                }
            }
        }

        private void DrawDeviceSlotBadges(WorldSceneRoot sceneRoot, ModConfig cfg)
        {
            IReadOnlyList<ResolvedDeviceSlot> slots = _slotResolver.GetOrderedSlots();
            if (slots.Count == 0)
            {
                return;
            }

            Camera uiCam = sceneRoot.uiCamera;

            for (int i = 0; i < slots.Count && i < 6; i++)
            {
                ResolvedDeviceSlot slot = slots[i];
                if (slot.Device == null || slot.PanelTransform == null || slot.UiView == null)
                {
                    continue;
                }

                if (!slot.UiView.gameObject.activeInHierarchy || !slot.PanelTransform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, slot.PanelTransform.position);
                // Do not draw or clamp when the weapon panel is slid off-screen (e.g., y < 85px from bottom)
                if (screenPos.x <= 20f || screenPos.x >= Screen.width - 20f || screenPos.y < 85f || screenPos.y > Screen.height - 60f)
                {
                    continue;
                }

                // Convert Unity screen space (bottom-left origin) to GUI space (top-left origin)
                float guiY = (Screen.height - screenPos.y) - 95f;
                if (guiY < 56f || guiY > Screen.height - 40f)
                {
                    continue;
                }

                string wKey = i < cfg.WeaponSlotKeys.Length ? ModConfig.FormatKeyLabel(cfg.WeaponSlotKeys[i]) : "?";
                string mKey = i < cfg.SlotMaintainKeys.Length ? ModConfig.FormatKeyLabel(cfg.SlotMaintainKeys[i]) : "?";

                bool isAuto = _weaponController.IsSlotAutoFireEnabled(i);
                bool hasEmergency = !slot.Device.is_validate ||
                                    (slot.Device.device_emergency != null && !slot.Device.device_emergency.removed);

                if (hasEmergency)
                {
                    _badgeStyle.normal.textColor = new Color(1f, 0.45f, 0.25f);
                    Rect badgeRect = new Rect(screenPos.x - 68f, guiY, 136f, 22f);
                    GUI.Box(badgeRect, $"[{wKey}] ⚠抢修:[{mKey}]", _badgeStyle);
                }
                else if (isAuto)
                {
                    Rect badgeRect = new Rect(screenPos.x - 86f, guiY, 172f, 22f);
                    string autoLabel = $"🟢[{wKey}:自动] Ctrl+{wKey}关 | 修:{mKey}";
                    if (GUI.Button(badgeRect, autoLabel, _autoBadgeButtonStyle))
                    {
                        _weaponController.ToggleSlotAutoFire(i);
                    }
                }
                else
                {
                    _badgeStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);
                    Rect badgeRect = new Rect(screenPos.x - 62f, guiY, 124f, 22f);
                    GUI.Box(badgeRect, $"[{wKey}] 修:[{mKey}]", _badgeStyle);
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

            return bestMsg;
        }

        private void EnsureGuiStyles()
        {
            if (_darkBgTex == null)
            {
                _darkBgTex = new Texture2D(1, 1);
                _darkBgTex.SetPixel(0, 0, new Color(0.08f, 0.1f, 0.14f, 0.84f));
                _darkBgTex.Apply();
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
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(10, 10, 3, 3)
                };
                _statusBarStyle.normal.background = _darkBgTex;
                _statusBarStyle.normal.textColor = new Color(0.95f, 0.92f, 0.72f);
            }

            if (_autoCancelBannerStyle == null)
            {
                _autoCancelBannerStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(10, 10, 3, 3)
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
