## 0.1.11 — deep networking quality pass

- Added a soft per-frame SendZDOs work budget with bounded carry-forward debt to reduce hitch amplification.
- Added an explicit exclusive-owner check for the send-drain Harmony seam.
- Added queue-age/deferral metrics to StatsLog and tolerant typed Steam connection-handle extraction.
- Corrected IL operand-width handling for ShortInlineI/ShortInlineBrTarget versus ShortInlineR.
- Preserved fail-closed validation for unverified Valheim internal changes and TargetPortal compatibility.

## 0.1.10 — network scheduling and transport hardening

- Replaced synchronized all-peer send sweeps with a bounded fair scheduler that preserves aggregate
  send cadence for normal 5–6 player server ticks while reducing same-frame bursts.
- Added wrapper-safe Steam socket discovery and per-peer Steam real-time status diagnostics.
- Applied validated Steam send-rate/Nagle settings at connection scope for existing peers.
- Added telemetry validity/reason fields and adaptive-budget grace handling for transient status misses.
- Made float IL validation accept both ldc.r4 and ldc.r8 while retaining strict instruction parsing.
- Clarified compatibility logs and made CI validate the live assembly surface instead of pinning one
  human-readable game version.
- Preserved TargetPortal's vanilla sync-list fallback.

# Changelog — Swmarly Valheim Networking

## 0.1.9 — forward-compatible runtime validation

- Compatible Valheim hotfixes and minor updates can remain active when the validated runtime surface is unchanged.
- Fundamental method, field, signature, Steam API, or asserted-IL changes still fail closed.
- KnownGoodBuilds remains an explicit verification record rather than the only activation gate.

## 0.1.8 — Valheim 1.0.12 runtime compatibility

- Recognize Valheim 1.0.12 `Version.c_networkVersion` and network version 40.
- Validate the verified `bool ZDOMan.SendZDOs(ZDOPeer, bool)` signature.
- Keep client-only `ZSyncTransform.SyncPosition` IL checks out of dedicated-server preflight.
- Preserve the explicit known-build allow-list and fail-closed behavior.

## 0.1.7 — release version correction

- Corrected the plugin, project, and Thunderstore package version to 0.1.7.
- No networking behavior or compatibility policy changed in this version.

## 0.1.4 — TargetPortal compatibility

- Automatically keeps vanilla ZDO sync-list handling when TargetPortal is loaded, preserving
  TargetPortal's forced portal advertisements and correct player removal after portal travel.
- The remaining networking, queue, compression, and performance modules stay enabled.

## 0.1.3 — portal sync fix

- Preserve vanilla invalid-sector removal when dirty-set synchronization drops a known ZDO
  outside a peer's active area, preventing players from remaining visible at their old portal.

## 0.1.2 — safer defaults

- Shared server-side map synchronization is now disabled by default.
- The optional map self-test is also disabled by default.

## 0.1.1 — compatibility and safety pass

- Fail closed on unknown game builds and detected overlapping networking/map mods.
- Added live module enable/disable lifecycle handling.
- Hardened compression handshakes, malformed-frame handling, shared-map validation, and map saves.
- Fixed package license notices and updated the BepInEx dependency to 5.4.2350.

## 0.1.0 — Valheim 1.0.7 / network version 39

- Initial unified release for Valheim 1.0.7.
- One BepInEx plugin identity combining the compatible transport, synchronization,
  server-performance, and client-smoothing features selected from SmoothServer, ValheimTune,
  and FiresGhettoNetworking.
- Safe compatibility gate: unsupported game builds leave replacement patches inactive instead of
  continuing against unknown method layouts.
- Vanilla clients remain supported by default; shared compression activates only after a safe
  per-peer handshake.
