# Swmarly Valheim Networking

Unified server-first Valheim networking and performance tuning.

## Features

- Adaptive per-peer send budgets and queue recovery
- Fixed-rate all-peer synchronization
- Zstandard compression with vanilla-peer fallback
- Dirty-set synchronization, relay throttling, and Top-K selection
- Receive caps and persistent diagnostics
- Steam send-rate, buffer, and low-latency tuning
- Dedicated-server frame, save, GC, WearNTear, and ownership tuning
- Optional client send budget, shared map, interpolation, and prediction
- Floating item/log diagnostics and cleanup

## Installation

Install `SwmarlyValheimNetworking.dll` and its bundled managed dependencies on the dedicated
server under `BepInEx/plugins/SwmarlyValheimNetworking/`. Vanilla clients can join by default.

Remove SmoothServer, ValheimTune, FiresGhettoNetworking, BetterNetworking, and Serverside
Simulations before enabling this package. Those mods patch overlapping Valheim networking methods.

Target build: Valheim 1.0.7 / network version 39 / BepInEx 5.4.2350.
