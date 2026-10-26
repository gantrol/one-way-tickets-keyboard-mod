using BepInEx;

namespace OneWayTickets.KeyboardMod
{
    /// <summary>
    /// BepInEx creates this component on Unity's main thread. No on-disk game
    /// assembly patches are needed to initialize the keyboard controls.
    /// </summary>
    [BepInPlugin("com.gantrol.onewaytickets.keyboardmod", "One Way Tickets Keyboard Mod", "1.1.0")]
    [BepInProcess("One Way Tickets.exe")]
    public sealed class KeyboardModPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            ModEntryPoint.EnsureInitialized();
        }
    }
}
