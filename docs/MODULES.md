# Module reference

Every first-party C# file begins with a purpose/why/change-contract header. The maintainer-level lifecycle and interaction rules are in AGENT_GUIDE.md.

| Module/file | Side | Target/role | Summary |
| --- | --- | --- | --- |
| SmoothServerPlugin | both process types | entrypoint | Owns config, side, conflict detection, discovery, Update, and shutdown. |
| FeatureModule | framework | lifecycle | Applies enabled/side/compatibility gates and per-module Harmony ownership. |
| ValheimTuneBridge | server bridge | selected ValheimTune patches | Runs preflight, selected patches, watchdog, measurements, and TargetPortal hooks. |
| Compat / RuntimeCompatValidator / PatchGuard | policy | no gameplay result | Allow-list, structural/IL checks, and foreign-owner refusal. |
| Profiles | server-synced policy | no direct patch | Applies Default, FastLink, or Custom values before activation. |
| SendCadence | ZDOMan.SendZDOToPeers2 | server | Fair peer deadlines and bounded catch-up reduce same-frame bursts. |
| SendBudget | ZDOMan.SendZDOs | server | Static high-water/minimum bulk limits. |
| AdaptiveBudget | ZDOMan.SendZDOs | server | Valid per-peer queue/Steam observations adjust bulk target. |
| SendQueueGuard | ZSteamSocket.SendQueuedPackages | server | Protects and measures the drain; emergency trimming is lossy and explicit. |
| SteamRates / LowLatency | RegisterGlobalCallbacks | server/both | Correct Steam configuration per process side. |
| Compression | socket send/receive and routed RPCs | both | Negotiated optional framing; vanilla peers stay unframed. |
| DirtyPatches | ZDO revisions/CreateSyncList | server bridge | Dirty FIFO, full-scan recovery, invalid-sector cleanup, and TargetPortal hybrid pass. |
| MeasurePatches | sync/send/deserialize | server bridge | Read-only timings and counters. |
| SortPatches / TopK | ServerSortSendZDOS | server bridge | Bounded selection for large join/reconcile candidates. |
| ReceiveCapPatch | ZRpc.Update | server bridge | Per-peer receive cap; zero is vanilla. |
| CreateBudget | ZNetScene.CreateObjects | server | Bounds zone creation work. |
| OwnershipRelease / VPOServer | ownership/support methods | server | Validated release and support-cache optimizations with invalidation. |
| AsyncSave | save methods | server | Save phase measurement; pre-size only when API exists. |
| GcThrottle / AssetUnloadPatch | CollectResources | server | Defers expensive cleanup with a backstop. |
| RenderMeshPatch | RebuildRenderMesh | server bridge | Optional headless render-mesh skip. |
| FrameRate | Unity timing policy | server | Frame target, not network send rate. |
| PeerTelemetry / Telemetry / StatsLog | pollers | server | Read-only peer, frame, and persistent diagnostics. |
| ClientNet / SmoothMotion | client send/transform | client | Optional client-only tuning and presentation smoothing. |
| SharedMap / MapStore / MapSelfTest | map RPC/data | both/server | Optional map sync, storage validation, and tests. |
| SteamSelfTest / SteamTransport | diagnostics/helpers | server/both | Explain Steam interface and wrapper differences. |

## TargetPortal

With TargetPortalAwareSync=true, ordinary rounds stay dirty-set based. Tracked global or targeted ForceSendZDO calls cause one vanilla CreateSyncList pass for each affected peer. Invalid-sector cleanup remains active after portal departure. If the overloads are absent or ambiguous, or the setting is false, the entire sync-list path uses vanilla behavior.

## Critical seams

SendZDOs intentionally composes SendBudget, AdaptiveBudget, and measurement inside this plugin. SendQueuedPackages composes SendQueueGuard and Compression. RegisterGlobalCallbacks composes SteamRates and LowLatency. PatchGuard prevents foreign mods from joining those seams.