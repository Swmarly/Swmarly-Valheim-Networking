using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SmoothServer
{
    internal static class PatchGuard
    {
        internal static void RequireExclusive(MethodBase target, string seam)
        {
            if (target == null) throw new ArgumentNullException("target");
            var info = Harmony.GetPatchInfo(target);
            if (info == null) return;
            var foreign = new List<string>();
            foreach (string owner in info.Owners)
            {
                if (string.IsNullOrEmpty(owner)) continue;
                if (owner.Equals(SmoothServerPlugin.PluginGuid, StringComparison.OrdinalIgnoreCase) ||
                    owner.StartsWith(SmoothServerPlugin.PluginGuid + ".", StringComparison.OrdinalIgnoreCase)) continue;
                foreign.Add(owner);
            }
            if (foreign.Count > 0)
                throw new Exception(seam + " has foreign Harmony owner(s): " +
                    string.Join(", ", foreign.ToArray()) + " - refusing to compose replacement patches");
        }
    }
}