using Tapo.Download;
using Xunit;

namespace Tapo.Tests.Unit;

public sealed class VideoDownloaderOptionsTests
{
    [Fact]
    public void Default_values_match_documented_defaults()
    {
        var options = VideoDownloaderOptions.Default;
        Assert.Equal(256, options.WindowSize);
        Assert.Equal(64, options.FallbackWindowSize);
        Assert.Equal(1, options.ParallelSessions);
        Assert.Equal(30_000, options.StallTimeoutMilliseconds);
        Assert.Equal(5, options.PaddingSeconds);
        Assert.Equal(1, options.MaxRetries);
        Assert.Equal(10, options.ParallelSlicingThresholdSeconds);
    }

    [Fact]
    public void Init_only_properties_can_be_overridden_with_object_initializer()
    {
        var options = new VideoDownloaderOptions
        {
            WindowSize = 1024,
            FallbackWindowSize = 32,
            ParallelSessions = 4,
            StallTimeoutMilliseconds = 60_000,
            PaddingSeconds = 10,
            MaxRetries = 3,
            ParallelSlicingThresholdSeconds = 30,
        };

        Assert.Equal(1024, options.WindowSize);
        Assert.Equal(32, options.FallbackWindowSize);
        Assert.Equal(4, options.ParallelSessions);
        Assert.Equal(60_000, options.StallTimeoutMilliseconds);
        Assert.Equal(10, options.PaddingSeconds);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(30, options.ParallelSlicingThresholdSeconds);
    }
}
