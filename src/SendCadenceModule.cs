using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SmoothServer
{
    /// <summary>
    /// C1 - paced per-peer ZDO send cadence. Vanilla advances one peer per frame;
    /// the previous replacement swept every peer in one frame. This scheduler preserves
    /// aggregate cadence for normal 5-6 player server ticks, rotates peers fairly, and
    /// bounds catch-up work after a hitch.
    /// </summary>
    internal sealed class SendCadenceModule : FeatureModule
    {
        public override string Name => "SendCadence";

        private ConfigEntry<float> _sendHz;
        private ConfigEntry<int> _maxPeerSendsPerFrame;

        internal static bool Active;
        internal static float SendHz = 20f;
        internal static int MaxPeerSendsPerFrame;

        private static ZDOMan _owner;
        private static float _sendDebt;
        private static int _cursor;
        private static float _failureLogAt;
        private static int _sendFailures;

        public override void Configure(ConfigFile cfg)
        {
            EnabledCfg = cfg.Bind("SendCadence", "Enabled", true,
                "Pace ZDO sends fairly across peers instead of sweeping every peer in one frame.");
            _sendHz = cfg.Bind("SendCadence", "SendHz", 20f,
                "Per-peer ZDO send frequency. The scheduler rotates peers and preserves this " +
                "aggregate rate where the frame budget permits. Clamped to 1-60." + Profiles.Note);
            _maxPeerSendsPerFrame = cfg.Bind("SendCadence", "MaxPeerSendsPerFrame", 0,
                "Safety cap for peer SendZDOs calls in one frame. 0 = automatic cap (up to four " +
                "peers per frame, enough to preserve 20Hz for 5-6 peers at a 30Hz server tick). " +
                "Raise only after measuring frame time.");
            Watch(_sendHz);
            Watch(_maxPeerSendsPerFrame);
        }

        protected override void ApplyPatches()
        {
            ReadConfig();
            var target = AccessTools.Method(typeof(ZDOMan), "SendZDOToPeers2", new[] { typeof(float) });
            if (target == null)
                throw new Exception("SmoothServer SendCadence: ZDOMan.SendZDOToPeers2(float) not found");
            PatchGuard.RequireExclusive(target, "ZDOMan.SendZDOToPeers2");

            Harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(SendCadenceModule), nameof(Prefix))
                {
                    priority = Priority.High
                });

            ResetScheduler();
            Active = true;
            Log.LogInfo("[SendCadence] SendHz=" + SendHz.ToString("F1") +
                        " maxPeerSendsPerFrame=" +
                        (MaxPeerSendsPerFrame > 0 ? MaxPeerSendsPerFrame.ToString() : "auto(4)"));
        }

        public override void Disable()
        {
            Active = false;
            ResetScheduler();
            base.Disable();
        }

        public override void OnConfigChanged(ConfigEntryBase entry)
        {
            if (entry != _sendHz && entry != _maxPeerSendsPerFrame) return;
            ReadConfig();
            Log.LogInfo("[SendCadence] SendHz=" + SendHz.ToString("F1") +
                        " maxPeerSendsPerFrame=" +
                        (MaxPeerSendsPerFrame > 0 ? MaxPeerSendsPerFrame.ToString() : "auto(4)"));
        }

        private void ReadConfig()
        {
            SendHz = Mathf.Clamp(_sendHz.Value, 1f, 60f);
            MaxPeerSendsPerFrame = Math.Max(0, _maxPeerSendsPerFrame.Value);
        }

        private static void ResetScheduler()
        {
            _owner = null;
            _sendDebt = 0f;
            _cursor = 0;
            _failureLogAt = 0f;
            _sendFailures = 0;
        }

        private static int AutomaticCap(int peerCount)
        {
            return Math.Max(1, Math.Min(peerCount, 4));
        }

        private static bool Prefix(ZDOMan __instance, float dt)
        {
            if (!Active || !ServerActive()) return true;

            var peers = __instance.m_peers;
            int peerCount = peers == null ? 0 : peers.Count;
            if (peerCount == 0)
            {
                __instance.m_nextSendPeer = -1;
                if (_owner == __instance) ResetScheduler();
                return false;
            }

            if (_owner != __instance)
            {
                _owner = __instance;
                _sendDebt = 0f;
                _cursor = 0;
            }

            dt = Mathf.Clamp(dt, 0f, 0.25f);
            _sendDebt += dt * SendHz * peerCount;
            float maxDebt = peerCount * 4f;
            if (_sendDebt > maxDebt) _sendDebt = maxDebt;

            int due = Mathf.FloorToInt(_sendDebt);
            if (due > 0)
            {
                int cap = MaxPeerSendsPerFrame > 0
                    ? Math.Min(MaxPeerSendsPerFrame, peerCount)
                    : AutomaticCap(peerCount);
                if (due > cap) due = cap;
                _sendDebt -= due;

                for (int i = 0; i < due; i++)
                {
                    if (_cursor >= peerCount) _cursor = 0;
                    var peer = peers[_cursor++];
                    if (peer == null) continue;

                    try
                    {
                        // SendZDOs may return void or bool across compatible Valheim releases.
                        __instance.SendZDOs(peer, false);
                    }
                    catch (Exception e)
                    {
                        _sendFailures++;
                        float now = Time.realtimeSinceStartup;
                        if (now >= _failureLogAt)
                        {
                            _failureLogAt = now + 10f;
                            SmoothServerPlugin.Log.LogWarning("[SendCadence] " + _sendFailures +
                                " peer-send calls failed in the last window; latest=" + e.Message);
                            _sendFailures = 0;
                        }
                    }
                }
            }

            __instance.m_nextSendPeer = -1;
            return false;
        }
    }
}
