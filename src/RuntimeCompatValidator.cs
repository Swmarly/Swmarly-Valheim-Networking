using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SmoothServer
{
    /// <summary>
    /// Runtime preflight for the replacement seams. The per-module Harmony transpilers still
    /// assert their own match counts; this pass prevents an unknown game build from reaching any
    /// of them unless the complete surface and the exact literals they expect are present.
    /// It is intentionally structural, not a version bypass: a changed signature, field, or IL
    /// pattern keeps replacement features vanilla.
    /// </summary>
    internal static class RuntimeCompatValidator
    {
        private static readonly Dictionary<ushort, OpCode> Opcodes = BuildOpcodes();

        internal static bool TryGetNetworkVersion(out int value)
        {
            value = 0;
            Type type = typeof(global::Version);
            string[] names = { "c_networkVersion", "m_networkVersion", "NetworkVersion", "networkVersion" };

            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo field = AccessTools.Field(type, names[i]);
                if (field != null && field.IsStatic)
                {
                    try
                    {
                        value = Convert.ToInt32(field.GetValue(null));
                        return true;
                    }
                    catch { }
                }

                PropertyInfo property = AccessTools.Property(type, names[i]);
                if (property != null && property.GetGetMethod(true) != null &&
                    property.GetGetMethod(true).IsStatic)
                {
                    try
                    {
                        value = Convert.ToInt32(property.GetValue(null, null));
                        return true;
                    }
                    catch { }
                }
            }

            return false;
        }

        internal static bool TryValidate(out string detail)
        {
            var errors = new List<string>();

            // Valheim has used both void and bool here across compatible releases. The
            // return value is not consumed by our patches; reject only an unexpected shape.
            MethodInfo send = RequireMethod(errors, typeof(ZDOMan), "SendZDOs",
                null, new[] { typeof(ZDOMan.ZDOPeer), typeof(bool) });
            if (send != null && send.ReturnType != typeof(void) && send.ReturnType != typeof(bool))
                errors.Add("ZDOMan.SendZDOs has unsupported return type " + send.ReturnType);
            MethodInfo cadence = RequireMethod(errors, typeof(ZDOMan), "SendZDOToPeers2",
                typeof(void), new[] { typeof(float) });
            MethodInfo release = RequireMethod(errors, typeof(ZDOMan), "ReleaseZDOS",
                typeof(void), new[] { typeof(float) });
            MethodInfo releaseNearby = RequireMethod(errors, typeof(ZDOMan), "ReleaseNearbyZDOS",
                typeof(void), new[] { typeof(Vector3), typeof(long) });
            MethodInfo createSync = RequireMethod(errors, typeof(ZDOMan), "CreateSyncList",
                typeof(void), new[] { typeof(ZDOMan.ZDOPeer), typeof(List<ZDO>) });
            MethodInfo sort = RequireMethod(errors, typeof(ZDOMan), "ServerSortSendZDOS",
                typeof(void), new[] { typeof(List<ZDO>), typeof(Vector3), typeof(ZDOMan.ZDOPeer) });

            MethodInfo queued = RequireMethod(errors, typeof(ZSteamSocket), "SendQueuedPackages",
                typeof(void), Type.EmptyTypes);
            MethodInfo callbacks = RequireMethod(errors, typeof(ZSteamSocket), "RegisterGlobalCallbacks",
                typeof(void), Type.EmptyTypes);

            MethodInfo syncPosition = null;
            if (!SmoothServerPlugin.IsServerSide)
            {
                syncPosition = AccessTools.Method(typeof(ZSyncTransform), "SyncPosition");
                if (syncPosition == null)
                    errors.Add("ZSyncTransform.SyncPosition missing");
                else
                {
                    ParameterInfo[] p = syncPosition.GetParameters();
                    if (p.Length != 3 || p[0].ParameterType != typeof(ZDO) ||
                        p[1].ParameterType != typeof(float) ||
                        p[2].ParameterType != typeof(bool).MakeByRefType() || !p[2].IsOut)
                        errors.Add("ZSyncTransform.SyncPosition signature is not (ZDO,float,out bool)");
                }
            }

            MethodInfo receive = RequireMethod(errors, typeof(ZRpc), "Update",
                typeof(ZRpc.ErrorCode), new[] { typeof(float) });
            MethodInfo create = AccessTools.Method(typeof(ZNetScene), "CreateObjects");
            if (create == null) errors.Add("ZNetScene.CreateObjects missing");

            MethodInfo save = RequireMethod(errors, typeof(ZNet), "SaveWorld",
                typeof(void), new[] { typeof(bool) });
            RequireNamed(errors, typeof(ZNet), "SaveWorldThread");
            MethodInfo collect = RequireMethod(errors, typeof(Game), "CollectResources",
                typeof(void), new[] { typeof(bool) });
            RequireNamed(errors, typeof(WearNTear), "UpdateSupport");
            RequireNamed(errors, typeof(WearNTear), "ClearCachedSupport");
            RequireNamed(errors, typeof(WearNTear), "OnDestroy");
            RequireNamed(errors, typeof(Heightmap), "RebuildRenderMesh");
            RequireNamed(errors, typeof(ZDO), "Deserialize");

            RequireField(errors, typeof(ZSteamSocket), "m_sendQueue");
            RequireField(errors, typeof(ZSteamSocket), "m_totalSent");
            RequireField(errors, typeof(ZSteamSocket), "m_con");

            RequireField(errors, typeof(ZDOMan), "m_objectsByID");
            RequireField(errors, typeof(ZDOMan), "m_peers");
            RequireField(errors, typeof(ZDOMan), "m_nextSendPeer");
            RequireField(errors, typeof(ZDOMan), "m_sendTimer");
            RequireField(errors, typeof(ZDOMan), "m_zdosSentLastSec");
            RequireField(errors, typeof(ZDOMan), "m_zdosRecvLastSec");
            RequireField(errors, typeof(ZDOMan.ZDOPeer), "m_peer");
            RequireField(errors, typeof(ZDOMan.ZDOPeer), "m_zdos");
            RequireField(errors, typeof(ZDOMan.ZDOPeer), "m_forceSend");
            RequireField(errors, typeof(ZDOMan.ZDOPeer), "m_invalidSector");
            RequireField(errors, typeof(ZNetPeer), "m_socket");
            RequireField(errors, typeof(ZNetPeer), "m_uid");
            RequireField(errors, typeof(ZNetPeer), "m_playerName");
            RequireField(errors, typeof(ZNetPeer), "m_refPos");
            RequireField(errors, typeof(ZNetPeer), "m_simulationDistance");
            RequireField(errors, typeof(ZNetScene), "m_instances");

            RequireNamed(errors, typeof(Steamworks.SteamNetworkingSockets),
                "GetConnectionRealTimeStatus");
            RequireNamed(errors, typeof(Steamworks.SteamGameServerNetworkingSockets),
                "GetConnectionRealTimeStatus");
            RequireNamed(errors, typeof(Steamworks.SteamNetworkingUtils), "GetConfigValue");
            RequireNamed(errors, typeof(Steamworks.SteamNetworkingUtils), "SetConfigValue");
            RequireNamed(errors, typeof(Steamworks.SteamGameServerNetworkingUtils), "GetConfigValue");
            RequireNamed(errors, typeof(Steamworks.SteamGameServerNetworkingUtils), "SetConfigValue");

            CheckIntIL(errors, send, "ZDOMan.SendZDOs", 10240, 2);
            CheckIntIL(errors, send, "ZDOMan.SendZDOs", 2048, 1);
            CheckIntIL(errors, create, "ZNetScene.CreateObjects", 10, 1);
            CheckFloatIL(errors, release, "ZDOMan.ReleaseZDOS", 2f, 1);
            if (!SmoothServerPlugin.IsServerSide)
            {
                CheckFloatIL(errors, syncPosition, "ZSyncTransform.SyncPosition", 0.2f, 2);
                CheckFloatIL(errors, syncPosition, "ZSyncTransform.SyncPosition", 2f, 2);
            }

            if (errors.Count == 0)
            {
                detail = "methods/fields/signatures/IL passed (forward-compatible surface)";
                return true;
            }

            detail = string.Join("; ", errors.ToArray());
            return false;
        }

        private static MethodInfo RequireMethod(List<string> errors, Type type, string name,
            Type returnType, Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            if (method == null)
            {
                errors.Add(type.Name + "." + name + " expected signature not found");
                return null;
            }

            if (returnType != null && method.ReturnType != returnType)
                errors.Add(type.Name + "." + name + " return type changed to " + method.ReturnType);
            return method;
        }

        private static void RequireNamed(List<string> errors, Type type, string name)
        {
            if (AccessTools.Method(type, name) == null)
                errors.Add(type.Name + "." + name + " missing");
        }

        private static void RequireField(List<string> errors, Type type, string name)
        {
            if (AccessTools.Field(type, name) == null)
                errors.Add(type.Name + "." + name + " field missing");
        }

        private static void CheckIntIL(List<string> errors, MethodInfo method, string label,
            int value, int expected)
        {
            if (method == null) return;
            int count;
            if (!TryCountInt(method, value, out count))
            {
                errors.Add(label + " IL could not be read");
                return;
            }
            if (count != expected)
                errors.Add(label + " expected " + expected + "x ldc.i4 " + value +
                           ", found " + count);
        }

        private static void CheckFloatIL(List<string> errors, MethodInfo method, string label,
            float value, int expected)
        {
            if (method == null) return;
            int count;
            if (!TryCountFloat(method, value, out count))
            {
                errors.Add(label + " IL could not be read");
                return;
            }
            if (count != expected)
                errors.Add(label + " expected " + expected + "x ldc.r4 " + value +
                           ", found " + count);
        }

        private static bool TryCountInt(MethodInfo method, int wanted, out int count)
        {
            count = 0;
            MethodBody body;
            try { body = method.GetMethodBody(); }
            catch { return false; }
            if (body == null) return false;

            byte[] il = body.GetILAsByteArray();
            if (il == null) return false;
            int offset = 0;
            while (offset < il.Length)
            {
                OpCode op;
                if (!ReadOpcode(il, ref offset, out op)) return false;

                if (op == OpCodes.Ldc_I4_M1 && wanted == -1) count++;
                else if (op == OpCodes.Ldc_I4_0 && wanted == 0) count++;
                else if (op == OpCodes.Ldc_I4_1 && wanted == 1) count++;
                else if (op == OpCodes.Ldc_I4_2 && wanted == 2) count++;
                else if (op == OpCodes.Ldc_I4_3 && wanted == 3) count++;
                else if (op == OpCodes.Ldc_I4_4 && wanted == 4) count++;
                else if (op == OpCodes.Ldc_I4_5 && wanted == 5) count++;
                else if (op == OpCodes.Ldc_I4_6 && wanted == 6) count++;
                else if (op == OpCodes.Ldc_I4_7 && wanted == 7) count++;
                else if (op == OpCodes.Ldc_I4_8 && wanted == 8) count++;
                else if (op == OpCodes.Ldc_I4_S)
                {
                    if (!CanRead(il, offset, 1)) return false;
                    if ((sbyte)il[offset] == wanted) count++;
                }
                else if (op == OpCodes.Ldc_I4)
                {
                    if (!CanRead(il, offset, 4)) return false;
                    if (BitConverter.ToInt32(il, offset) == wanted) count++;
                }

                if (!SkipOperand(il, ref offset, op.OperandType)) return false;
            }
            return true;
        }

        private static bool TryCountFloat(MethodInfo method, float wanted, out int count)
        {
            count = 0;
            MethodBody body;
            try { body = method.GetMethodBody(); }
            catch { return false; }
            if (body == null) return false;

            byte[] il = body.GetILAsByteArray();
            if (il == null) return false;
            int offset = 0;
            while (offset < il.Length)
            {
                OpCode op;
                if (!ReadOpcode(il, ref offset, out op)) return false;
                if (op == OpCodes.Ldc_R4)
                {
                    if (!CanRead(il, offset, 4)) return false;
                    float value = BitConverter.ToSingle(il, offset);
                    if (Math.Abs(value - wanted) < 0.0001f) count++;
                }
                if (!SkipOperand(il, ref offset, op.OperandType)) return false;
            }
            return true;
        }

        private static bool ReadOpcode(byte[] il, ref int offset, out OpCode op)
        {
            op = default(OpCode);
            if (!CanRead(il, offset, 1)) return false;
            ushort key = il[offset++];
            if (key == 0xfe)
            {
                if (!CanRead(il, offset, 1)) return false;
                key = (ushort)(0xfe00 | il[offset++]);
            }
            return Opcodes.TryGetValue(key, out op);
        }

        private static bool SkipOperand(byte[] il, ref int offset, OperandType type)
        {
            int size;
            switch (type)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineR:
                case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI8:
                case OperandType.InlineMethod:
                case OperandType.InlineR:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType: size = type == OperandType.InlineI8 ||
                    type == OperandType.InlineR ? 8 : 4; break;
                case OperandType.InlineSwitch:
                    if (!CanRead(il, offset, 4)) return false;
                    int n = BitConverter.ToInt32(il, offset);
                    if (n < 0 || n > 100000) return false;
                    size = 4 + n * 4;
                    break;
                default: return false;
            }
            if (!CanRead(il, offset, size)) return false;
            offset += size;
            return true;
        }

        private static bool CanRead(byte[] il, int offset, int count)
        {
            return offset >= 0 && count >= 0 && offset <= il.Length - count;
        }

        private static Dictionary<ushort, OpCode> BuildOpcodes()
        {
            var result = new Dictionary<ushort, OpCode>();
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public |
                BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(OpCode)) continue;
                OpCode op = (OpCode)fields[i].GetValue(null);
                result[unchecked((ushort)op.Value)] = op;
            }
            return result;
        }
    }
}
