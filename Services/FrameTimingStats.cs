namespace DynamicIsland.Services;

// UI-thread-owned accumulator. Long gaps are reported separately, not treated as displayed frames.
internal sealed class FrameTimingStats
{
    public long Frames { get; private set; }
    public long Late { get; private set; }
    public long VeryLate { get; private set; }
    public long LongGaps { get; private set; }
    public double Sum { get; private set; }
    public double Worst { get; private set; }
    public double LongestGap { get; private set; }

    public bool Record(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return false;
        if (seconds > 0.5)
        {
            LongGaps++;
            LongestGap = Math.Max(LongestGap, seconds);
            return false;
        }
        Frames++;
        Sum += seconds;
        if (seconds > 0.020) Late++;
        if (seconds > 0.050) VeryLate++;
        Worst = Math.Max(Worst, seconds);
        return true;
    }
}
