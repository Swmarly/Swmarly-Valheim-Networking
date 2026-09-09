using System;
using System.IO;
using HarmonyLib;

namespace ValheimTune.Patches
{
    [HarmonyPatch(typeof(ZRpc), nameof(ZRpc.Update))]
    public static class ReceiveCapPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ZRpc __instance, float dt, ref ZRpc.ErrorCode __result)
        {
            if (__instance == null || __instance.m_socket == null)
            {
                __result = ZRpc.ErrorCode.Disconnected;
                return false;
            }

            int cap = Cfg.MaxPacketsPerPeerPerFrame.Value;
            if (!Compat.ReplacementsAllowed || cap <= 0) return true;
            cap = Math.Min(cap, 4096);

            try
            {
                if (!__instance.m_socket.IsConnected())
                {
                    __result = ZRpc.ErrorCode.Disconnected;
                    return false;
                }
                for (int n = 0; n < cap; n++)
                {
                    ZPackage pkg = __instance.m_socket.Recv();
                    if (pkg == null) break;
                    __instance.m_recvPackages++;
                    __instance.m_recvData += pkg.Size();
                    try
                    {
                        __instance.HandlePackage(pkg);
                    }
                    catch (EndOfStreamException ex)
                    {
                        ZLog.LogError("EndOfStreamException in ZRpc::HandlePackage: Assume incompatible version: " + ex.Message);
                        __result = ZRpc.ErrorCode.IncompatibleVersion;
                        return false;
                    }
                    catch (Exception ex2)
                    {
                        ZLog.Log("Exception in ZRpc::HandlePackage: " + ex2);
                    }
                }
                __instance.UpdatePing(dt);
                __result = ZRpc.ErrorCode.Success;
                return false;
            }
            catch (Exception ex)
            {
                ZLog.LogError("Exception in receive-cap path: " + ex);
                __result = ZRpc.ErrorCode.Disconnected;
                return false;
            }
        }
    }
}
