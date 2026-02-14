using FlagKit.Errors;
using System.Security;
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

    #region Base URL Tests

    [Fact]
    public void GetBaseUrl_Returns_Production_Url_By_Default()
    {
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", null);
        var url = Http.FlagKitHttpClient.GetBaseUrl();
        Assert.Equal("https://api.flagkit.dev/api/v1", url);
    }

    [Fact]
    public void GetBaseUrl_Returns_Local_Url_When_Mode_Is_Local()
    {
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", "local");
        var url = Http.FlagKitHttpClient.GetBaseUrl();
        Assert.Equal("https://api.flagkit.on/api/v1", url);
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", null);
    }

    [Fact]
    public void GetBaseUrl_Returns_Beta_Url_When_Mode_Is_Beta()
    {
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", "beta");
        var url = Http.FlagKitHttpClient.GetBaseUrl();
        Assert.Equal("https://api.beta.flagkit.dev/api/v1", url);
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", null);
    }

    [Fact]
    public void GetBaseUrl_Is_Case_Insensitive()
    {
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", "LOCAL");
        var url = Http.FlagKitHttpClient.GetBaseUrl();
        Assert.Equal("https://api.flagkit.on/api/v1", url);
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", null);
    }

    [Fact]
    public void GetBaseUrl_Trims_Whitespace()
    {
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", " local ");
        var url = Http.FlagKitHttpClient.GetBaseUrl();
        Assert.Equal("https://api.flagkit.on/api/v1", url);
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", null);
    }

    [Fact]
    public void GetBaseUrl_Falls_Through_For_Unknown_Mode()
    {
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", "staging");
        var url = Http.FlagKitHttpClient.GetBaseUrl();
        Assert.Equal("https://api.flagkit.dev/api/v1", url);
        Environment.SetEnvironmentVariable("FLAGKIT_MODE", null);
    }

    #endregion

    #region Security Options Tests

    [Fact]
    public void Security_Options_Have_Correct_Defaults()
    {
        var options = new FlagKitOptions { ApiKey = "sdk_test123" };

        Assert.Null(options.SecondaryApiKey);
        Assert.False(options.StrictPIIMode);
        Assert.Null(options.PrivateAttributes);
        Assert.False(options.EnableRequestSigning);
        Assert.False(options.EnableCacheEncryption);
    }

    [Fact]
    public void Valid_SecondaryApiKey_Passes_Validation()
    {
        var options = new FlagKitOptions
        {
            ApiKey = "sdk_primary_key",
            SecondaryApiKey = "sdk_secondary_key"
        };

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Invalid_SecondaryApiKey_Prefix_Throws()
    {
        var options = new FlagKitOptions
        {
            ApiKey = "sdk_primary_key",
            SecondaryApiKey = "invalid_secondary"
        };

        var ex = Assert.Throws<FlagKitException>(() => options.Validate());

        Assert.Equal(ErrorCode.ConfigInvalidApiKey, ex.Code);
        Assert.Contains("secondary", ex.Message.ToLower());
    }

    [Fact]
    public void Builder_With_Security_Options()
    {
        var privateAttributes = new List<string> { "email", "phone" };

        var options = FlagKitOptions.CreateBuilder("sdk_test123")
            .SecondaryApiKey("sdk_secondary_key")
            .StrictPIIMode(true)
            .PrivateAttributes(privateAttributes)
            .EnableRequestSigning(true)
            .EnableCacheEncryption(true)
            .Build();

        Assert.Equal("sdk_secondary_key", options.SecondaryApiKey);
        Assert.True(options.StrictPIIMode);
        Assert.NotNull(options.PrivateAttributes);
        Assert.Equal(2, options.PrivateAttributes.Count);
        Assert.True(options.EnableRequestSigning);
        Assert.True(options.EnableCacheEncryption);
    }

    #endregion
}
