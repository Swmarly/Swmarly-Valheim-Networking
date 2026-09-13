# Configuration reference

Code descriptions are the operator-facing source of truth. Update this page and the generated config description whenever semantics or defaults change.

| Section/key | Default | Owner | Meaning |
| --- | ---: | --- | --- |
| General/Mode | Auto | Plugin | Local. Batch mode runs server half; other processes run client half. |
| General/SteamSelfTest | false | SteamSelfTest | Local, observation-only Steam interface/IL report. |
| General/HotReload | true | ConfigWatcher | Local debounced config reload. |
| General/EnforceClientMod | false | Plugin/ServerSync | Synced. Requires clients to have this version or newer when enabled. |
| Sync/TargetPortalAwareSync | true | TargetPortalCompat/DirtyPatches | Keeps ordinary dirty rounds active with TargetPortal; forced sends get one vanilla sync-list pass. False selects full vanilla sync. |
| Compat/KnownGoodBuilds | 1.0.7,1.0.12 | Bridge | Explicit replacement-patch allow-list. |
| Compat/DisableOnUnknownBuild | true | Bridge | Legacy entry; current bridge still requires allow-list plus preflight. |
| Measure/LogIntervalSeconds | 10 | Bridge | Bridge timing/stat interval; zero disables its line. |
| Receive/MaxPacketsPerPeerPerFrame | 0 | ReceiveCapPatch | Positive values cap one peer's receive drain; zero is vanilla. |
| Sync/DirtySets | true | DirtyPatches | Changed-ZDO FIFO for ordinary rounds. |
| Sync/DirtyMaxItemsPerRound | 4096 | DirtyPatches | Maximum queued entries inspected per peer; unfinished entries remain queued. |
| Sync/ReconcileSeconds | 30 | DirtyPatches | Periodic full-scan safety net. |
| Sync/RelayMinIntervalMs | 0 | DirtyPatches | Low-priority resend throttle; zero is vanilla. |
| Sync/SendWindowBytes | 10240 | TopK | Legacy fallback for deriving TopK when TopK is zero; not the active send budget. |
| Sync/TopKSort | true | SortPatches | Bounded candidate selection during joins/reconciliation. |
| Sync/TopK | 0 | TopK | Zero derives a bounded count from SendWindowBytes. |
| Server/SkipRenderMesh | false | RenderMeshPatch | Dedicated-server render-mesh skip; collision work remains. |
| Server/DeferAssetUnload | false | GcThrottle/AssetUnloadPatch | Defers unused-asset collection while players are active. |
| Server/AssetUnloadMaxDeferMinutes | 240 | AssetUnload | Backstop for deferred collection. |
| Cleanup/FloatingDropsRun | false | FloatingDrops | One-shot scan on reload. |
| Cleanup/FloatingDropsDelete | false | FloatingDrops | Explicit destructive deletion switch; false is dry run. |

## Auto-discovered module sections

Every normal FeatureModule gets [ModuleName]/Enabled. Unless noted, the default is true.

| Module | Side | Default | Purpose |
| --- | --- | ---: | --- |
| AdaptiveBudget | Server | true | Per-peer bulk budget from valid transport observations. |
| AsyncSave | Server | true | Save timing and optional API-dependent clone pre-sizing. |
| CreateBudget | Server | true | Bound object creation per frame. |
| FrameRate | Server | true | Apply Unity frame target; TargetFrameRate=0 is no-op. |
| GcThrottle | Server | true | Defer unused-asset collection under policy. |
| LowLatency | Both | true | Steam low-latency/Nagle policy. |
| OwnershipRelease | Server | true | Validated ownership-release interval. |
| PeerTelemetry | Server | true | Read-only peer transport/ZDO sampling. |
| SendBudget | Server | true | Static bulk send high-water/minimum chunk. |
| SendCadence | Server | true | Fair peer deadline scheduler. |
| SendQueueGuard | Server | true | Queue protection and diagnostics. |
| StatsLog | Server | true | Persistent JSONL; observation-only. |
| SteamRates | Server | true | Steam rate/buffer tuning. |
| Telemetry | Server | true | Frame/ZDO console line; observation-only. |
| VPOServer | Server | true | Support-cache and release-scan optimizations. |
| Compression | Both | true | Negotiated Zstandard framing. |
| ClientNet | Client | true | Client send and Steam tuning. |
| SharedMap | Both | false | Optional map exploration/pin synchronization. |
| MapSelfTest | Server | false | Optional map codec tests. |
| SmoothMotion | Client | false | Optional interpolation/extrapolation. |

## Important interpretation rules

- Applied means a patch installed; it does not mean the configured value differs from vanilla.
- AdaptiveBudget uses static fallback limits when Steam status is unavailable; zero samples are not adaptive data.
- AsyncSave pre-sizing is n/a when ZDOMan.GetSaveClone is absent.
- TargetFrameRate=0, receive cap=0, relay throttle=0, and similar values intentionally preserve vanilla behavior.
- Enabled changes can be live when the module handles them. Target resolution, file paths, and other installation-time settings generally need restart.
- The full TargetPortal fallback is automatic if ForceSendZDO cannot be proven, and explicit with TargetPortalAwareSync=false.