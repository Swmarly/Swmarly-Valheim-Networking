# Changelog — Swmarly Valheim Networking

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
