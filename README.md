# Swmarly Valheim Networking

One BepInEx 5 plugin for smoother Valheim multiplayer on dedicated servers. It improves scheduling, bulk ZDO synchronization, queue protection, transport diagnostics, and selected server workload without changing Valheim's wire protocol.

## Design goals

- Stable latency for several peers, including players spread across distant parts of a large world.
- Vanilla clients supported by default; optional client features are separate.
- Per-peer work and budgets so one slow connection does not control everyone.
- Explicit diagnostics for frame stalls, ZDO rates, queues, Steam status, saves, GC, and budgets.
- Fail-closed compatibility checks instead of silently running stale Harmony IL.

## Main parts

| Part | What it does |
| --- | --- |
| SendCadence | Fair per-peer deadlines and bounded catch-up to reduce synchronized send bursts. |
| SendBudget and AdaptiveBudget | Bound bulk ZDO work and adapt per peer only from valid transport observations. |
| SendQueueGuard and Compression | Protect the Steam drain and optionally frame negotiated Zstandard traffic; vanilla peers remain unframed. |
| DirtyPatches | Process changed ZDOs from per-peer queues, with full-scan recovery and stale-object removal. |
| TargetPortalCompat | Keeps ordinary dirty synchronization optimized while routing forced TargetPortal records through one vanilla sync-list pass per affected peer. |
| Server safeguards | Bound object creation, ownership/support work, save/GC/render-mesh hitches, and frame policy. |
| Diagnostics | Console Telemetry/PeerTelemetry plus persistent StatsLog JSONL records. |

## Compatibility

Version 0.1.12 is verified for Valheim 1.0.7/network 39 and 1.0.12/network 40 with BepInEx 5.4.2350. The replacement gate requires an explicit KnownGoodBuilds entry and a passing live assembly/IL preflight. Unknown or structurally changed builds keep unsafe replacements vanilla.

TargetPortal 1.2.6 is supported. With Sync/TargetPortalAwareSync=true, normal rounds use dirty sets, while global or per-peer ForceSendZDO calls cause one vanilla CreateSyncList pass for the affected peer. If the required overloads change, or the setting is false, the full sync-list path falls back to vanilla.

## Installation

Install the package contents into BepInEx/plugins/SwmarlyValheimNetworking/. Do not run SmoothServer, ValheimTune, FiresGhettoNetworking, BetterNetworking, ServerSideMap, or Serverside Simulations alongside it; PatchGuard refuses overlapping foreign Harmony owners.

The generated config is BepInEx/config/Swmarly.ValheimNetworking.cfg. See docs/AGENT_GUIDE.md, docs/MODULES.md, and docs/CONFIGURATION.md for the complete architecture and maintenance contract.

## Diagnostics

Telemetry shows frame/fps/worst-frame, peers, ZDO rates, ZDO count, and scene objects. PeerTelemetry adds per-peer transport validity/reason, RTT/quality when available, pending/in-flight bytes, socket queue, ZDO queue, and force/invalid counts. StatsLog writes stats-YYYY-MM-DD.jsonl and events-YYYY-MM-DD.jsonl under BepInEx/config/smoothserver/stats/ by default.

Zero values with a reason such as Steam status unavailable are missing measurements, not proof of a healthy connection. Use tools/analyze.py to correlate persistent records with host samples.

## Build

~~~bash
export VALHEIM_MANAGED=/path/to/valheim_server_Data/Managed
export VALHEIM_BEPINEX_CORE=/path/to/Valheim/BepInEx/core
dotnet build src -c Release
python3 scripts/package.py
~~~

Read the agent guide before changing compatibility or Harmony seams.