# Swmarly Valheim Networking — agent and maintainer guide

Read this before changing a Harmony target, transport setting, compatibility check, or package layout. The source files also begin with short purpose/why/change-contract headers.

## Project goal

One BepInEx 5 plugin for stable, low-latency Valheim dedicated-server multiplayer. The design is server-first, keeps vanilla clients working by default, preserves Valheim's wire protocol, and improves scheduling, queue behavior, diagnostics, and avoidable server hitches.

The important test case is a Linux dedicated server with roughly 600,000 ZDOs and up to six players spread across distant areas. A change that helps one nearby player but creates a scan or frame burst for six peers is not a successful change.

## Compatibility policy

Version 0.1.12 is verified for Valheim 1.0.7/network 39 and 1.0.12/network 40 with BepInEx 5.4.2350. Replacement patches require both an explicit KnownGoodBuilds match and a passing RuntimeCompatValidator preflight for methods, fields, signatures, Steam APIs, and asserted IL literals. Unknown or structurally changed versions stay vanilla.

Telemetry, PeerTelemetry, and StatsLog are observation-only and can still run when replacement patches are gated off. Do not make a failed assertion warning-only. DisableOnUnknownBuild is a legacy config entry; the bridge's allow-list and preflight are authoritative.

## Lifecycle

Plugin.cs is the only BepInEx entrypoint. Awake binds ServerSync and local config, resolves Server/Client side, detects overlapping mods, validates and starts ValheimTuneBridge, discovers every FeatureModule by reflection, configures modules, applies Profiles, installs module patches, installs the ZNet.Start summary hook, and starts ConfigWatcher.

Update ticks Telemetry, FrameRate, Compression, SharedMap, ServerModules, ValheimTuneBridge measurements/watchdog, and the main-thread ConfigWatcher pump. Shutdown disables modules, unpatches the bridge and bootstrap hook, and disposes the watcher.

FeatureModule owns per-module config, side checks, compatibility checks, a private Harmony instance, status reporting, and live Enabled changes. A module must leave a clear vanilla fallback and throw on target/IL mismatch.

### Harmony state invariant

Harmony passes prefix state to a postfix through one parameter named __state. Do not add a second pseudo-state parameter such as __portalVanilla; Harmony will try to resolve it as an original-method argument and the patch will fail. DirtyPatches uses one SyncListState value containing both FullScan and PortalVanilla. When both flags are true, the postfix must consume the TargetPortal marker and still refill the dirty queue from the vanilla full-scan list.

## Patch ownership

| Seam | Internal owner(s) | Purpose |
| --- | --- | --- |
| ZDOMan.SendZDOToPeers2(float) | SendCadence | Fair per-peer deadlines and bounded catch-up. |
| ZDOMan.SendZDOs(ZDOPeer,bool) | SendBudget, AdaptiveBudget, MeasurePatches | Bulk budget, adaptive budget, and measurement. |
| ZSteamSocket.SendQueuedPackages() | SendQueueGuard, Compression | Queue protection and negotiated framing. |
| ZSteamSocket.RegisterGlobalCallbacks() | SteamRates, LowLatency | Side-correct Steam rate and latency settings. |
| ZDOMan.CreateSyncList | DirtyPatches, MeasurePatches | Dirty discovery/recovery and measurement. |
| ZDO.DataRevision/OwnerRevision | DirtyPatches | Enqueue changed IDs for every peer. |
| ZDOMan.ServerSortSendZDOS | SortPatches/TopK | Bounded candidate selection for joins/reconciliation. |
| ZRpc.Update(float) | ReceiveCapPatch | Per-peer receive fairness; zero is vanilla. |
| ZNetScene.CreateObjects | CreateBudget | Spread object creation across frames. |
| ZDOMan.ReleaseZDOS | OwnershipRelease | Validated ownership-release interval. |
| WearNTear and ReleaseNearbyZDOS | VPOServer | Support caching and bounded ownership scans. |
| ZSyncTransform.SyncPosition | SmoothMotion | Optional client-only presentation smoothing. |

PatchGuard rejects foreign Harmony owners on critical seams. Internal modules may compose because they are one plugin; an unrelated networking mod must not be installed alongside this one.

## TargetPortal

TargetPortal 1.2.6 uses ForceSendZDO for portal records and needs ordinary sync-list behavior during travel. With Sync/TargetPortalAwareSync=true, TargetPortalCompat detects both global and per-peer ForceSendZDO overloads, records force events, leaves ordinary CreateSyncList calls on dirty-set synchronization, and lets the affected peer execute one vanilla CreateSyncList pass. DirtyPatches still marks known out-of-area objects for invalid-sector removal after departure.

If either ForceSendZDO overload is absent or ambiguous, or the setting is false, the complete sync-list path falls back to vanilla. This is a targeted fallback, not the normal behavior, and it does not patch TargetPortal or change the wire protocol.

## Synchronization model

Each peer has its own known-ZDO set and simulation area. DirtyPatches marks changed ZDO IDs, deduplicates them in a per-peer FIFO, checks existence/area/ShouldSend/relay policy, requeues deferred IDs, and marks known out-of-area IDs for removal. Full scans remain for join/reactivation, zone changes, and reconciliation.

Six distant players do not each receive all 600,000 ZDOs. The expensive cases are initial joins, zone transitions, reconciliation, and bursts of changes. SendCadence, DirtyMaxItemsPerRound, SendBudget, AdaptiveBudget, and the soft work budget control those cases. Never discard deferred or invalid IDs: stale players, mobs, portals, containers, or pieces can remain visible to one peer.

## Transport and diagnostics

SendCadence chooses when a peer is serviced. SendBudget bounds bulk ZDO work. AdaptiveBudget uses valid queue/Steam observations and an explicit fallback when status is unavailable. SendQueueGuard protects the drain and records emergency actions. SteamRates and LowLatency configure transport but do not prove health. Compression frames only after negotiation; vanilla peers remain unframed.

Telemetry logs frame/fps/worst-frame, peer, ZDO, and scene counts. PeerTelemetry reports validity/reason, RTT/quality when available, pending/in-flight bytes, Steam rate, socket queue, ZDO queue, and force/invalid counts. StatsLog writes stats-YYYY-MM-DD.jsonl and events-YYYY-MM-DD.jsonl under BepInEx/config/smoothserver/stats/ by default. tools/analyze.py correlates these with host samples from tools/collect-host.sh.

Zero values accompanied by steam status unavailable are missing observations, not proof of a healthy connection.

## Release/change procedure

1. Read README.md, docs/MODULES.md, docs/CONFIGURATION.md, and this guide.
2. Identify the existing owner of the target seam and inspect PatchGuard.
3. Preserve vanilla clients and the wire protocol unless the task explicitly changes them.
4. Keep target/signature/IL assertions strict and document every fallback.
5. Update source headers, config descriptions, README, and module docs when behavior changes.
6. Run Python and shell checks, live assembly validation, dotnet build, and scripts/package.py.
7. Inspect ZIP root, manifest, icon, DLL, runtime dependencies, and license notices.
8. Live-test portals, reconnects, zone changes, saves, large bases, vanilla/modded clients, and spread-out peers. CI compilation is not gameplay validation.
9. For a runtime release, update PluginVersion, csproj Version, manifest version_number, and CHANGELOG together.

## Complete repository map

### First-party runtime files
- src/Plugin.cs — entrypoint, side resolution, config sync, module discovery, Update, shutdown.
- src/FeatureModule.cs — module lifecycle, status, side and compatibility gates.
- src/ConfigWatcher.cs — debounced main-thread config reload.
- src/Profiles.cs — Default/FastLink/Custom preset policy.
- src/RuntimeCompatValidator.cs — reflection, field, Steam API, and IL preflight.
- src/PatchGuard.cs — foreign Harmony-owner refusal.
- src/SteamTransport.cs — wrapper-safe Steam socket/interface/handle helpers.
- src/TelemetryModule.cs — observation-only frame/ZDO logging.
- src/FrameRateModule.cs — Unity frame target policy.
- src/LowLatencyModule.cs — both-end low-latency settings.
- src/SendCadenceModule.cs — fair per-peer send scheduler.
- src/SendBudgetModule.cs — SendZDOs bulk window.
- src/CreateBudgetModule.cs — CreateObjects budget.
- src/ValheimTuneBridge.cs — selected ValheimTune patches, watchdog, measurements, and TargetPortal hook installation.

### Server, client, map, and bridge code
- src/Modules/Server/ — AdaptiveBudget, AsyncSave, GcThrottle, OwnershipRelease, PeerTelemetry, SendQueueGuard, ServerTick, StatsLog, SteamRates, SteamSelfTest, and VPOServer.
- src/Modules/Client/SmoothMotionModule.cs — optional client interpolation/extrapolation.
- src/Modules/Net/ClientNetModule.cs — client send/Steam tuning.
- src/Modules/Net/CompressionModule.cs — negotiated Zstandard framing.
- src/Modules/Map/SharedMapModule.cs — map RPC coordinator.
- src/Modules/Map/MapStore.cs — map/pin data, validation, serialization, persistence.
- src/Modules/Map/MapSelfTestModule.cs — opt-in map codec tests.
- src/ValheimTune/Cfg.cs — bridge config descriptions and entries.
- src/ValheimTune/Compat.cs — pure allow-list predicate.
- src/ValheimTune/DirtyPeerState.cs — per-peer dirty FIFO and reconciliation state.
- src/ValheimTune/TargetPortalCompat.cs — ForceSendZDO tracker.
- src/ValheimTune/TopK.cs — bounded heap.
- src/ValheimTune/AssetUnload.cs, FloatingDrops.cs, ChurnTally.cs, RollingStats.cs — policy, cleanup, counters, and statistics helpers.
- src/ValheimTune/Patches/ — narrow adapters for asset unload, dirty sync, measurements, receive caps, render meshes, and sorting.

### Build, package, and operations
- src/SmoothServer.csproj and src/Directory.Build.props — net472 build/reference/output settings.
- scripts/validate_valheim_assemblies.py — CI static contract check.
- scripts/package.py — Thunderstore staging and ZIP assembly.
- .github/workflows/build.yml — obtains live assemblies, validates, builds, packages, and uploads.
- thunderstore/ — package manifest, README, changelog, and icon.
- LICENSE, THIRD_PARTY.md, src/Vendor/*-LICENSE.txt — legal notices; ServerSync.cs is vendored code and must not be treated as first-party.
- tools/analyze.py — StatsLog report generator.
- tools/collect-host.sh — host/container sampler.
- tools/cfg.py — config helper; verify paths before use.
- tools/cutover/* — deployment/cutover helpers.
- tools/gameday/* — fetch, decompile, rebuild, and smoke helpers.
- tools/smoothserver-host-collect.service/.timer — optional systemd collection.
- tools/README.md — operational tool instructions.

Scripts and systemd units are environment-specific operational aids, not DLL runtime code.