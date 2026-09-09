# Modules

The plugin discovers its modules at startup and applies only the side-appropriate features.
All replacement patches are guarded by the Valheim 1.0.7/network 39 compatibility gate.

| Area | Selected implementation | Notes |
| --- | --- | --- |
| ZDO send cadence and budgets | SmoothServer | Fixed cadence, adaptive per-peer budgets, queue back-pressure, and Steam rate tuning share one owner for the overlapping send path. |
| Changed-object discovery | ValheimTune | Dirty revision sets, periodic reconciliation, relay throttling, and a watchdog that falls back to vanilla scanning. |
| Candidate prioritization | ValheimTune | Top-K selection replaces a full sort only when enabled and only on the server. |
| Receive protection | ValheimTune | Optional per-peer packet cap prevents one busy connection monopolising a frame. |
| Transport compression | Unified implementation | Explicit frame tags, negotiated dictionary hash, vanilla-peer fallback, and double-compression protection. |
| Server simulation | SmoothServer/VPO-derived | WearNTear, ownership-release, save-clone, GC, render-mesh, and frame-budget safeguards. |
| Client presentation | SmoothServer | Optional client send budget, shared map, interpolation, and prediction controls. |
| Diagnostics | Unified bridge | Persistent stats plus ValheimTune sync/receive/dirty-set measurements. |

## Intentional exclusions

The original ValheimTune `ConstPatches` and overlapping `SendZDOs`/Steam/all-peer patches are not
loaded because the unified SmoothServer transport modules own those methods. The original
FiresGhettoNetworking binary is not bundled; its MIT license is preserved in
`src/Vendor/FiresGhettoNetworking-LICENSE.txt`, and only compatible, source-auditable features
are included in this first release. Server-authority simulation and wire-level ZDO delta
serialization remain separate future work because they need dedicated live-server validation and
must not silently change the protocol for vanilla clients.
