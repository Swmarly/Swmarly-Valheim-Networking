// -----------------------------------------------------------------------------
// File role: Pure version allow-list predicate used by the global compatibility gate.
// Why it exists: Version policy should be deterministic and testable without loading Unity or Valheim assemblies.
// Change contract: Do not make this method infer compatibility from a version prefix.
// -----------------------------------------------------------------------------
using System;
using System.Linq;

namespace ValheimTune
{
    // Pure: no game types. Gates the replacement patches on a known game build.
    public static class Compat
    {
        public static bool ReplacementsAllowed = true;
        public static string GameVersion = "?";
        public static string NetworkVersion = "?";
        public static string ValidationSummary = "not-run";

        // Comma-separated list, whitespace tolerated, exact match on the trimmed entries.
        public static bool IsKnown(string version, string knownList)
        {
            if (string.IsNullOrEmpty(knownList)) return false;
            return knownList.Split(',').Any(s => s.Trim() == version);
        }
    }
}
