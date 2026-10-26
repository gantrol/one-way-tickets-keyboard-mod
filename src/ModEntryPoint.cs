using System;
using UnityEngine;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// Entry point called by the BepInEx plugin on Unity's main thread.
    /// Initializes the persistent KeyboardModBehaviour GameObject once per session.
    /// </summary>
    public static class ModEntryPoint
    {
        private static bool _initialized;
        private static GameObject _modRootObject;

        public static void EnsureInitialized()
        {
            if (_initialized && _modRootObject != null)
            {
                return;
            }

            try
            {
                _initialized = true;
                ModConfig.Load();

                _modRootObject = new GameObject("OneWayTickets_KeyboardMod_Root");
                UnityEngine.Object.DontDestroyOnLoad(_modRootObject);
                _modRootObject.hideFlags = HideFlags.HideAndDontSave;
                _modRootObject.AddComponent<KeyboardModBehaviour>();

                Debug.Log("[KeyboardMod] OneWayTickets.KeyboardMod initialized successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KeyboardMod] Initialization error: {ex}");
            }
        }
    }
}
