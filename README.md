# Swmarly Valheim Networking — maintainer and agent guide

This is the context document for anyone changing this repository. Read it before changing a Harmony target, transport setting, compatibility check, or package layout.

## Mission

This is one BepInEx 5 plugin for Valheim. Its goal is stable, low-latency dedicated-server multiplayer with vanilla clients by default.

It optimizes Valheim's existing server loop rather than inventing a new network protocol:
- distribute per-peer ZDO work instead of synchronized bursts;
- bound bulk work and queue growth without casually dropping reliable gameplay messages;
- measure frame time, ZDO rates, transport queues, and Steam status so intermittent lag is diagnosable;
- reduce avoidable save, GC, object-creation, ownership, and support-physics hitches;
- keep optional client presentation features separate from server authority.

The important operating case is a Linux dedicated server with roughly 600,000 ZDOs and up to six players spread across distant areas. A change that helps one nearby player but creates a scan or frame burst for six peers is not a successful change.

## Compatibility contract

Release metadata is 0.1.12. The verified target set is Valheim 1.0.7/network 39 and Valheim 1.0.12/network 40 with BepInEx 5.4.2350. The target is the dedicated-server Steam app 896660 on Linux or Windows.

Replacement patches require both:
1. Compat.IsKnown must find the game version in Compat/KnownGoodBuilds.
2. RuntimeCompatValidator must pass the required methods, fields, signatures, Steam APIs, and asserted IL constants.

Unknown or structurally changed builds stay vanilla. Observation-only modules — Telemetry, PeerTelemetry, and StatsLog — can still run because they do not replace game methods. Never weaken an assertion just to make a module show as applied.

DisableOnUnknownBuild is a legacy configuration entry retained for config compatibility. The current bridge requires the allow-list and structural preflight; do not treat that entry as permission to activate an unverified build.

## Runtime lifecycle

SmoothServerPlugin in src/Plugin.cs is the only BepInEx entrypoint.

Startup order:
1. Bind plugin config and initialize ServerSync.
2. Resolve Server, Client, or Both process side.
3. Detect conflicting networking/map mods.
4. Bind and validate the ValheimTune bridge; install selected bridge hooks only when the global gate passes.
5. Reflect over the assembly and discover every FeatureModule subclass.
6. Configure modules and attach Enabled change handlers.
7. Apply the selected profile after all entries exist.
8. Enable modules and install their individual Harmony instances.
9. Patch ZNet.Start only to emit the module summary.
10. Start ConfigWatcher.

Every Unity Update runs Telemetry, FrameRate, Compression, SharedMap, ServerModules, the ValheimTune watchdog/measurements, and finally the main-thread config watcher pump. Shutdown disables modules, unpatches the bridge, disposes the watcher, and removes the bootstrap hook.

## Module rules

FeatureModule is the contract. A module declares its name, side, config, compatibility requirement, patches, status detail, and safe disable behavior. Modules are discovered by reflection; there is no second registry to edit.

Every replacement patch must:
- resolve an exact target and validate its signature;
- use PatchGuard on a critical seam;
- assert its IL match count where a transpiler depends on a literal or instruction shape;
- gate behavior on the module's IsActive and the correct server/client side;
- fail closed when a target changes;
- leave vanilla behavior available through an explicit fallback.

Each module owns a Harmony instance, so a module failure can be isolated. The global bridge itself is stricter: a selected bridge patch failure unpatches the bridge rather than leaving a partially modified networking stack.

## Critical patch ownership

| Seam | Internal owner | Reason |
| --- | --- | --- |
| ZDOMan.SendZDOToPeers2(float) | SendCadence | Fair per-peer deadlines and bounded catch-up avoid synchronized send bursts. |
| ZDOMan.SendZDOs(ZDOPeer,bool) | SendBudget, AdaptiveBudget, MeasurePatches | Static and adaptive bulk budgets compose with read-only measurement. |
| ZSteamSocket.SendQueuedPackages() | SendQueueGuard, Compression | Queue protection and optional negotiated framing share the drain seam. |
| ZSteamSocket.RegisterGlobalCallbacks() | SteamRates, LowLatency | Server/client Steam settings are applied at the correct lifecycle point. |
| ZDOMan.CreateSyncList | DirtyPatches, MeasurePatches | Dirty queues reduce area walks; measurement wraps the same call. |
| ZDO.DataRevision and OwnerRevision | DirtyPatches | Changed ZDO IDs enter each peer's deduplicated FIFO. |
| ZDOMan.ServerSortSendZDOS | SortPatches and TopK | Bounded candidate selection is used for joins/reconciliation. |
| ZRpc.Update(float) | ReceiveCapPatch | One peer cannot drain an unbounded number of packets in one frame. |
| ZNetScene.CreateObjects | CreateBudget | Zone creation work is spread across frames. |
| ZDOMan.ReleaseZDOS | OwnershipRelease | Only a validated ownership-release interval is changed. |
| WearNTear and ReleaseNearbyZDOS | VPOServer | Support/ownership work is cached or bounded with invalidation. |
| ZSyncTransform.SyncPosition | SmoothMotion, client only | Optional presentation smoothing; the most version-sensitive client seam. |

PatchGuard rejects foreign Harmony owners. Several modules may compose on a seam only because they are part of this single plugin. Do not install another networking mod beside it to compare behavior.

## TargetPortal 1.2.6

TargetPortal forces portal records with ZDOMan.ForceSendZDO and relies on normal sync-list behavior during portal travel. Compatibility is therefore a hybrid path, not a blanket disable:
- TargetPortalCompat detects the soft dependency.
- Both global ForceSendZDO(ZDOID) and targeted ForceSendZDO(peer,ZDOID) overloads are discovered and required.
- A postfix records the forced ZDO globally or for the affected peer.
- Ordinary CreateSyncList calls continue through dirty-set synchronization.
- The next call for an affected peer runs vanilla CreateSyncList once, preserving force-send selection and cleanup.
- DirtyPatches still adds known out-of-area ZDOs to invalid-sector cleanup when a player leaves through a portal.
- The marker is consumed only after that peer's vanilla pass.
- If either overload is absent or ambiguous, or TargetPortalAwareSync is false, the whole sync-list path falls back to vanilla.

This code does not patch TargetPortal or change the wire protocol.

## Synchronization and spread-out players

Valheim maintains a known-ZDO set per peer. Each peer has its own simulation area. DirtyPatches enqueues changed IDs, checks existence/area/ShouldSend/relay policy, requeues deferred entries, and marks known out-of-area entries for removal. Full scans remain for join/reactivation, zone changes, and reconciliation.

Six distant players do not each receive all 600,000 ZDOs. The expensive cases are initial joins, zone transitions, reconciliation, and bursts of changes. SendCadence, DirtyMaxItemsPerRound, SendBudget, AdaptiveBudget, and the soft work budget control those cases.

Never discard an invalid or deferred ID just because it is outside the current area. Stale player, mob, portal, container, or piece state can otherwise remain visible to one peer.

## Transport and latency

SendCadence decides when each peer is serviced. SendBudget limits bulk ZDO work. AdaptiveBudget changes a peer's target only from valid queue/transport observations and uses an explicit fallback when Steam data is unavailable. SendQueueGuard records queue age/bytes and emergency actions. SteamRates and LowLatency configure transport but do not prove connection health. Compression frames only after capability negotiation; vanilla peers stay unframed.

Any queue change must answer whether door, combat, portal, ownership, or inventory RPCs can be delayed behind bulk ZDOs; whether packets are dropped or merely deferred; whether ordering is preserved; and whether the delay is visible per peer.

## Diagnostics

Telemetry logs frame/fps/worst-frame, peer, ZDO, and scene counts. PeerTelemetry logs per-peer validity/reason, RTT/quality when available, pending and in-flight bytes, Steam rate, socket queue, ZDO queue, and force/invalid counts. StatsLog writes structured JSONL:
- stats-YYYY-MM-DD.jsonl — periodic snapshots;
- events-YYYY-MM-DD.jsonl — joins, leaves, save stalls, GC, queue drops, config reloads, and budget transitions.

Default location: BepInEx/config/smoothserver/stats/. tools/analyze.py reports sessions, frame percentiles, peer queues, adaptive backoff, compression, saves, ZDO growth, and worst windows. tools/collect-host.sh adds host/container samples.

Zero values with a reason such as steam status unavailable are missing observations, not proof of a healthy connection.

## Configuration and profiles

Plugin and bridge entries are documented in docs/CONFIGURATION.md. Generated BepInEx descriptions are part of the operator contract. Every new key must state whether it is local or synced, hot-reloadable or restart-only, its vanilla/no-op value, its owner, and the metric that proves it works.

Profiles are applied after every entry is bound and before patches install. When adding a setting, decide explicitly whether Default, FastLink, or Custom should change it.

## Change and release procedure

1. Read this guide, README.md, docs/MODULES.md, and docs/CONFIGURATION.md.
2. Identify the existing owner of the seam and inspect PatchGuard.
3. Make the smallest first-party change; do not edit src/Vendor/ServerSync.cs as if it were project code.
4. Keep wire compatibility and vanilla clients unless the task explicitly requires a protocol change.
5. Update source comments, config descriptions, module docs, and README when behavior changes.
6. Run Python/shell checks, live assembly validation, dotnet build, and scripts/package.py.
7. Inspect package root, manifest, icon, DLL, runtime dependencies, and licenses.
8. Live-test vanilla and modded clients, reconnects, zone transitions, portals, large bases, saves, and spread-out peers. CI compilation is not gameplay validation.
9. For a runtime release, update PluginVersion, csproj Version, manifest version_number, and thunderstore/CHANGELOG.md together.

## Repository map

### Runtime and policies
- src/Plugin.cs — sole BepInEx entrypoint and lifecycle.
- src/FeatureModule.cs — discovery/lifecycle/side/config/status framework.
- src/ConfigWatcher.cs — debounced main-thread config reload.
- src/Profiles.cs — server-selected presets.
- src/RuntimeCompatValidator.cs — reflection/IL/Steam preflight.
- src/PatchGuard.cs — foreign Harmony-owner refusal.
- src/SteamTransport.cs — wrapper-safe Steam discovery and interface helpers.
- src/ValheimTuneBridge.cs — selected ValheimTune lifecycle and TargetPortal hook installation.
- src/ValheimTune/Cfg.cs and Compat.cs — bridge settings and pure allow-list logic.

### First-party modules
- src/SendCadenceModule.cs, SendBudgetModule.cs, CreateBudgetModule.cs, FrameRateModule.cs, LowLatencyModule.cs, TelemetryModule.cs — core timing, send, creation, Steam, and observation features.
- src/Modules/Server/ — AdaptiveBudget, AsyncSave, GcThrottle, OwnershipRelease, PeerTelemetry, SendQueueGuard, ServerTick, StatsLog, SteamRates, SteamSelfTest, and VPOServer.
- src/Modules/Client/ — SmoothMotion.
- src/Modules/Net/ — ClientNet and Compression.
- src/Modules/Map/ — SharedMapModule, MapStore, and MapSelfTestModule.
- src/ValheimTune/Patches/ — AssetUnloadPatch, DirtyPatches, MeasurePatches, ReceiveCapPatch, RenderMeshPatch, and SortPatches.
- src/ValheimTune/DirtyPeerState.cs, TopK.cs, FloatingDrops.cs, AssetUnload.cs, ChurnTally.cs, and RollingStats.cs — bounded sync state, selection, cleanup, policy, and measurement helpers.

### Build, package, and operations
- src/SmoothServer.csproj and src/Directory.Build.props — net472 build and reference configuration.