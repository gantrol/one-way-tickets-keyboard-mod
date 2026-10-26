using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using World;
using World.Characters;
using World.Player_Tool;
using World.Player_Tool.ToolsLogic;
using World.Player_Tool.ToolsLogic.Rags;
using World.Player_Tool.ToolsLogic.Saws;
using World.Player_Tool.ToolsLogic.WakeUp;

namespace OneWayTickets.KeyboardMod
{
    public enum ToolAnimCategory
    {
        None = 0,
        Wrench = 1,
        Extinguisher = 2,
        Saw = 3,
        Rag = 4,
        WakeUp = 5
    }

    /// <summary>
    /// Renders a non-intrusive visual animation that pulls the active tool out of the Toolbox,
    /// animates it working on the target device/character, and puts it back into the Toolbox.
    /// All operations are wrapped in try-catch blocks so version changes in UI prefabs never affect gameplay.
    /// </summary>
    public sealed class ToolAnimationController
    {
        private enum AnimPhase
        {
            Idle = 0,
            PullingOut = 1,
            Working = 2,
            Returning = 3
        }

        private readonly DeviceSlotResolver _slotResolver;

        private static readonly MethodInfo _insToolsViewMethod = typeof(PlayerToolMgrView).GetMethod(
            "ins_tools_view",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly MethodInfo _setBoxImageMethod = typeof(PlayerToolMgrView).GetMethod(
            "set_box_image",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo _isOpenField = typeof(PlayerToolMgrView).GetField(
            "is_open",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private PlayerToolMgrView _cachedPtView;
        private float _lastPtViewLookupTime;

        private GameObject _visualGo;
        private RectTransform _visualRect;
        private Image _visualImage;

        private AnimPhase _phase = AnimPhase.Idle;
        private ToolAnimCategory _activeCategory = ToolAnimCategory.None;
        private bool _isDrierSweep;
        private int _targetSlotIndex = -1;
        private float _phaseElapsed;
        private float _holdRemainSeconds;

        private Vector3 _startWorldPos;
        private Vector3 _targetWorldPos;
        private Image _dimmedTrayImage;
        private bool _openedBoxSpriteTemporarily;

        private const float PullOutDuration = 0.11f;
        private const float ReturnDuration = 0.14f;

        public ToolAnimationController(DeviceSlotResolver slotResolver)
        {
            _slotResolver = slotResolver;
        }

        public void TriggerToolUse(ToolAnimCategory category, int targetSlotIndex = -1, bool isGlobalSweep = false, float holdDuration = 0.24f)
        {
            if (!ModConfig.Instance.EnableToolPullOutAnimation || category == ToolAnimCategory.None)
            {
                return;
            }

            try
            {
                PlayerToolMgrView ptView = GetPlayerToolMgrView();
                if (ptView == null || !ptView.gameObject.activeInHierarchy)
                {
                    return;
                }

                ToolLogic logic = FindToolLogic(ptView, category);
                Sprite toolSprite = GetBestToolSprite(logic);
                if (toolSprite == null)
                {
                    return;
                }

                if (!EnsureVisualObject(ptView))
                {
                    return;
                }

                _visualImage.sprite = toolSprite;
                CopyToolSize(logic);

                _startWorldPos = GetToolboxWorldPosition(ptView);
                _targetWorldPos = ResolveTargetWorldPosition(ptView, category, targetSlotIndex);
                _targetSlotIndex = targetSlotIndex;
                _isDrierSweep = isGlobalSweep;
                _holdRemainSeconds = Mathf.Max(_holdRemainSeconds, holdDuration);

                if (_activeCategory != category)
                {
                    RestoreDimmedTrayImage();
                    _activeCategory = category;
                    DimTrayImageIfOpen(ptView, logic);
                }

                OpenToolboxLidTemporarily(ptView);

                if (_phase == AnimPhase.Idle || _phase == AnimPhase.Returning)
                {
                    _phase = AnimPhase.PullingOut;
                    _phaseElapsed = 0f;
                    _visualGo.SetActive(true);
                    _visualRect.SetAsLastSibling();
                }
            }
            catch
            {
                // Safely ignore any visual animation error so tool functionality never breaks
            }
        }

        public void UpdateAnimation(float unscaledDeltaTime)
        {
            if (_phase == AnimPhase.Idle)
            {
                return;
            }

            try
            {
                if (!ModConfig.Instance.EnableToolPullOutAnimation || _visualGo == null || _visualRect == null)
                {
                    FinishAndReset();
                    return;
                }

                PlayerToolMgrView ptView = GetPlayerToolMgrView();
                if (ptView != null && ptView.gameObject.activeInHierarchy)
                {
                    _startWorldPos = GetToolboxWorldPosition(ptView);
                    if (!_isDrierSweep)
                    {
                        _targetWorldPos = ResolveTargetWorldPosition(ptView, _activeCategory, _targetSlotIndex);
                    }
                }

                _phaseElapsed += unscaledDeltaTime;

                switch (_phase)
                {
                    case AnimPhase.PullingOut:
                    {
                        float t = Mathf.Clamp01(_phaseElapsed / PullOutDuration);
                        float smoothT = t * t * (3f - 2f * t);
                        Vector3 pos = Vector3.Lerp(_startWorldPos, _targetWorldPos, smoothT);
                        pos.y += Mathf.Sin(t * Mathf.PI) * 0.45f;
                        _visualRect.position = pos;

                        float scale = Mathf.Lerp(0.55f, 1.08f, smoothT);
                        _visualRect.localScale = new Vector3(scale, scale, 1f);
                        _visualRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-25f, 0f, smoothT));

                        if (t >= 1f)
                        {
                            _phase = AnimPhase.Working;
                            _phaseElapsed = 0f;
                        }
                        break;
                    }

                    case AnimPhase.Working:
                    {
                        _holdRemainSeconds -= unscaledDeltaTime;
                        ApplyWorkingMotion(_phaseElapsed);

                        if (_holdRemainSeconds <= 0f)
                        {
                            _phase = AnimPhase.Returning;
                            _phaseElapsed = 0f;
                        }
                        break;
                    }

                    case AnimPhase.Returning:
                    {
                        float t = Mathf.Clamp01(_phaseElapsed / ReturnDuration);
                        float smoothT = t * t * (3f - 2f * t);
                        Vector3 pos = Vector3.Lerp(_targetWorldPos, _startWorldPos, smoothT);
                        pos.y += Mathf.Sin(t * Mathf.PI) * 0.35f;
                        _visualRect.position = pos;

                        float scale = Mathf.Lerp(1.0f, 0.45f, smoothT);
                        _visualRect.localScale = new Vector3(scale, scale, 1f);
                        _visualRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 20f, smoothT));

                        if (t >= 1f)
                        {
                            FinishAndReset();
                        }
                        break;
                    }
                }
            }
            catch
            {
                FinishAndReset();
            }
        }

        private void ApplyWorkingMotion(float workTime)
        {
            Vector3 basePos = _targetWorldPos;
            float rotZ = 0f;
            Vector3 offset = Vector3.zero;

            if (_isDrierSweep)
            {
                offset.x = Mathf.Sin(workTime * 18f) * 1.6f;
                offset.y = Mathf.Cos(workTime * 36f) * 0.15f;
                rotZ = Mathf.Sin(workTime * 18f) * 15f;
            }
            else
            {
                switch (_activeCategory)
                {
                    case ToolAnimCategory.Rag:
                        offset.x = Mathf.Cos(workTime * 28f) * 0.32f;
                        offset.y = Mathf.Sin(workTime * 28f) * 0.24f;
                        rotZ = Mathf.Sin(workTime * 28f) * 12f;
                        break;

                    case ToolAnimCategory.Saw:
                        offset.x = Mathf.Sin(workTime * 38f) * 0.35f;
                        offset.y = -Mathf.Sin(workTime * 38f) * 0.18f;
                        rotZ = -18f + Mathf.Sin(workTime * 38f) * 8f;
                        break;

                    case ToolAnimCategory.Wrench:
                        rotZ = Mathf.Sin(workTime * 26f) * 32f;
                        offset.y = Mathf.Abs(Mathf.Sin(workTime * 26f)) * 0.12f;
                        break;

                    case ToolAnimCategory.Extinguisher:
                        rotZ = -15f + Mathf.Sin(workTime * 45f) * 6f;
                        offset.x = Mathf.Sin(workTime * 22f) * 0.22f;
                        break;

                    case ToolAnimCategory.WakeUp:
                        rotZ = Mathf.Sin(workTime * 30f) * 28f;
                        offset.y = Mathf.Abs(Mathf.Cos(workTime * 30f)) * 0.28f;
                        break;
                }
            }

            _visualRect.position = basePos + offset;
            _visualRect.localScale = Vector3.one;
            _visualRect.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        }

        private void FinishAndReset()
        {
            _phase = AnimPhase.Idle;
            _activeCategory = ToolAnimCategory.None;
            _holdRemainSeconds = 0f;
            _isDrierSweep = false;

            try
            {
                if (_visualGo != null)
                {
                    _visualGo.SetActive(false);
                }
                RestoreDimmedTrayImage();
                RestoreToolboxLid();
            }
            catch
            {
            }
        }

        private bool EnsureVisualObject(PlayerToolMgrView ptView)
        {
            if (_visualGo != null && _visualRect != null && _visualImage != null)
            {
                return true;
            }

            Transform canvasParent = null;
            if (ptView.pick_transform != null && ptView.pick_transform.parent != null)
            {
                canvasParent = ptView.pick_transform.parent;
            }
            else if (ptView.transform != null && ptView.transform.parent != null)
            {
                canvasParent = ptView.transform.parent;
            }

            if (canvasParent == null)
            {
                return false;
            }

            _visualGo = new GameObject("KeyboardMod_PulledToolVisual");
            _visualRect = _visualGo.AddComponent<RectTransform>();
            _visualRect.SetParent(canvasParent, false);
            _visualRect.sizeDelta = new Vector2(90f, 90f);

            _visualImage = _visualGo.AddComponent<Image>();
            _visualImage.raycastTarget = false;
            _visualImage.preserveAspect = true;

            _visualGo.SetActive(false);
            return true;
        }

        private void CopyToolSize(ToolLogic logic)
        {
            if (logic == null || _visualRect == null)
            {
                return;
            }

            RectTransform srcRect = logic.tool_image != null
                ? logic.tool_image.rectTransform
                : logic.GetComponent<RectTransform>();

            if (srcRect != null && srcRect.rect.width > 10f && srcRect.rect.height > 10f)
            {
                _visualRect.sizeDelta = new Vector2(srcRect.rect.width, srcRect.rect.height);
            }
            else
            {
                _visualRect.sizeDelta = new Vector2(92f, 92f);
            }
        }

        private static ToolLogic FindToolLogic(PlayerToolMgrView ptView, ToolAnimCategory category)
        {
            if (ptView == null || ptView.tool_view_list == null)
            {
                return null;
            }

            bool anyLogicLoaded = false;
            foreach (ToolView tvCheck in ptView.tool_view_list)
            {
                if (tvCheck != null && tvCheck.logic != null)
                {
                    anyLogicLoaded = true;
                    break;
                }
            }
            if (!anyLogicLoaded && ptView.owner != null && ptView.owner.tools != null && ptView.owner.tools.Count > 0)
            {
                try { _insToolsViewMethod?.Invoke(ptView, null); } catch { }
            }

            foreach (ToolView tv in ptView.tool_view_list)
            {
                if (tv == null || tv.logic == null)
                {
                    continue;
                }

                ToolLogic l = tv.logic;
                switch (category)
                {
                    case ToolAnimCategory.Wrench:
                        if (l is FixWrench) return l;
                        break;
                    case ToolAnimCategory.Extinguisher:
                        if (l is Extinguisher) return l;
                        break;
                    case ToolAnimCategory.Saw:
                        if (l is Saw || l is Scissors || l is ChainSaw || l is CircleSaw) return l;
                        break;
                    case ToolAnimCategory.Rag:
                        if (l is Rag || l is BigRag || l is Mop || l is Drier) return l;
                        break;
                    case ToolAnimCategory.WakeUp:
                        if (l is InflatingHammer || l is ScreamingChicken || l is Clock || l is Tweeter || l is AED) return l;
                        break;
                }
            }

            return null;
        }

        private static Sprite GetBestToolSprite(ToolLogic logic)
        {
            if (logic == null)
            {
                return null;
            }
            if (logic.press_tool != null) return logic.press_tool;
            if (logic.pick_tool != null) return logic.pick_tool;
            if (logic.normal_tool != null) return logic.normal_tool;
            if (logic.tool_image != null && logic.tool_image.sprite != null) return logic.tool_image.sprite;
            return null;
        }

        private static Vector3 GetToolboxWorldPosition(PlayerToolMgrView ptView)
        {
            if (ptView.box_image != null)
            {
                return ptView.box_image.transform.position;
            }
            return ptView.transform.position;
        }

        private Vector3 ResolveTargetWorldPosition(PlayerToolMgrView ptView, ToolAnimCategory category, int slotIndex)
        {
            if (slotIndex >= 0 && _slotResolver.TryGetSlot(slotIndex, out ResolvedDeviceSlot slot) && slot.PanelTransform != null)
            {
                return slot.PanelTransform.position;
            }

            if (category == ToolAnimCategory.WakeUp)
            {
                // Position near the character bar above slot 3/4
                if (_slotResolver.TryGetSlot(2, out ResolvedDeviceSlot midSlot) && midSlot.PanelTransform != null)
                {
                    return midSlot.PanelTransform.position + new Vector3(0f, 1.35f, 0f);
                }
            }

            // Fallback to center weapon slot
            if (_slotResolver.TryGetSlot(2, out ResolvedDeviceSlot centerSlot) && centerSlot.PanelTransform != null)
            {
                return centerSlot.PanelTransform.position;
            }

            return GetToolboxWorldPosition(ptView) + new Vector3(-2.5f, 0.5f, 0f);
        }

        private void OpenToolboxLidTemporarily(PlayerToolMgrView ptView)
        {
            try
            {
                bool isOpen = false;
                if (_isOpenField != null && _isOpenField.GetValue(ptView) is bool b)
                {
                    isOpen = b;
                }

                if (ptView != null && !isOpen && ptView.box_image != null && ptView.box_sprites != null && ptView.box_sprites.Length > 1 && ptView.box_sprites[1] != null)
                {
                    ptView.box_image.sprite = ptView.box_sprites[1];
                    _openedBoxSpriteTemporarily = true;
                }
            }
            catch
            {
            }
        }

        private void RestoreToolboxLid()
        {
            if (!_openedBoxSpriteTemporarily)
            {
                return;
            }
            _openedBoxSpriteTemporarily = false;

            try
            {
                PlayerToolMgrView ptView = GetPlayerToolMgrView();
                if (ptView != null)
                {
                    _setBoxImageMethod?.Invoke(ptView, null);
                }
            }
            catch
            {
            }
        }

        private void DimTrayImageIfOpen(PlayerToolMgrView ptView, ToolLogic logic)
        {
            try
            {
                if (ptView != null && logic != null && logic.tool_image != null)
                {
                    _dimmedTrayImage = logic.tool_image;
                    _dimmedTrayImage.color = new Color(1f, 1f, 1f, 0.25f);
                }
            }
            catch
            {
            }
        }

        private void RestoreDimmedTrayImage()
        {
            try
            {
                if (_dimmedTrayImage != null)
                {
                    _dimmedTrayImage.color = Color.white;
                    _dimmedTrayImage = null;
                }
            }
            catch
            {
                _dimmedTrayImage = null;
            }
        }

        public PlayerToolMgrView GetPlayerToolMgrView()
        {
            if (_cachedPtView != null && _cachedPtView.gameObject != null && _cachedPtView.gameObject.activeInHierarchy)
            {
                return _cachedPtView;
            }

            float now = Time.unscaledTime;
            if (now - _lastPtViewLookupTime < 1.0f)
            {
                return _cachedPtView;
            }

            _lastPtViewLookupTime = now;
            _cachedPtView = UnityEngine.Object.FindObjectOfType<PlayerToolMgrView>();
            return _cachedPtView;
        }
    }
}
