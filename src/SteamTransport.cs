// -----------------------------------------------------------------------------
// File role: Reflection-tolerant Steam socket unwrapping, interface selection, handle extraction, and diagnostics.
// Why it exists: Other mods can wrap ZSteamSocket; telemetry/tuning must find the underlying socket and choose server/client Steam APIs without assuming one concrete type.
// Change contract: Every fallback must report unavailable rather than inventing transport data.
// -----------------------------------------------------------------------------
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SmoothServer
{
    /// <summary>
    /// Safe transport introspection shared by telemetry, compression, and Steam tuning.
    /// ServerSync and similar libraries can leave nested BufferingSocket wrappers around the
    /// real transport. Never classify a wrapper by inheritance: ServerSync's BufferingSocket
    /// derives from ZPlayFabSocket even when its Original socket is Steam.
    /// </summary>
    internal static class SteamTransport
    {
        private const int MaxUnwrapDepth = 16;
        private const BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static ISocket UnwrapSocket(ISocket socket)
        {
            int depth = 0;
            while (socket != null && depth++ < MaxUnwrapDepth &&
                   socket.GetType().Name == "BufferingSocket")
            {
                object next = null;
                Type type = socket.GetType();

                FieldInfo field = type.GetField("Original", InstanceFlags);
                if (field != null)
                {
                    try { next = field.GetValue(socket); }
                    catch { next = null; }
                }
                else
                {
                    PropertyInfo property = type.GetProperty("Original", InstanceFlags);
                    if (property != null && property.GetGetMethod(true) != null)
                    {
                        try { next = property.GetValue(socket, null); }
                        catch { next = null; }
                    }
                }

                var inner = next as ISocket;
                if (inner == null || object.ReferenceEquals(inner, socket)) break;
                socket = inner;
            }
            return socket;
        }

        internal static ZSteamSocket AsSteamSocket(ISocket socket)
        {
            return UnwrapSocket(socket) as ZSteamSocket;
        }

        internal static string Describe(ISocket socket)
        {
            if (socket == null) return "null";
            ISocket inner = UnwrapSocket(socket);
            if (inner == null) return socket.GetType().Name + " -> null";
            if (object.ReferenceEquals(inner, socket)) return socket.GetType().Name;
            return socket.GetType().Name + " -> " + inner.GetType().Name;
        }

        internal static bool TryGetConnectionHandle(ZSteamSocket socket, out uint handle)
        {
            handle = 0u;
            if (socket == null) return false;

            try
            {
                FieldInfo connection = FindField(socket.GetType(), "m_con");
                if (connection == null) return false;

                object value = connection.GetValue(socket);
                if (value == null) return false;

                if (value is uint)
                {
                    handle = (uint)value;
                    return handle != 0u;
                }

                // Steamworks.NET normally wraps HSteamNetConnection as a struct containing
                // m_HSteamNetConnection, but forks have exposed it as int/long or renamed the
                // value field. Only accept a field whose name identifies the connection handle;
                // never guess from an arbitrary numeric field.
                FieldInfo[] fields = value.GetType().GetFields(InstanceFlags);
                for (int i = 0; i < fields.Length; i++)
                {
                    string name = fields[i].Name;
                    if (name.IndexOf("HSteamNetConnection", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("ConnectionHandle", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !name.Equals("m_Value", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("Value", StringComparison.OrdinalIgnoreCase))
                        continue;

                    object raw = fields[i].GetValue(value);
                    if (raw is uint) handle = (uint)raw;
                    else if (raw is int && (int)raw > 0) handle = (uint)(int)raw;
                    else if (raw is long && (long)raw > 0) handle = (uint)(long)raw;
                    else if (raw is ulong && (ulong)raw > 0) handle = (uint)(ulong)raw;
                    if (handle != 0u) return true;
                }
            }
            catch { }

            return false;
        }

        internal static bool TrySetConnectionConfig(string enumMemberName, int value,
                                                     uint connectionHandle, out string reason)
        {
            reason = null;
            if (connectionHandle == 0u)
            {
                reason = "connection handle unavailable";
                return false;
            }

            IntPtr valuePtr = IntPtr.Zero;
            try
            {
                Type enumType = FindType("Steamworks.ESteamNetworkingConfigValue");
                Type scopeType = FindType("Steamworks.ESteamNetworkingConfigScope");
                Type dataType = FindType("Steamworks.ESteamNetworkingConfigDataType");
                if (enumType == null || scopeType == null || dataType == null)
                {
                    reason = "Steamworks config enum unavailable";
                    return false;
                }

                if (Array.IndexOf(Enum.GetNames(enumType), enumMemberName) < 0)
                {
                    reason = enumMemberName + " is not exposed by this Steamworks assembly";
                    return false;
                }

                string scopeName = "k_ESteamNetworkingConfig_Connection";
                if (Array.IndexOf(Enum.GetNames(scopeType), scopeName) < 0)
                {
                    reason = "connection config scope unavailable";
                    return false;
                }

                object enumValue = Enum.Parse(enumType, enumMemberName);
                object scopeValue = Enum.Parse(scopeType, scopeName);
                object dataValue = Enum.Parse(dataType, "k_ESteamNetworkingConfig_Int32");

                Type utilsType = FindType(SmoothServerPlugin.IsServerSide
                    ? "Steamworks.SteamGameServerNetworkingUtils"
                    : "Steamworks.SteamNetworkingUtils");
                if (utilsType == null)
                {
                    reason = "matching SteamNetworkingUtils interface unavailable";
                    return false;
                }

                MethodInfo setter = null;
                foreach (MethodInfo method in utilsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == "SetConfigValue" && method.GetParameters().Length == 5)
                    {
                        setter = method;
                        break;
                    }
                }
                if (setter == null)
                {
                    reason = "SetConfigValue connection overload unavailable";
                    return false;
                }

                valuePtr = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(valuePtr, value);

                object result = setter.Invoke(null, new object[]
                {
                    enumValue, scopeValue, new IntPtr((long)connectionHandle), dataValue, valuePtr
                });

                if (result is bool && !(bool)result)
                {
                    reason = "Steam rejected the connection setting";
                    return false;
                }

                return true;
            }
            catch (TargetInvocationException e)
            {
                reason = e.InnerException != null ? e.InnerException.Message : e.Message;
                return false;
            }
            catch (Exception e)
            {
                reason = e.Message;
                return false;
            }
            finally
            {
                if (valuePtr != IntPtr.Zero) Marshal.FreeHGlobal(valuePtr);
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, InstanceFlags);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
    }
}
