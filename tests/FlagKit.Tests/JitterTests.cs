using System.Diagnostics;
using FlagKit.Types;
using Xunit;

namespace FlagKit.Tests;

public class JitterTests
{
    private const string ValidApiKey = "sdk_test_key_12345";

    [Fact]
    public void Jitter_NotApplied_WhenDisabled()
    {
        // Arrange - jitter disabled by default
        var options = new FlagKitOptions
        {
            ApiKey = ValidApiKey,
            Bootstrap = new Dictionary<string, object> { { "test-flag", true } }
        };
        using var client = new FlagKitClient(options);

        // Act - measure time for multiple evaluations
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            client.GetBooleanValue("test-flag", false);
        }
        stopwatch.Stop();

        // Assert - should be very fast (no jitter delay)
        // 10 evaluations with no jitter should complete in well under 50ms
        Assert.True(stopwatch.ElapsedMilliseconds < 50,
            $"Expected evaluations to complete quickly without jitter, but took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Jitter_Applied_WhenEnabled()
    {
        // Arrange - enable jitter with known min/max
        var options = new FlagKitOptions
        {
            ApiKey = ValidApiKey,
            Bootstrap = new Dictionary<string, object> { { "test-flag", true } },
            EvaluationJitter = new EvaluationJitterConfig
            {
                Enabled = true,
                MinMs = 10,
                MaxMs = 20
            }
        };
        using var client = new FlagKitClient(options);

        // Act - measure time for multiple evaluations
        var stopwatch = Stopwatch.StartNew();
        const int evaluationCount = 5;
        for (int i = 0; i < evaluationCount; i++)
        {
            client.GetBooleanValue("test-flag", false);
        }
        stopwatch.Stop();

        // Assert - should take at least minMs * evaluationCount
        // 5 evaluations with min 10ms jitter should take at least 50ms
        Assert.True(stopwatch.ElapsedMilliseconds >= evaluationCount * 10,
            $"Expected evaluations to take at least {evaluationCount * 10}ms with jitter, but took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Jitter_Timing_FallsWithinMinMaxRange()
    {
        // Arrange - use specific min/max values
        const int minMs = 15;
        const int maxMs = 25;
        var options = new FlagKitOptions
        {
            ApiKey = ValidApiKey,
            Bootstrap = new Dictionary<string, object> { { "test-flag", "value" } },
            EvaluationJitter = new EvaluationJitterConfig
            {
                Enabled = true,
                MinMs = minMs,
                MaxMs = maxMs
            }
        };
        using var client = new FlagKitClient(options);

        // Act - perform multiple single evaluations and check timing
        var timings = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            client.GetStringValue("test-flag", "default");
            stopwatch.Stop();
            timings.Add(stopwatch.ElapsedMilliseconds);
        }

        // Assert - all timings should be >= minMs (allowing some margin for execution time)
        // Note: We use minMs - 2 to account for timing imprecision
        foreach (var timing in timings)
        {
            Assert.True(timing >= minMs - 2,
                $"Expected timing to be at least {minMs}ms (with 2ms margin), but was {timing}ms");
        }

        // Assert - average timing should be reasonable (not excessively large)
        var averageTiming = timings.Average();
        Assert.True(averageTiming <= maxMs + 50,
            $"Expected average timing to be around {minMs}-{maxMs}ms range, but average was {averageTiming}ms");
    }

    [Fact]
    public void EvaluationJitterConfig_Defaults()
    {
        // Arrange & Act
        var config = new EvaluationJitterConfig();

        // Assert
        Assert.False(config.Enabled);
        Assert.Equal(5, config.MinMs);
        Assert.Equal(15, config.MaxMs);
    }

    [Fact]
    public void FlagKitOptions_EvaluationJitter_DefaultConfig()
    {
        // Arrange & Act
        var options = new FlagKitOptions { ApiKey = ValidApiKey };

        // Assert
        Assert.NotNull(options.EvaluationJitter);
        Assert.False(options.EvaluationJitter.Enabled);
    }

    [Fact]
    public void Builder_EvaluationJitter_SetsConfig()
    {
        // Arrange
        var jitterConfig = new EvaluationJitterConfig
        {
            Enabled = true,
            MinMs = 20,
            MaxMs = 50
        };

        // Act
        var options = FlagKitOptions.CreateBuilder(ValidApiKey)
            .EvaluationJitter(jitterConfig)
            .Build();

        // Assert
        Assert.True(options.EvaluationJitter.Enabled);
        Assert.Equal(20, options.EvaluationJitter.MinMs);
        Assert.Equal(50, options.EvaluationJitter.MaxMs);
    }
}
