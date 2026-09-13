using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimTune
{
    /// <summary>
    /// Keeps TargetPortal's forced portal advertisements on a vanilla CreateSyncList pass,
    /// while allowing ordinary dirty-set rounds to remain optimized. The ForceSendZDO hooks
    /// are discovered and asserted at runtime so an internal API change fails closed for the
    /// portal-aware path instead of silently losing portal records.
    /// </summary>
    internal static class TargetPortalCompat
    {
        internal const string PluginGuid = "org.bepinex.plugins.targetportal";
        internal static bool IsLoaded { get; private set; }
        internal static bool ForceHooksInstalled { get; private set; }

        private static readonly object s_gate = new object();
        private static readonly Dictionary<long, HashSet<ZDOID>> s_targeted =
            new Dictionary<long, HashSet<ZDOID>>();
        private static readonly Dictionary<long, HashSet<ZDOID>> s_global =
            new Dictionary<long, HashSet<ZDOID>>();

        internal static bool PortalAwareReady =>
            IsLoaded && ForceHooksInstalled &&
            Cfg.TargetPortalAwareSync != null && Cfg.TargetPortalAwareSync.Value;

        internal static void Refresh(ManualLogSource log)
        {
            bool loaded = false;
            try
            {
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
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
            ForceHooksInstalled = false;
            Reset();

            if (loaded)
                log?.LogInfo("[TargetPortal] detected; preparing portal-aware dirty synchronization");
        }

        internal static void Install(Harmony harmony, ManualLogSource log)
        {
            if (!IsLoaded || harmony == null)
                return;

            try
            {
                MethodInfo global = null;
                MethodInfo targeted = null;
                MethodInfo[] methods = typeof(ZDOMan).GetMethods(
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic);

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method.Name != "ForceSendZDO")
                        continue;

                    ParameterInfo[] p = method.GetParameters();
                    if (p.Length == 1 && p[0].ParameterType == typeof(ZDOID))
                    {
                        if (global != null)
                            throw new InvalidOperationException("multiple global ForceSendZDO(ZDOID) overloads");
                        global = method;
                    }
                    else if (p.Length == 2 &&
                             ((p[0].ParameterType == typeof(ZDOID) && IsIntegral(p[1].ParameterType)) ||
                              (p[1].ParameterType == typeof(ZDOID) && IsIntegral(p[0].ParameterType))))
                    {
                        if (targeted != null)
                            throw new InvalidOperationException("multiple targeted ForceSendZDO overloads");
                        targeted = method;
                    }
                }

                if (global == null || targeted == null)
                {
                    throw new MissingMethodException(
                        "required ForceSendZDO(ZDOID) and ForceSendZDO(peer,ZDOID) overloads not found");
                }

                HarmonyMethod postfix = new HarmonyMethod(
                    typeof(TargetPortalCompat), nameof(ForceSendPostfix));
                harmony.Patch(global, postfix: postfix);
                harmony.Patch(targeted, postfix: postfix);
                ForceHooksInstalled = true;

                log.LogInfo("[TargetPortal] portal-aware dirty synchronization enabled; " +
                            "ordinary rounds stay dirty-set based, forced portal sends use one " +
                            "vanilla sync-list pass, and portal departures retain invalid-sector cleanup");
            }
            catch (Exception e)
            {
                ForceHooksInstalled = false;
                Reset();
                log.LogWarning("[TargetPortal] portal-aware sync unavailable; using vanilla ZDO " +
                               "sync-list handling: " + e.Message);
            }
        }

        private static bool IsIntegral(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(byte) || type == typeof(sbyte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong);
        }

        // Harmony's __args keeps this postfix valid for both ForceSendZDO overloads even if
        // Valheim changes the peer UID's integral width between compatible game hotfixes.
        private static void ForceSendPostfix(object[] __args)
        {
            try
            {
                ZDOID id = ZDOID.None;
                bool hasId = false;
                long peerUid = 0;
                bool hasPeer = false;

                if (__args != null)
                {
                    for (int i = 0; i < __args.Length; i++)
                    {
                        object arg = __args[i];
                        if (arg is ZDOID)
                        {
                            id = (ZDOID)arg;
                            hasId = true;
                        }
                        else if (TryIntegral(arg, out long value) && value > 0)
                        {
                            peerUid = value;
                            hasPeer = true;
                        }
                    }
                }

                if (!hasId || id == ZDOID.None)
                    return;

                if (hasPeer)
                    MarkTargeted(peerUid, id);
                else
                    MarkGlobal(id);
            }
            catch
            {
                // The hook is advisory. Vanilla ForceSendZDO has already completed and remains
                // the fallback if telemetry/tracking cannot interpret an argument.
            }
        }

        private static bool TryIntegral(object value, out long result)
        {
            result = 0;
            if (value == null) return false;
            try
            {
                if (value is byte) { result = (byte)value; return true; }
                if (value is sbyte) { result = (sbyte)value; return true; }
                if (value is short) { result = (short)value; return true; }
                if (value is ushort) { result = (ushort)value; return true; }
                if (value is int) { result = (int)value; return true; }
                if (value is uint) { result = (uint)value; return true; }
                if (value is long) { result = (long)value; return true; }
                if (value is ulong)
                {
                    ulong u = (ulong)value;
                    if (u > long.MaxValue) return false;
                    result = (long)u;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static void MarkTargeted(long peerUid, ZDOID id)
        {
            lock (s_gate)
            {
                Add(s_targeted, peerUid, id);
            }
        }

        private static void MarkGlobal(ZDOID id)
        {
            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            lock (s_gate)
            {
                for (int i = 0; i < man.m_peers.Count; i++)
                {
                    ZDOMan.ZDOPeer peer = man.m_peers[i];
                    if (peer?.m_peer == null) continue;
                    Add(s_global, peer.m_peer.m_uid, id);
                }
            }
        }

        private static void Add(Dictionary<long, HashSet<ZDOID>> map, long peerUid, ZDOID id)
        {
            if (peerUid <= 0 || id == ZDOID.None) return;
            if (!map.TryGetValue(peerUid, out HashSet<ZDOID> ids))
            {
                ids = new HashSet<ZDOID>();
                map[peerUid] = ids;
            }
            ids.Add(id);
        }

        internal static bool HasPending(ZDOMan.ZDOPeer peer)
        {
            if (!PortalAwareReady || peer?.m_peer == null)
                return false;

            long uid = peer.m_peer.m_uid;
            lock (s_gate)
            {
                return Contains(s_targeted, uid) || Contains(s_global, uid);
            }
        }

        private static bool Contains(Dictionary<long, HashSet<ZDOID>> map, long peerUid)
        {
            return map.TryGetValue(peerUid, out HashSet<ZDOID> ids) && ids.Count > 0;
        }

        internal static void Consume(ZDOMan.ZDOPeer peer)
        {
            if (peer?.m_peer == null) return;
            long uid = peer.m_peer.m_uid;
            lock (s_gate)
            {
                s_targeted.Remove(uid);
                s_global.Remove(uid);
            }
        }

        internal static void Reset()
        {
            lock (s_gate)
            {
                s_targeted.Clear();
                s_global.Clear();
            }
        }
    }
}
