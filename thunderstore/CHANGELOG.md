# Changelog — Swmarly Valheim Networking

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
