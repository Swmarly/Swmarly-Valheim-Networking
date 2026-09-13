// -----------------------------------------------------------------------------
// File role: Tiny counter for repeated object/update churn.
// Why it exists: Counters keep hot paths cheap while allowing a later diagnostic summary to show whether a mitigation is doing work.
// Change contract: Keep it allocation-free and resettable.
// -----------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;

namespace ValheimTune
{
    // Counts occurrences per int key and reports the top N. Used for "which prefabs churn".
    public sealed class ChurnTally
    {
        private readonly Dictionary<int, int> _counts = new Dictionary<int, int>();
        public int Total { get; private set; }

        public void Add(int key)
        {
            _counts.TryGetValue(key, out int n);
            _counts[key] = n + 1;
            Total++;
        }

        public List<KeyValuePair<int, int>> Top(int n) =>
            _counts.OrderByDescending(kv => kv.Value).Take(n).ToList();

        public void Reset()
        {
            _counts.Clear();
            Total = 0;
        }
    }
}
