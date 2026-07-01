using Ember;

namespace Ember.Tests;

public class RhythmTests
{
    // Totals are documented in AGENTS.md's GIF-capture recipe: "slow exhale = 14s,
    // 4·7·8 = 19s, box = 16s" — this test locks that documented contract in code.
    [Theory]
    [InlineData(0, "slow exhale", 14.0)]
    [InlineData(1, "4 · 7 · 8", 19.0)]
    [InlineData(2, "box", 16.0)]
    public void Total_MatchesDocumentedRhythmLength(int index, string expectedName, double expectedTotal)
    {
        Rhythm rhythm = EmberRenderer.Rhythms[index];

        Assert.Equal(expectedName, rhythm.Name);
        Assert.Equal(expectedTotal, rhythm.Total);
    }
}

public class SmoothTests
{
    [Theory]
    [InlineData(-1, 0)]      // clamps below 0
    [InlineData(0, 0)]       // Smooth(0) == 0
    [InlineData(0.25, 0.15625)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.75, 0.84375)]
    [InlineData(1, 1)]       // Smooth(1) == 1
    [InlineData(2, 1)]       // clamps above 1
    public void Smooth_ClampsToUnitIntervalAndEasesAtKnownPoints(double input, double expected)
    {
        Assert.Equal(expected, EmberRenderer.Smooth(input));
    }

    [Fact]
    public void Smooth_IsMonotonicAcrossTheDomain()
    {
        double previous = double.NegativeInfinity;
        for (double t = -0.5; t <= 1.5; t += 0.1)
        {
            double current = EmberRenderer.Smooth(t);
            Assert.True(current >= previous, $"Smooth({t}) = {current} was less than the previous sample {previous}.");
            previous = current;
        }
    }
}

public class BreathStateTests
{
    // "slow exhale": breathe in 0-4s, hold 4-6s, let go 6-12s, rest 12-14s, total 14s.
    [Theory]
    [InlineData(0.0, 0.0, "breathe in")]   // start of "breathe in": P == 0
    [InlineData(2.0, 0.5, "breathe in")]   // midpoint of "breathe in": Smooth(0.5) == 0.5
    [InlineData(4.0, 1.0, "hold")]         // exactly at the boundary: moves to "hold", P == level == 1
    [InlineData(6.0, 1.0, "let go")]       // exactly at the next boundary: moves to "let go", P starts at 1
    [InlineData(9.0, 0.5, "let go")]       // midpoint of "let go": 1 - Smooth(0.5) == 0.5
    [InlineData(12.0, 0.0, "rest")]        // exactly at the boundary: moves to "rest", P == level == 0
    [InlineData(13.0, 0.0, "rest")]        // mid "rest": level stays 0
    [InlineData(14.0, 0.0, "breathe in")]  // wraps exactly one full cycle: 14 % 14 == 0
    [InlineData(16.0, 0.5, "breathe in")]  // wraps mid-cycle: 16 % 14 == 2, same as t=2
    public void BreathState_TransitionsAtStepBoundaries(double time, double expectedP, string expectedName)
    {
        var renderer = new EmberRenderer { Rhythm = EmberRenderer.Rhythms[0] }; // "slow exhale"

        (double p, string name) = renderer.BreathState(time);

        Assert.Equal(expectedP, p);
        Assert.Equal(expectedName, name);
    }
}
