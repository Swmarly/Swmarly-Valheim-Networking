// -----------------------------------------------------------------------------
// File role: Small rolling statistics accumulator used by diagnostics.
// Why it exists: A fixed-size/simple accumulator gives stable averages and maxima without a logging allocation on every network call.
// Change contract: Keep behavior deterministic and reset semantics clear.
// -----------------------------------------------------------------------------
namespace ValheimTune
{
    public sealed class RollingStats
    {
        private double _sum;
        public int Count { get; private set; }
        public double Max { get; private set; }
        public double Avg => Count == 0 ? 0.0 : _sum / Count;

        public void Add(double v)
        {
            _sum += v;
            Count++;
            if (v > Max) Max = v;
        }

        public void Reset()
        {
            _sum = 0; Count = 0; Max = 0;
        }
    }
}
