using System;
using System.Collections.Generic;
using Foundations;
using UnityEngine;
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
    }

    /// <summary>
    /// Resolves active caravan devices in left-to-right visual order (1-6),
    /// matching the bottom UI panels for Q/W/E/R/T/Y and A/S/D/F/G/H.
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

            if (_cachedMgrView != null && _cachedMgrView.device_ui_panels_pos != null)
            {
                List<DeviceUiPanelPos> activePanels = new List<DeviceUiPanelPos>(8);
                foreach (DeviceUiPanelPos panel in _cachedMgrView.device_ui_panels_pos)
                {
                    if (panel != null && panel.device_ui_view != null && panel.device_ui_view.owner != null)
                    {
                        activePanels.Add(panel);
                    }
                }

                activePanels.Sort((a, b) =>
                {
                    float ax = a.pos != null ? a.pos.position.x : 0f;
                    float bx = b.pos != null ? b.pos.position.x : 0f;
                    return ax.CompareTo(bx);
                });

                for (int i = 0; i < activePanels.Count; i++)
                {
                    DeviceUiPanelPos panel = activePanels[i];
                    _slots.Add(new ResolvedDeviceSlot
                    {
                        Index = i,
                        SlotName = panel.slot_name ?? $"Slot_{i + 1}",
                        Device = panel.device_ui_view.owner,
                        UiView = panel.device_ui_view,
                        PanelTransform = panel.pos != null ? panel.pos : panel.device_ui_view.transform
                    });
                }

                if (_slots.Count > 0)
                {
                    return _slots;
                }
            }

            // Fallback to DeviceMgr.slots_device if UI view is not yet ready
            Mission mission = Singleton<Mission>.instance;
            if (mission != null && mission.try_get_mgr("DeviceMgr", out DeviceMgr deviceMgr) && deviceMgr?.slots_device != null)
            {
                List<DeviceSlot> validSlots = new List<DeviceSlot>(8);
                foreach (DeviceSlot ds in deviceMgr.slots_device)
                {
                    if (ds != null && ds.slot_device != null)
                    {
                        validSlots.Add(ds);
                    }
                }

                validSlots.Sort((a, b) => a.slot_device.position.x.CompareTo(b.slot_device.position.x));

                for (int i = 0; i < validSlots.Count; i++)
                {
                    DeviceSlot ds = validSlots[i];
                    _slots.Add(new ResolvedDeviceSlot
                    {
                        Index = i,
                        SlotName = ds._name ?? $"Slot_{i + 1}",
                        Device = ds.slot_device,
                        UiView = null,
                        PanelTransform = null
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
    }
}
