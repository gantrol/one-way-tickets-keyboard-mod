using System;
using System.Collections.Generic;
using Foundations;
using UnityEngine;
using World;
using World.Devices;
using World.Devices.DeviceUiViews;

namespace OneWayTickets.KeyboardMod
{
    public struct ResolvedDeviceSlot
    {
        public int Index;
        public string SlotName;
        public Device Device;
        public DeviceUiView UiView;
        public Transform PanelTransform;
        public bool IsEmpty => Device == null;
    }

    /// <summary>
    /// Resolves all fixed caravan device slots (1-6) in left-to-right visual order,
    /// so Q/W/E/R/T/Y and A/S/D/F/G/H always map to fixed physical slot indices
    /// even when some slots are empty.
    /// </summary>
    public sealed class DeviceSlotResolver
    {
        private DeviceMgrView _cachedMgrView;
        private float _lastSearchTime;
        private readonly List<ResolvedDeviceSlot> _slots = new List<ResolvedDeviceSlot>(8);

        public IReadOnlyList<ResolvedDeviceSlot> GetOrderedSlots()
        {
            _slots.Clear();

            if (_cachedMgrView == null && Time.unscaledTime - _lastSearchTime > 0.5f)
            {
                _lastSearchTime = Time.unscaledTime;
                _cachedMgrView = UnityEngine.Object.FindObjectOfType<DeviceMgrView>();
            }

            Mission mission = Singleton<Mission>.instance;
            DeviceMgr deviceMgr = null;
            if (mission != null)
            {
                mission.try_get_mgr("DeviceMgr", out deviceMgr);
            }

            // 1. Primary path: Use DeviceMgr.slots_device (all fixed slots defined on the current caravan)
            //    paired with DeviceMgrView.device_ui_panels_pos for exact left-to-right UI ordering.
            if (deviceMgr?.slots_device != null && deviceMgr.slots_device.Count > 0)
            {
                List<DeviceSlot> fixedSlots = new List<DeviceSlot>(deviceMgr.slots_device.Count);
                foreach (DeviceSlot ds in deviceMgr.slots_device)
                {
                    if (ds != null && !string.IsNullOrEmpty(ds._name))
                    {
                        fixedSlots.Add(ds);
                    }
                }

                fixedSlots.Sort((a, b) => CompareFixedSlotsLeftToRight(a, b));

                for (int i = 0; i < fixedSlots.Count; i++)
                {
                    DeviceSlot ds = fixedSlots[i];
                    DeviceUiPanelPos panel = FindUiPanelForSlot(ds._name);
                    Device device = ds.slot_device ?? (panel?.device_ui_view != null ? panel.device_ui_view.owner : null);
                    DeviceUiView uiView = panel?.device_ui_view;
                    Transform panelTransform = GetPanelTransform(panel, uiView);

                    _slots.Add(new ResolvedDeviceSlot
                    {
                        Index = i,
                        SlotName = ds._name,
                        Device = device,
                        UiView = uiView,
                        PanelTransform = panelTransform
                    });
                }

                return _slots;
            }

            // 2. Fallback path: If DeviceMgr isn't ready yet, inspect active/wood_mask panels in DeviceMgrView
            if (_cachedMgrView != null && _cachedMgrView.device_ui_panels_pos != null)
            {
                List<DeviceUiPanelPos> panels = new List<DeviceUiPanelPos>(8);
                foreach (DeviceUiPanelPos panel in _cachedMgrView.device_ui_panels_pos)
                {
                    if (panel == null)
                    {
                        continue;
                    }

                    bool hasDevice = panel.device_ui_view != null && panel.device_ui_view.owner != null;
                    bool hasWoodMask = panel.wood_mask != null && panel.wood_mask.activeInHierarchy;
                    if (hasDevice || hasWoodMask)
                    {
                        panels.Add(panel);
                    }
                }

                panels.Sort((a, b) => GetPanelOrderX(a).CompareTo(GetPanelOrderX(b)));

                for (int i = 0; i < panels.Count; i++)
                {
                    DeviceUiPanelPos panel = panels[i];
                    DeviceUiView uiView = panel.device_ui_view;
                    _slots.Add(new ResolvedDeviceSlot
                    {
                        Index = i,
                        SlotName = panel.slot_name ?? $"Slot_{i + 1}",
                        Device = uiView != null ? uiView.owner : null,
                        UiView = uiView,
                        PanelTransform = GetPanelTransform(panel, uiView)
                    });
                }
            }

            return _slots;
        }

        public bool TryGetSlot(int index, out ResolvedDeviceSlot slot)
        {
            IReadOnlyList<ResolvedDeviceSlot> list = GetOrderedSlots();
            if (index >= 0 && index < list.Count)
            {
                slot = list[index];
                return true;
            }
            slot = default;
            return false;
        }

        private int CompareFixedSlotsLeftToRight(DeviceSlot a, DeviceSlot b)
        {
            DeviceUiPanelPos panelA = FindUiPanelForSlot(a._name);
            DeviceUiPanelPos panelB = FindUiPanelForSlot(b._name);

            if (panelA != null && panelB != null)
            {
                float pxA = GetPanelOrderX(panelA);
                float pxB = GetPanelOrderX(panelB);
                int cmp = pxA.CompareTo(pxB);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            WorldContext wctx = Singleton<WorldContext>.instance;
            if (wctx?.caravan_slots != null)
            {
                bool hasPosA = wctx.caravan_slots.TryGetValue(a._name, out Vector3 posA);
                bool hasPosB = wctx.caravan_slots.TryGetValue(b._name, out Vector3 posB);
                if (hasPosA && hasPosB)
                {
                    int cmp = posA.x.CompareTo(posB.x);
                    if (cmp != 0)
                    {
                        return cmp;
                    }
                }
            }

            return string.Compare(a._name, b._name, StringComparison.Ordinal);
        }

        private DeviceUiPanelPos FindUiPanelForSlot(string slotName)
        {
            if (_cachedMgrView == null || _cachedMgrView.device_ui_panels_pos == null || string.IsNullOrEmpty(slotName))
            {
                return null;
            }

            List<DeviceUiPanelPos> panels = _cachedMgrView.device_ui_panels_pos;
            for (int i = 0; i < panels.Count; i++)
            {
                DeviceUiPanelPos p = panels[i];
                if (p != null && p.slot_name == slotName)
                {
                    return p;
                }
            }

            return null;
        }

        private static float GetPanelOrderX(DeviceUiPanelPos panel)
        {
            if (panel == null)
            {
                return 0f;
            }

            if (panel.pos != null)
            {
                return panel.pos.position.x;
            }

            if (panel.special_pos != null)
            {
                return panel.special_pos.position.x;
            }

            if (panel.device_ui_view != null)
            {
                return panel.device_ui_view.transform.position.x;
            }

            return 0f;
        }

        private static Transform GetPanelTransform(DeviceUiPanelPos panel, DeviceUiView uiView)
        {
            if (uiView != null)
            {
                return uiView.transform;
            }

            if (panel != null)
            {
                if (panel.pos != null && panel.pos.gameObject.activeInHierarchy)
                {
                    return panel.pos;
                }
                if (panel.special_pos != null && panel.special_pos.gameObject.activeInHierarchy)
                {
                    return panel.special_pos;
                }
                if (panel.wood_mask != null && panel.wood_mask.activeInHierarchy)
                {
                    return panel.wood_mask.transform;
                }
                if (panel.pos != null)
                {
                    return panel.pos;
                }
            }

            return null;
        }
    }
}
