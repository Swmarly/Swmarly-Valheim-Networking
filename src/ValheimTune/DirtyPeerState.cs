// -----------------------------------------------------------------------------
// File role: Per-peer FIFO of changed ZDO IDs plus full-scan/reconciliation state.
// Why it exists: Dirty synchronization avoids repeatedly walking hundreds of thousands of ZDOs, but deferred/out-of-area IDs must remain recoverable so no peer silently misses state.
// Change contract: Preserve deduplication, bounded draining, deferral, and full-scan rearming.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace ValheimTune
{
    // Per-peer bookkeeping for B1. No game types: TId is ZDOID in production, int in tests.
    public sealed class DirtyPeerState<TId>
    {
        private (int x, int y) _lastZone = (int.MinValue, int.MinValue);
        private float _lastFullScan = float.NegativeInfinity;
        private bool _wasActive;
        private readonly Queue<TId> _work = new Queue<TId>();

        public HashSet<TId> Pending { get; } = new HashSet<TId>();
        public int FullScans { get; private set; }

        public void Enqueue(TId id)
        {
            if (Pending.Add(id)) _work.Enqueue(id);
        }

        // active reflects whether DirtySets was actually in effect (not disabled by the watchdog)
        // on this call. Coming back from an inactive round always forces a full scan.
        public bool NeedsFullScan((int x, int y) zone, float now, float reconcileSeconds, bool active)
        {
            bool full = !_wasActive || zone != _lastZone || now - _lastFullScan > reconcileSeconds;
            _wasActive = active;
            if (full)
            {
                _lastZone = zone;
                _lastFullScan = now;
                FullScans++;
            }
            return full;
        }

        public int Drain(List<TId> into, Func<TId, bool> exists, Func<TId, bool> inArea,
                         Func<TId, bool> shouldSend, Func<TId, bool> deferSend = null,
                         Action<TId> onInvalid = null, int maxItems = 4096)
        {
            EnsureWork();

            int processed = 0;
            int limit = Math.Max(1, maxItems);
            while (processed < limit && _work.Count > 0)
            {
                TId id = _work.Dequeue();
                if (!Pending.Contains(id)) continue;
                processed++;

                if (!exists(id))
                {
                    Pending.Remove(id);
                    onInvalid?.Invoke(id);
                    continue;
                }
                if (!inArea(id))
                {
                    Pending.Remove(id);
                    onInvalid?.Invoke(id);
                    continue;
                }
                if (!shouldSend(id))
                {
                    Pending.Remove(id);
                    continue;
                }
                if (deferSend != null && deferSend(id))
                {
                    _work.Enqueue(id);
                    continue;
                }

                into.Add(id);
                // A successfully selected dirty entry has now entered this peer's send list.
                // Leaving it in Pending causes it to be reconsidered forever.
                Pending.Remove(id);
            }

            return processed;
        }

        private void EnsureWork()
        {
            if (_work.Count != 0 || Pending.Count == 0) return;
            foreach (TId id in Pending) _work.Enqueue(id);
        }
    }
}