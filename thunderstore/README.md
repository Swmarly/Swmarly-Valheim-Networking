# Swmarly Valheim Networking

A server-first BepInEx 5 networking and performance mod for Valheim.

## Features

- Fair per-peer ZDO scheduling and bounded bulk send budgets.
- Queue back-pressure and per-peer transport diagnostics.
- Optional negotiated Zstandard compression with vanilla-peer fallback.
- Dirty-set synchronization for ordinary rounds with full-scan recovery.
- TargetPortal 1.2.6 hybrid compatibility: optimized ordinary rounds and one controlled vanilla sync-list pass for forced portal advertisements.
- Server frame, save, GC, object-creation, ownership, and support-physics safeguards.
- PeerTelemetry, Telemetry, and persistent StatsLog diagnostics.

## Compatibility

Version 0.1.12 is verified for Valheim 1.0.7/network 39 and Valheim 1.0.12/network 40 with BepInEx 5.4.2350. Unknown or structurally changed builds remain vanilla until reviewed and added to the allow-list.

TargetPortal is a soft dependency. If its ForceSendZDO overloads change, the mod safely falls back to full vanilla sync-list handling. Set Sync/TargetPortalAwareSync=false for the same diagnostic fallback explicitly.

## Installation

Install the package contents into BepInEx/plugins/SwmarlyValheimNetworking/. Do not run SmoothServer, ValheimTune, FiresGhettoNetworking, BetterNetworking, ServerSideMap, or Serverside Simulations beside it; overlapping Harmony owners are refused.

The generated config is BepInEx/config/Swmarly.ValheimNetworking.cfg. The full maintenance guide and configuration reference are in the GitHub repository.

## Diagnostics

StatsLog writes stats-YYYY-MM-DD.jsonl and events-YYYY-MM-DD.jsonl under BepInEx/config/smoothserver/stats/ by default. Use tools/analyze.py to correlate frame spikes, peer queues, budgets, saves, GC, and ZDO rates.