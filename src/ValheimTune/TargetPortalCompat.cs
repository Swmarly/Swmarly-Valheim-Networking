using System;
using BepInEx;
using BepInEx.Logging;

namespace ValheimTune
{
    /// <summary>
    /// Compatibility gate for TargetPortal. TargetPortal uses ZDOMan.ForceSendZDO to advertise
    /// portal records outside a peer's active area and relies on vanilla CreateSyncList to remove
    /// a player from the old area after an arbitrary portal teleport. Our dirty-set replacement
    /// deliberately bypasses that method, so it must stay inactive while TargetPortal is loaded.
    /// </summary>
    internal static class TargetPortalCompat
    {
        internal const string PluginGuid = "org.bepinex.plugins.targetportal";
        internal static bool IsLoaded { get; private set; }

        internal static void Refresh(ManualLogSource log)
        {
            bool loaded = false;
            try
            {
                foreach (var kv in Bootstrap.Chainloader.PluginInfos)
                {
                    if (kv.Key != null && kv.Key.Equals(PluginGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        loaded = true;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                log?.LogWarning("[TargetPortal] could not inspect loaded plugins: " + e.Message);
            }

            IsLoaded = loaded;
            if (loaded)
            {
                log?.LogInfo("[TargetPortal] detected; using vanilla ZDO sync-list handling for portal compatibility");
            }
        }
    }
}
