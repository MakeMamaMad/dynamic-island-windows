using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class FrameTimingStatsTests
{
    [Theory]
    [InlineData(0.016, 0, 0)]
    [InlineData(0.020, 0, 0)]
    [InlineData(0.020001, 1, 0)]
    [InlineData(0.050, 1, 0)]
    [InlineData(0.050001, 1, 1)]
    [InlineData(0.080, 1, 1)]
    [InlineData(0.500, 1, 1)]
    public void FrameThresholds(double seconds, long late, long veryLate)
    {
        var stats = new FrameTimingStats();
        Assert.True(stats.Record(seconds));
        Assert.Equal(1L, stats.Frames);
        Assert.Equal(late, stats.Late);
        Assert.Equal(veryLate, stats.VeryLate);
        Assert.Equal(seconds, stats.Sum);
        Assert.Equal(seconds, stats.Worst);
        Assert.Equal(0L, stats.LongGaps);
    }

    [Fact]
    public void LongGapsAreSeparateFromFrameStatistics()
    {
        var stats = new FrameTimingStats();
        Assert.False(stats.Record(0.500001));
        Assert.False(stats.Record(2));
        Assert.True(stats.Record(0.016));
        Assert.True(stats.Record(0.080));
        Assert.Equal(2L, stats.Frames);
        Assert.Equal(1L, stats.Late);
        Assert.Equal(1L, stats.VeryLate);
        Assert.Equal(0.096, stats.Sum, 10);
        Assert.Equal(0.080, stats.Worst);
        Assert.Equal(2L, stats.LongGaps);
        Assert.Equal(2.0, stats.LongestGap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidIntervalsDoNotChangeStatistics(double seconds)
    {
        var stats = new FrameTimingStats();
        Assert.False(stats.Record(seconds));
        Assert.Equal(0L, stats.Frames);
        Assert.Equal(0L, stats.LongGaps);
        Assert.Equal(0.0, stats.Sum);
        Assert.Equal(0.0, stats.Worst);
        Assert.Equal(0.0, stats.LongestGap);
    }
}
