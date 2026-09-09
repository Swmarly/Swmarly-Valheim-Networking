using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ValheimTune;
using ValheimTune.Patches;

namespace SmoothServer
{
    /// <summary>
    /// Hosts the useful, non-overlapping ValheimTune features inside the single Swmarly plugin.
    ///
    /// The original ValheimTune Plugin class is intentionally not included. SmoothServer owns
    /// the lifecycle, config reload, logging, version gate, and Harmony instances. We also do not
    /// import ValheimTune's ConstPatches: SmoothServer's adaptive SendBudget, SendCadence,
    /// SteamRates, and SendQueueGuard are the selected implementations for those seams.
    /// </summary>
    internal static class ValheimTuneBridge
    {
        private static Harmony _harmony;
        private static bool _started;
        private static float _watchdogTimer;
        private static float _logTimer;

        internal static bool Active { get; private set; }

        internal static void Initialize(ConfigFile config, ManualLogSource log)
        {
            try
            {
                Cfg.Bind(config);
                Compat.GameVersion = global::Version.CurrentVersion.ToString();
                Compat.ReplacementsAllowed = !Cfg.DisableOnUnknownBuild.Value ||
                                             Compat.IsKnown(Compat.GameVersion, Cfg.KnownGoodBuilds.Value);
                SmoothServerPlugin.ReplacementsAllowed = Compat.ReplacementsAllowed;

                if (SmoothServerPlugin.ConflictingNetworkingModPresent)
                {
                    SmoothServerPlugin.ReplacementsAllowed = false;
                    log.LogWarning("[ValheimTuneBridge] overlapping mod detected; bridge patches disabled");
                    return;
                }

                if (!SmoothServerPlugin.ReplacementsAllowed)
                {
                    log.LogWarning("[ValheimTuneBridge] game " + Compat.GameVersion +
                                   " is not in KnownGoodBuilds (" + Cfg.KnownGoodBuilds.Value +
                                   "); all optional replacement patches will stay inactive");
                    return;
                }

                if (!SmoothServerPlugin.IsServerSide)
                {
                    log.LogInfo("[ValheimTuneBridge] server-only optimisation patches skipped on the client");
                    return;
                }

                _harmony = new Harmony(SmoothServerPlugin.PluginGuid + ".valheimtune");

                // These are the selected ValheimTune patches. ConstPatches is deliberately
                // excluded because it overlaps with SmoothServer's stronger implementations.
                Type[] selected =
                {
                    typeof(AssetUnloadPatch),
                    typeof(DirtyPatches),
                    typeof(MeasurePatches),
                    typeof(ReceiveCapPatch),
                    typeof(RenderMeshPatch),
                    typeof(SortPatches)
                };

                int patched = 0;
                foreach (Type type in selected)
                {
                    try
                    {
                        patched += _harmony.CreateClassProcessor(type).Patch().Count;
                    }
                    catch (Exception e)
                    {
                        // A single changed game method must not prevent the rest of the merged
                        // plugin from loading. The affected feature falls back to vanilla.
                        log.LogError("[ValheimTuneBridge] " + type.Name + " disabled: " + e.Message);
                    }
                }

                if (patched == 0)
                {
                    _harmony.UnpatchSelf();
                    _harmony = null;
                    _started = false;
                    Active = false;
                    log.LogWarning("[ValheimTuneBridge] no selected patches matched this build; bridge disabled");
                    return;
                }

                _started = true;
                Active = true;
                log.LogInfo("[ValheimTuneBridge] " + Compat.GameVersion +
                            " loaded; selected patches=" + patched +
                            ", replacements=" + (Compat.ReplacementsAllowed ? "on" : "OFF"));
            }
            catch (Exception e)
            {
                Active = false;
                log.LogError("[ValheimTuneBridge] failed to initialise; all bridge features are disabled: " + e);
            }
        }

        internal static void Tick(float dt)
        {
            if (!Active || !_started) return;

            try
            {
                AssetUnloadPatch.RunIfDue();

                if (Cfg.FloatingDropsRun.Value && ZNet.instance != null &&
                    ZNet.instance.IsServer() && ZDOMan.instance != null)
                {
                    FloatingDrops.Run(Cfg.FloatingDropsDelete.Value, SmoothServerPlugin.Log);
                    Cfg.FloatingDropsRun.Value = false;
                    SmoothServerPlugin.Cfg.Save();
                }

                DirtyWatchdog(dt);
                Stats(dt);
            }
            catch (Exception e)
            {
                SmoothServerPlugin.Log.LogError("[ValheimTuneBridge] runtime tick failed: " + e);
            }
        }

        private static void DirtyWatchdog(float dt)
        {
            _watchdogTimer += dt;
            if (_watchdogTimer < 10f) return;
            _watchdogTimer = 0f;

            long received = DirtyPatches.WatchdogRecv;
            long marks = DirtyPatches.WatchdogMarks;
            if (DirtyPatches.WatchdogShouldTrip(Cfg.DirtySets.Value, DirtyPatches.Disabled,
                                                received > 0, marks))
            {
                DirtyPatches.Disabled = true;
                SmoothServerPlugin.Log.LogError("[ValheimTuneBridge] dirty-set revision hook stopped firing; " +
                                                "falling back to vanilla scanning");
            }
            else if (DirtyPatches.WatchdogShouldRearm(DirtyPatches.Disabled, marks))
            {
                DirtyPatches.Disabled = false;
                SmoothServerPlugin.Log.LogWarning("[ValheimTuneBridge] dirty-set revision hook recovered; " +
                                                  "a full scan will re-arm each peer");
            }

            DirtyPatches.WatchdogRecv = 0;
            DirtyPatches.WatchdogMarks = 0;
        }

        private static void Stats(float dt)
        {
            MeasurePatches.FrameMs.Add(dt * 1000f);
            int interval = Cfg.LogIntervalSeconds.Value;
            if (interval <= 0)
            {
                MeasurePatches.ResetAll();
                DirtyPatches.ResetCounters();
                RenderMeshPatch.Skipped = 0;
                return;
            }

            _logTimer += dt;
            if (_logTimer < interval) return;
            _logTimer = 0f;

            ZDOMan man = ZDOMan.instance;
            int peers = man == null ? 0 : man.m_peers.Count;
            int sent = man == null ? 0 : man.GetSentZDOs();
            int recv = man == null ? 0 : man.GetRecvZDOs();

            SmoothServerPlugin.Log.LogInfo(
                "[ValheimTuneBridge] frame avg " + MeasurePatches.FrameMs.Avg.ToString("F1") +
                " max " + MeasurePatches.FrameMs.Max.ToString("F1") + " ms | syncList avg " +
                MeasurePatches.SyncListMs.Avg.ToString("F2") + " ms | send avg " +
                MeasurePatches.SendMs.Avg.ToString("F2") + " ms | Z max " + MeasurePatches.MaxZ +
                " | peer-sends " + MeasurePatches.Rounds + " | zdos/s sent " + sent +
                " recv " + recv + " | peers " + peers + " | marks " + DirtyPatches.Marks +
                " full " + DirtyPatches.FullScans + " dirtyRounds " + DirtyPatches.DirtyRounds +
                " deferred " + DirtyPatches.Deferred + " drained " + DirtyPatches.LastDrained +
                (DirtyPatches.Disabled ? " DISABLED" : "") +
                " | meshSkips " + RenderMeshPatch.Skipped);

            RenderMeshPatch.Skipped = 0;
            DirtyPatches.ResetCounters();
            MeasurePatches.ResetAll();
        }

        internal static void Shutdown()
        {
            Active = false;
            _started = false;
            try { _harmony?.UnpatchSelf(); }
            catch (Exception e) { SmoothServerPlugin.Log?.LogWarning("[ValheimTuneBridge] unpatch failed: " + e.Message); }
            _harmony = null;
        }
    }
}
