using FlagKit.Errors;
using Xunit;

namespace FlagKit.Tests;

public class FlagKitOptionsTests
{
    [Fact]
    public void Valid_Options_Pass_Validation()
    {
        var options = new FlagKitOptions { ApiKey = "sdk_test123" };

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Missing_ApiKey_Throws()
    {
        var options = new FlagKitOptions { ApiKey = "" };

        var ex = Assert.Throws<FlagKitException>(() => options.Validate());

        Assert.Equal(ErrorCode.ConfigInvalidApiKey, ex.Code);
    }

    [Fact]
    public void Invalid_ApiKey_Prefix_Throws()
    {
        var options = new FlagKitOptions { ApiKey = "invalid_key" };

        var ex = Assert.Throws<FlagKitException>(() => options.Validate());

        Assert.Equal(ErrorCode.ConfigInvalidApiKey, ex.Code);
    }

    [Theory]
    [InlineData("sdk_")]
    [InlineData("srv_")]
    [InlineData("cli_")]
    public void Valid_ApiKey_Prefixes_Pass(string prefix)
    {
        var options = new FlagKitOptions { ApiKey = $"{prefix}test123" };

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Invalid_BaseUrl_Throws()
    {
        var options = new FlagKitOptions
        {
            ApiKey = "sdk_test123",
            BaseUrl = "not-a-url"
        };

        var ex = Assert.Throws<FlagKitException>(() => options.Validate());

        Assert.Equal(ErrorCode.ConfigInvalidBaseUrl, ex.Code);
    }

    [Fact]
    public void Zero_PollingInterval_Throws()
    {
        var options = new FlagKitOptions
        {
            ApiKey = "sdk_test123",
            PollingInterval = TimeSpan.Zero
        };

        var ex = Assert.Throws<FlagKitException>(() => options.Validate());

        Assert.Equal(ErrorCode.ConfigInvalidPollingInterval, ex.Code);
    }

    [Fact]
    public void Negative_CacheTtl_Throws()
    {
        var options = new FlagKitOptions
        {
            ApiKey = "sdk_test123",
            CacheTtl = TimeSpan.FromSeconds(-1)
        };

        var ex = Assert.Throws<FlagKitException>(() => options.Validate());

        Assert.Equal(ErrorCode.ConfigInvalidCacheTtl, ex.Code);
    }

    [Fact]
    public void Builder_Creates_Valid_Options()
    {
        var options = FlagKitOptions.CreateBuilder("sdk_test123")
            .BaseUrl("https://custom.api.com")
            .PollingInterval(TimeSpan.FromSeconds(60))
            .CacheTtl(TimeSpan.FromMinutes(10))
            .MaxCacheSize(500)
            .CacheEnabled(true)
            .EventBatchSize(20)
            .EventFlushInterval(TimeSpan.FromSeconds(60))
            .EventsEnabled(true)
            .Timeout(TimeSpan.FromSeconds(30))
            .RetryAttempts(5)
            .Build();

        Assert.Equal("sdk_test123", options.ApiKey);
        Assert.Equal("https://custom.api.com", options.BaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(60), options.PollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), options.CacheTtl);
        Assert.Equal(500, options.MaxCacheSize);
        Assert.True(options.CacheEnabled);
        Assert.Equal(20, options.EventBatchSize);
        Assert.Equal(TimeSpan.FromSeconds(60), options.EventFlushInterval);
        Assert.True(options.EventsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.Equal(5, options.RetryAttempts);
    }

    [Fact]
    public void Builder_With_Bootstrap_Data()
    {
        var bootstrap = new Dictionary<string, object>
        {
            ["flag1"] = true,
            ["flag2"] = "value"
        };

        var options = FlagKitOptions.CreateBuilder("sdk_test123")
            .Bootstrap(bootstrap)
            .Build();

        Assert.NotNull(options.Bootstrap);
        Assert.Equal(2, options.Bootstrap.Count);
    }

    [Fact]
    public void Default_Values_Are_Set()
    {
        var options = new FlagKitOptions { ApiKey = "sdk_test123" };

        Assert.Equal(FlagKitOptions.DefaultBaseUrl, options.BaseUrl);
        Assert.Equal(FlagKitOptions.DefaultPollingInterval, options.PollingInterval);
        Assert.Equal(FlagKitOptions.DefaultCacheTtl, options.CacheTtl);
        Assert.Equal(FlagKitOptions.DefaultMaxCacheSize, options.MaxCacheSize);
        Assert.True(options.CacheEnabled);
        Assert.Equal(FlagKitOptions.DefaultEventBatchSize, options.EventBatchSize);
        Assert.Equal(FlagKitOptions.DefaultEventFlushInterval, options.EventFlushInterval);
        Assert.True(options.EventsEnabled);
        Assert.Equal(FlagKitOptions.DefaultTimeout, options.Timeout);
        Assert.Equal(FlagKitOptions.DefaultRetryAttempts, options.RetryAttempts);
    }
}
