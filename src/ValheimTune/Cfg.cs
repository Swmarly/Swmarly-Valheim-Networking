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
            SendWindowBytes  = c.Bind("Sync", "SendWindowBytes", 10240, new ConfigDescription("Per-peer bytes in flight before the server stops queueing ZDO data. Vanilla 10240. Try 32768. Also sizes each iteration of the disconnect flush.", new AcceptableValueRange<int>(4096, 1048576)));
            MaxPacketsPerPeerPerFrame = c.Bind("Receive", "MaxPacketsPerPeerPerFrame", 0, "Stop draining one peer's socket after this many packets in a frame; the rest wait in Steam's queue. 0 = vanilla (unlimited). Try 64.");
            FloatingDropsRun = c.Bind("Cleanup", "FloatingDropsRun", false, "One-shot trigger: set true to scan for item drops floating in water. The plugin runs it on the next config reload and sets this back to false. Measured ~50 ms of main-thread stall on a 698k-ZDO world (one 67 ms frame, 2026-09-07).");
            FloatingDropsDelete = c.Bind("Cleanup", "FloatingDropsDelete", false, "When a scan runs with this true, the found items are DELETED (server takes ownership and destroys them; clients see them vanish). Leave false for a dry run that only logs counts.");
            DirtySets        = c.Bind("Sync", "DirtySets", true, "B1: only consider changed ZDOs each round instead of rescanning the whole active area. Full scan on join, zone change, and every ReconcileSeconds.");
            ReconcileSeconds = c.Bind("Sync", "ReconcileSeconds", 30f, "Safety-net full scan interval per peer when DirtySets is on.");
            RelayMinIntervalMs = c.Bind("Sync", "RelayMinIntervalMs", 0, "Do not re-send a non-prioritized object (fish, items, pieces) to the same peer more often than this, in ms. 0 = vanilla. 200 = 5 Hz; fish and drifting items are the bulk of idle traffic at a big base.");
            KnownGoodBuilds = c.Bind("Compat", "KnownGoodBuilds", "1.0.7,1.0.12", "Game versions (Version.CurrentVersion) this build was verified against. Comma-separated. The shipped 0.1.8 defaults include Valheim 1.0.12/network 40.");
            // Upgrade the old 0.1.4/0.1.5 shipped default in-place. Deliberately narrow: a
            // custom allow-list is never rewritten; the bridge still requires its preflight.
            if (KnownGoodBuilds.Value.Trim() == "1.0.7")
                KnownGoodBuilds.Value = "1.0.7,1.0.12";
            DisableOnUnknownBuild = c.Bind("Compat", "DisableOnUnknownBuild", true, "If runtime structural validation fails, keep the plugin loaded but disable optional Harmony/runtime features so the game remains vanilla. KnownGoodBuilds records explicitly verified versions; structurally compatible hotfixes may run automatically.");
            TopKSort = c.Bind("Sync", "TopKSort", true, "B2: bounded-heap selection of the objects that fit the send window instead of a full sort of every candidate. Only matters during joins, zone changes and the reconcile scan.");
            ServerSkipRenderMesh = c.Bind("Server", "SkipRenderMesh", false, "B4a: skip Heightmap.RebuildRenderMesh on a dedicated server. The render mesh is rebuilt for every ghost zone a player explores and every terrain edit that loads with one, and never drawn on a -nographics process. Collision mesh untouched. Watch meshSkips on the stats line.");
            DeferAssetUnload = c.Bind("Server", "DeferAssetUnload", false, "G1: hold the hourly Resources.UnloadUnusedAssets() until no players are connected. Vanilla runs it every hour regardless (Game.cs:239); measured 443-607 ms of main-thread stall on a 698k-ZDO world, landing with players online. Deferred, not skipped: it still runs the moment the server empties, or after AssetUnloadMaxDeferMinutes whichever comes first.");
            AssetUnloadMaxDeferMinutes = c.Bind("Server", "AssetUnloadMaxDeferMinutes", 240, new ConfigDescription("Backstop for DeferAssetUnload: run the collection even with players online once it has been held this long. Stops a server that never empties from never collecting.", new AcceptableValueRange<int>(10, 1440)));
            TopK     = c.Bind("Sync", "TopK", 0, new ConfigDescription("How many candidates to order per round. 0 = SendWindowBytes / 64 (512 at 32 KB), never below 64.", new AcceptableValueRange<int>(0, 8192)));
        }
    }
}
