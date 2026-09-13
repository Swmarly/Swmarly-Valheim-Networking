// -----------------------------------------------------------------------------
// File role: Shared configuration entries used by the ValheimTune bridge and its patches.
// Why it exists: These values are read by several patch classes, so one binding location prevents drift between the config file, runtime behavior, and diagnostics.
// Change contract: Update descriptions whenever semantics or defaults change; descriptions are part of the operator contract.
// -----------------------------------------------------------------------------
using BepInEx.Configuration;

namespace ValheimTune
{
    public static class Cfg
    {
        public static ConfigEntry<int> LogIntervalSeconds;
        public static ConfigEntry<int> SendWindowBytes;
        public static ConfigEntry<int> MaxPacketsPerPeerPerFrame;
        public static ConfigEntry<bool> FloatingDropsRun, FloatingDropsDelete;
        public static ConfigEntry<bool> DirtySets;
        public static ConfigEntry<bool> TargetPortalAwareSync;
        public static ConfigEntry<int> DirtyMaxItemsPerRound;
        public static ConfigEntry<float> ReconcileSeconds;
        public static ConfigEntry<int> RelayMinIntervalMs;
        public static ConfigEntry<string> KnownGoodBuilds;
        public static ConfigEntry<bool> DisableOnUnknownBuild;
        public static ConfigEntry<bool> TopKSort;
        public static ConfigEntry<int> TopK;
        public static ConfigEntry<bool> ServerSkipRenderMesh;
        public static ConfigEntry<bool> DeferAssetUnload;
        public static ConfigEntry<int> AssetUnloadMaxDeferMinutes;

        public static void Bind(ConfigFile c)
        {
            LogIntervalSeconds = c.Bind("Measure", "LogIntervalSeconds", 10, "How often to print the stats line. 0 disables.");
            SendWindowBytes  = c.Bind("Sync", "SendWindowBytes", 10240, new ConfigDescription("Legacy fallback size used to derive TopK when TopK=0; the active SendBudget high-water mark controls ZDO budgeting. Vanilla 10240. Try 32768.", new AcceptableValueRange<int>(4096, 1048576)));
            MaxPacketsPerPeerPerFrame = c.Bind("Receive", "MaxPacketsPerPeerPerFrame", 0, "Stop draining one peer's socket after this many packets in a frame; the rest wait in Steam's queue. 0 = vanilla (unlimited). Try 64.");
            FloatingDropsRun = c.Bind("Cleanup", "FloatingDropsRun", false, "One-shot trigger: set true to scan for item drops floating in water. The plugin runs it on the next config reload and sets this back to false. Measured ~50 ms of main-thread stall on a 698k-ZDO world (one 67 ms frame, 2026-09-07).");
            FloatingDropsDelete = c.Bind("Cleanup", "FloatingDropsDelete", false, "When a scan runs with this true, the found items are DELETED (server takes ownership and destroys them; clients see them vanish). Leave false for a dry run that only logs counts.");
            DirtySets        = c.Bind("Sync", "DirtySets", true, "B1: only consider changed ZDOs each round instead of rescanning the whole active area. Full scan on join, zone change, and every ReconcileSeconds.");
            TargetPortalAwareSync = c.Bind("Sync", "TargetPortalAwareSync", true,
                "When TargetPortal is loaded, keep dirty-set synchronization for ordinary rounds and route its forced portal sends through one vanilla sync-list pass. Set false for full vanilla sync-list behavior.");
            DirtyMaxItemsPerRound = c.Bind("Sync", "DirtyMaxItemsPerRound", 4096,
                new ConfigDescription("Maximum queued dirty/full-scan entries inspected for one peer in one SendZDOs call. " +
                    "A bounded FIFO prevents a 600k-ZDO full scan from being walked repeatedly in one frame. " +
                    "Unfinished entries remain queued.", new AcceptableValueRange<int>(256, 32768)));
            ReconcileSeconds = c.Bind("Sync", "ReconcileSeconds", 30f, "Safety-net full scan interval per peer when DirtySets is on.");
            RelayMinIntervalMs = c.Bind("Sync", "RelayMinIntervalMs", 0, "Do not re-send a non-prioritized object (fish, items, pieces) to the same peer more often than this, in ms. 0 = vanilla. 200 = 5 Hz; fish and drifting items are the bulk of idle traffic at a big base.");
            KnownGoodBuilds = c.Bind("Compat", "KnownGoodBuilds", "1.0.7,1.0.12", "Game versions (Version.CurrentVersion) this build was explicitly verified against. Comma-separated. Unknown versions remain vanilla until reviewed and added.");
            // Upgrade the old 0.1.4/0.1.5 shipped default in-place. Deliberately narrow: a
            // custom allow-list is never rewritten; the bridge still requires its preflight.
            if (KnownGoodBuilds.Value.Trim() == "1.0.7")
                KnownGoodBuilds.Value = "1.0.7,1.0.12";
            DisableOnUnknownBuild = c.Bind("Compat", "DisableOnUnknownBuild", true, "If the game version is not explicitly verified or runtime structural validation fails, keep the plugin loaded but disable optional Harmony/runtime features so the game remains vanilla.");
            TopKSort = c.Bind("Sync", "TopKSort", true, "B2: bounded-heap selection of the objects that fit the send window instead of a full sort of every candidate. Only matters during joins, zone changes and the reconcile scan.");
            ServerSkipRenderMesh = c.Bind("Server", "SkipRenderMesh", false, "B4a: skip Heightmap.RebuildRenderMesh on a dedicated server. The render mesh is rebuilt for every ghost zone a player explores and every terrain edit that loads with one, and never drawn on a -nographics process. Collision mesh untouched. Watch meshSkips on the stats line.");
            DeferAssetUnload = c.Bind("Server", "DeferAssetUnload", false, "G1: hold the hourly Resources.UnloadUnusedAssets() until no players are connected. Vanilla runs it every hour regardless (Game.cs:239); measured 443-607 ms of main-thread stall on a 698k-ZDO world, landing with players online. Deferred, not skipped: it still runs the moment the server empties, or after AssetUnloadMaxDeferMinutes whichever comes first.");
            AssetUnloadMaxDeferMinutes = c.Bind("Server", "AssetUnloadMaxDeferMinutes", 240, new ConfigDescription("Backstop for DeferAssetUnload: run the collection even with players online once it has been held this long. Stops a server that never empties from never collecting.", new AcceptableValueRange<int>(10, 1440)));
            TopK     = c.Bind("Sync", "TopK", 0, new ConfigDescription("How many candidates to order per round. 0 = SendWindowBytes / 64 (512 at 32 KB), never below 64.", new AcceptableValueRange<int>(0, 8192)));
        }
    }
}
