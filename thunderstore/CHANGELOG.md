# Changelog — Swmarly Valheim Networking

## 0.1.0 — Valheim 1.0.7 / network version 39

- Initial unified release for Valheim 1.0.7.
- One BepInEx plugin identity combining the compatible transport, synchronization,
  server-performance, and client-smoothing features selected from SmoothServer, ValheimTune,
  and FiresGhettoNetworking.
- Safe compatibility gate: unsupported game builds leave replacement patches inactive instead of
  continuing against unknown method layouts.
- Vanilla clients remain supported by default; shared compression activates only after a safe
  per-peer handshake.
