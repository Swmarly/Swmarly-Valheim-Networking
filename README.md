# Swmarly Valheim Networking

One BepInEx 5 plugin combining the strongest compatible parts of SmoothServer, ValheimTune,
and FiresGhettoNetworking into a single server-first networking/performance stack.

## Target

- Valheim 1.0.7 and 1.0.12, network versions 39 and 40
- Dedicated-server Steam app 896660
- BepInEx 5.4.2350
- Linux and Windows dedicated servers

The compatibility gate defaults to safe mode. If Valheim changes its internal method signatures,
replacement patches are disabled and the server falls back to vanilla behaviour instead of
continuing with unverified IL.

## What is included

### Transport and synchronization

- Per-peer adaptive ZDO send budgets
- Queue back-pressure and recovery
- Fixed-rate all-peer send cadence
- Steam send-rate and buffer tuning
- Optional Nagle/low-latency tuning
- Zstandard compression with vanilla-peer fallback (plain zstd in the first package)
- Dirty-set synchronization with watchdog recovery
- TargetPortal compatibility: its explicit portal ZDO sends and portal-travel removals use the vanilla sync-list path
- Relay throttling for low-priority objects
- Top-K send selection during large joins
- Receive packet caps
- Persistent server/network statistics

### Server performance

- Dedicated-server frame-rate control
- Server-safe physics and WearNTear optimizations
- Ownership-release tuning
- Save-stall measurement (Valheim 1.0 owns save-clone sizing in vanilla)
- Garbage-collection and asset-unload throttling
- Headless render-mesh skip
- Floating item/log diagnostics and optional cleanup

### Optional client features

- Client send-budget tuning
- Compression when both sides support it
- Shared map support
- Player/object interpolation and prediction

### Selection notes

Overlapping methods have one owner: SmoothServer owns the transport cadence, adaptive budget,
queue, and Steam-rate path; ValheimTune contributes dirty-set discovery, relay throttling, Top-K
selection, receive caps, cleanup, and measurements. FiresGhettoNetworking's compatible tuning
ideas are represented by the Steam/queue/compression/WearNTear and client-smoothing modules.
Its original binary is not bundled, and protocol-changing server-authority or ZDO-delta patches
are intentionally outside this first release until they can be validated against vanilla and
modded clients on a live 1.0.7 server.

## Installation

Install the package on the dedicated server in `BepInEx/plugins/SwmarlyValheimNetworking/`.
Players can join without installing the mod unless the server owner explicitly enables
`[General] EnforceClientMod`.

Do not install SmoothServer, ValheimTune, FiresGhettoNetworking, BetterNetworking, ServerSideMap,
or Serverside Simulations alongside this plugin. They patch the same Valheim networking/map
methods and the merged plugin will fail closed when it detects one. TargetPortal is supported;
when it is loaded, dirty-set replacement is automatically bypassed so TargetPortal's forced
portal sends and vanilla portal-travel cleanup remain intact.

The generated config is `BepInEx/config/Swmarly.ValheimNetworking.cfg`.

## Building

Point the build at a Valheim dedicated-server installation and BepInEx core:

```bash
export VALHEIM_MANAGED=/path/to/valheim_server_Data/Managed
export VALHEIM_BEPINEX_CORE=/path/to/Valheim/BepInEx/core
dotnet build src -c Release
python3 scripts/package.py
```

The package contains the plugin, Zstandard runtime dependencies, Thunderstore metadata, and
the required third-party license notices.

## Credits and licenses

- SmoothServer by Matt Jensen / Nosferatu — MIT
- ValheimTune by Akoozie — MIT
- FiresGhettoNetworking by fire-VA — MIT; its license is included in `src/Vendor/`
- ServerSync and other inherited third-party components retain their upstream notices

This project is not affiliated with Iron Gate or Coffee Stain.
