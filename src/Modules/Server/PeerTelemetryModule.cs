// -----------------------------------------------------------------------------
// File role: Read-only per-peer transport, queue, ZDO, and Steam-status sampling.
// Why it exists: Diagnostics must distinguish valid measurements from unavailable API data so AdaptiveBudget and operators do not act on fabricated zeros.
// Change contract: This module intentionally has no Harmony patches and remains useful when replacement patches are gated off.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Steamworks;
using UnityEngine;

namespace SmoothServer
{
    /// <summary>
    /// M1 - per-peer instrumentation. No Harmony patches: it polls the live sockets.
    ///
    /// Every SampleIntervalSec it walks ZDOMan.m_peers and records, per peer:
    ///   * ZSteamSocket.GetConnectionQuality  -> ping, local/remote quality, out/in B/s
    ///   * SteamNetworkingSockets.GetConnectionRealTimeStatus(m_con) -> m_cbPendingReliable,
    ///     m_cbPendingUnreliable (queued but not yet on the wire), m_cbSentUnackedReliable
    ///     (in flight), m_nSendRateBytesPerSecond (Steam's own bandwidth estimate)
    ///   * ISocket.GetSendQueueSize() - exactly the number ZDOMan.SendZDOs budgets against
    ///   * the ZDOMan-side per-peer state: peer.m_zdos.Count (ZDOs this peer is known to have),
    ///     peer.m_forceSend.Count, peer.m_invalidSector.Count
    /// plus the global ZDOMan.m_zdosSentLastSec / m_zdosRecvLastSec.
    ///
    /// <b>Which Steam interface (corrected in 0.3.1).</b> The two builds of assembly_valheim.dll
    /// are compiled differently: on the CLIENT, ZSteamSocket calls SteamNetworkingSockets; on the
    /// DEDICATED SERVER it calls SteamGameServerNetworkingSockets throughout (NOTES §20). Only the
    /// matching half of Steamworks is initialised per process, so the other one throws
    /// "Steamworks is not initialized.". We therefore probe the build's own interface FIRST, cache
    /// whichever answers, and log it once (open question 1 of SMOOTHSERVER-PHASE2 §7).
    ///
    /// Note one vanilla quirk the decompile exposes: even on the server build,
    /// ZSteamSocket.GetConnectionQuality still calls the *client* SteamNetworkingSockets - so it
    /// throws on a dedicated server. It is client-UI-only in vanilla, which is why nobody noticed.
    /// We call it once, and if it throws we stop calling it instead of paying an interop
    /// exception per peer per sample.
    ///
    /// The snapshot is static so AdaptiveBudget (and anything later) can read it without
    /// re-polling Steam.
    /// </summary>
    internal sealed class PeerTelemetryModule : FeatureModule
    {
        public override string Name => "PeerTelemetry";
        public override bool RequiresCompatibility => false;

        internal struct PeerStat
        {
            public long Uid;
            public string PlayerName;
            public bool Valid;              // Steam real-time status answered
            public string SteamStatus;       // source or precise failure reason
            public int Ping;                // ms
            public float QualityLocal;
            public float QualityRemote;
            public float OutBytesPerSec;
            public float InBytesPerSec;
            public int PendingReliable;     // queued in Steam, not yet sent
            public int PendingUnreliable;
            public int SentUnackedReliable; // in flight
            public int SendRateBytesPerSec; // Steam's own estimate
            public int SocketQueueBytes;    // ISocket.GetSendQueueSize() - what SendZDOs budgets on
            public int ZdoQueue;            // peer.m_zdos.Count
            public int ForceSend;
            public int InvalidSector;
            public float SampledAt;
        }

        private ConfigEntry<float> _interval;
        private ConfigEntry<float> _sampleInterval;

        internal static bool Active;
        internal static float IntervalSec = 10f;
        internal static float SampleIntervalSec = 1f;

        private static readonly Dictionary<long, PeerStat> Stats = new Dictionary<long, PeerStat>();
        private static readonly List<long> TempIds = new List<long>();
        private static float _sampleAcc;
        private static float _logAcc;
        private static int _steamIface;     // 0 = unknown, 1 = user, 2 = gameserver, -1 = none
        private static float _ifaceRetryAt;
        private static bool _ifaceLogged;
        private static bool _qualityAbsent; // ZSteamSocket.GetConnectionQuality unusable on this build

        public override void Configure(ConfigFile cfg)
        {
            EnabledCfg = cfg.Bind("PeerTelemetry", "Enabled", true,
                "Log a per-peer network line (ping, pending/in-flight bytes, send rate, ZDO queue). " +
                "Also feeds AdaptiveBudget.");
            _interval = cfg.Bind("PeerTelemetry", "IntervalSec", 10f,
                "Seconds between per-peer telemetry lines.");
            _sampleInterval = cfg.Bind("PeerTelemetry", "SampleIntervalSec", 1f,
                "Seconds between samples of the live Steam connection status. The snapshot other " +
                "modules read is refreshed at this rate; the log line is printed every IntervalSec.");
            Watch(_interval);
            Watch(_sampleInterval);
        }

        protected override void ApplyPatches()
        {
            IntervalSec = Mathf.Max(1f, _interval.Value);
            SampleIntervalSec = Mathf.Clamp(_sampleInterval.Value, 0.1f, 10f);
            Stats.Clear();
            _sampleAcc = 0f; _logAcc = 0f;
            _steamIface = 0;
            _ifaceRetryAt = 0f;
            _ifaceLogged = false;
            _qualityAbsent = false;
            Active = true;
            Log.LogInfo("[PeerTelemetry] sampling every " + SampleIntervalSec.ToString("F1") +
                        "s, logging every " + IntervalSec.ToString("F1") + "s (no patches)");
        }

        public override void Disable()
        {
            Active = false;
            Stats.Clear();
            base.Disable();
        }

        public override void OnConfigChanged(ConfigEntryBase entry)
        {
            if (entry == _interval) IntervalSec = Mathf.Max(1f, _interval.Value);
            else if (entry == _sampleInterval) SampleIntervalSec = Mathf.Clamp(_sampleInterval.Value, 0.1f, 10f);
            else return;
            _logAcc = 0f;
            Log.LogInfo("[PeerTelemetry] interval=" + IntervalSec.ToString("F1") +
                        "s sample=" + SampleIntervalSec.ToString("F1") + "s");
        }

        // ---- public snapshot ------------------------------------------------------------

        /// <summary>Latest sample for a peer uid. False when telemetry is off or the peer is new.</summary>
        internal static bool TryGet(long uid, out PeerStat stat)
        {
            return Stats.TryGetValue(uid, out stat);
        }

        /// <summary>Copy of every current per-peer sample.</summary>
        internal static PeerStat[] Snapshot()
        {
            var a = new PeerStat[Stats.Count];
            Stats.Values.CopyTo(a, 0);
            return a;
        }

        // ---- polling -------------------------------------------------------------------

        internal static void Tick(float dt)
        {
            if (!Active || !ServerActive()) return;

            _sampleAcc += dt;
            _logAcc += dt;

            if (_sampleAcc >= SampleIntervalSec)
            {
                _sampleAcc = 0f;
                Sample();
            }

            if (_logAcc >= IntervalSec)
            {
                _logAcc = 0f;
                Emit();
            }
        }

        private static void Sample()
        {
            var zm = ZDOMan.instance;
            if (zm == null) return;

            float now = Time.realtimeSinceStartup;
            TempIds.Clear();

            for (int i = 0; i < zm.m_peers.Count; i++)
            {
                var zp = zm.m_peers[i];
                if (zp == null || zp.m_peer == null) continue;

                var stat = new PeerStat
                {
                    Uid = zp.m_peer.m_uid,
                    PlayerName = string.IsNullOrEmpty(zp.m_peer.m_playerName) ? "?" : zp.m_peer.m_playerName,
                    ZdoQueue = zp.m_zdos.Count,
                    ForceSend = zp.m_forceSend.Count,
                    InvalidSector = zp.m_invalidSector.Count,
                    SampledAt = now,
                    SteamStatus = "not-probed"
                };

                var sock = zp.m_peer.m_socket;
                if (sock != null)
                {
                    try { stat.SocketQueueBytes = sock.GetSendQueueSize(); }
                    catch { stat.SocketQueueBytes = -1; }
                }

                var zs = SteamTransport.AsSteamSocket(sock);
                if (zs != null)
                {
                    SteamNetConnectionRealTimeStatus_t realtime;
                    string statusReason;
                    if (TryRealTimeStatus(zs, out realtime, out statusReason))
                    {
                        stat.Valid = true;
                        stat.SteamStatus = _steamIface == 2 ? "game-server" : "user";
                        stat.PendingReliable = realtime.m_cbPendingReliable;
                        stat.PendingUnreliable = realtime.m_cbPendingUnreliable;
                        stat.SentUnackedReliable = realtime.m_cbSentUnackedReliable;
                        stat.SendRateBytesPerSec = realtime.m_nSendRateBytesPerSecond;
                        stat.Ping = realtime.m_nPing;
                        stat.QualityLocal = realtime.m_flConnectionQualityLocal;
                        stat.QualityRemote = realtime.m_flConnectionQualityRemote;
                        stat.OutBytesPerSec = realtime.m_flOutBytesPerSec;
                        stat.InBytesPerSec = realtime.m_flInBytesPerSec;
                    }
                    else
                    {
                        stat.SteamStatus = statusReason;
                        // A listen-host/client may still have a working vanilla quality accessor.
                        // It is not marked Valid because AdaptiveBudget requires richer status.
                        if (!SmoothServerPlugin.IsServerSide && !_qualityAbsent)
                        {
                            try
                            {
                                float ql, qr, ob, ib; int ping;
                                zs.GetConnectionQuality(out ql, out qr, out ping, out ob, out ib);
                                stat.QualityLocal = ql;
                                stat.QualityRemote = qr;
                                stat.Ping = ping;
                                stat.OutBytesPerSec = ob;
                                stat.InBytesPerSec = ib;
                                stat.SteamStatus = "vanilla-quality-only";
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message != null && ex.Message.IndexOf("not initialized",
                                        StringComparison.OrdinalIgnoreCase) >= 0)
                                    _qualityAbsent = true;
                            }
                        }
                    }
                }
                else if (sock == null)
                {
                    stat.SteamStatus = "no-socket";
                }
                else
                {
                    stat.SteamStatus = "transport:" + SteamTransport.Describe(sock);
                }

                Stats[stat.Uid] = stat;
                TempIds.Add(stat.Uid);
            }

            // drop peers that went away
            if (Stats.Count != TempIds.Count)
            {
                var stale = new List<long>();
                foreach (var kv in Stats)
                    if (!TempIds.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (var id in stale) Stats.Remove(id);
            }
        }

        private static bool TryRealTimeStatus(ZSteamSocket zs,
            out SteamNetConnectionRealTimeStatus_t status, out string reason)
        {
            status = default(SteamNetConnectionRealTimeStatus_t);
            reason = "status unavailable";

            uint handle;
            if (!SteamTransport.TryGetConnectionHandle(zs, out handle))
            {
                reason = "connection handle unavailable";
                return false;
            }

            if (_steamIface == -1)
            {
                if (Time.realtimeSinceStartup < _ifaceRetryAt)
                {
                    reason = "Steam status interface unavailable; retry pending";
                    return false;
                }
                _steamIface = 0;
            }

            bool serverFirst = SmoothServerPlugin.IsServerSide;
            string firstReason = null;

            if (_steamIface == 0 || _steamIface == (serverFirst ? 2 : 1))
            {
                if (Probe(serverFirst, zs, out status, out reason)) return true;
                firstReason = reason;
            }

            if (_steamIface == 0 || _steamIface == (serverFirst ? 1 : 2))
            {
                if (Probe(!serverFirst, zs, out status, out reason)) return true;
            }

            if (string.IsNullOrEmpty(reason)) reason = firstReason ?? "no Steam interface answered";
            if (_steamIface == 0)
                SetIface(-1, "neither interface answered GetConnectionRealTimeStatus");
            return false;
        }

        private static bool Probe(bool gameServer, ZSteamSocket zs,
            out SteamNetConnectionRealTimeStatus_t status, out string reason)
        {
            status = default(SteamNetConnectionRealTimeStatus_t);
            reason = null;
            string label = gameServer ? "game-server" : "user";

            try
            {
                SteamNetConnectionRealTimeLaneStatus_t lanes =
                    default(SteamNetConnectionRealTimeLaneStatus_t);
                EResult result = gameServer
                    ? SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(zs.m_con, ref status, 0,
                        ref lanes)
                    : SteamNetworkingSockets.GetConnectionRealTimeStatus(zs.m_con, ref status, 0,
                        ref lanes);
                if (result == EResult.k_EResultOK)
                {
                    SetIface(gameServer ? 2 : 1, gameServer
                        ? "SteamGameServerNetworkingSockets (game-server interface)"
                        : "SteamNetworkingSockets (user interface)");
                    return true;
                }

                reason = label + " interface returned " + result;
            }
            catch (Exception ex)
            {
                reason = label + " interface exception " + ex.GetType().Name +
                         (string.IsNullOrEmpty(ex.Message) ? "" : ": " + ex.Message);
            }
            return false;
        }

        private static void SetIface(int iface, string what)
        {
            _steamIface = iface;
            if (iface == -1)
            {
                _ifaceRetryAt = Time.realtimeSinceStartup + 30f;
                return;
            }
            _ifaceRetryAt = 0f;
            if (_ifaceLogged) return;
            _ifaceLogged = true;
            SmoothServerPlugin.Log.LogInfo("[PeerTelemetry] Steam real-time status source: " + what);
        }

        private static void Emit()
        {
            var zm = ZDOMan.instance;
            int peers = zm == null ? 0 : zm.m_peers.Count;

            if (peers == 0)
            {
                SmoothServerPlugin.Log.LogInfo("[PeerTelemetry] peers=0 - no peers connected, nothing to measure");
                return;
            }

            SmoothServerPlugin.Log.LogInfo(string.Format(
                "[PeerTelemetry] peers={0} zdosSent/s={1} zdosRecv/s={2}",
                peers, zm.m_zdosSentLastSec, zm.m_zdosRecvLastSec));

            foreach (var kv in Stats)
            {
                var s = kv.Value;
                SmoothServerPlugin.Log.LogInfo(string.Format(
                    "[PeerTelemetry]   '{0}' uid={1} ping={2}ms qual={3:F2}/{4:F2} out={5:F1}kB/s in={6:F1}kB/s " +
                    "pending={7}B(r)+{8}B(u) inflight={9}B steamRate={10}B/s socketQueue={11}B zdos={12} force={13} invalid={14}{15}",
                    s.PlayerName, s.Uid, s.Ping, s.QualityLocal, s.QualityRemote,
                    s.OutBytesPerSec / 1024f, s.InBytesPerSec / 1024f,
                    s.PendingReliable, s.PendingUnreliable, s.SentUnackedReliable,
                    s.SendRateBytesPerSec, s.SocketQueueBytes, s.ZdoQueue, s.ForceSend, s.InvalidSector,
                    " status=" + s.SteamStatus));
            }
        }
    }
}
