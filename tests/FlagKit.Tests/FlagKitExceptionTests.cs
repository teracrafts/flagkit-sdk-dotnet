using FlagKit.Errors;
using Xunit;

namespace FlagKit.Tests;

public class FlagKitExceptionTests
{
    [Fact]
    public void ConfigError_Creates_Config_Exception()
    {
        var ex = FlagKitException.ConfigError(ErrorCode.ConfigInvalidApiKey, "Invalid API key");

        Assert.Equal(ErrorCode.ConfigInvalidApiKey, ex.Code);
        Assert.Contains("Invalid API key", ex.Message);
        Assert.True(ex.IsConfigError);
        Assert.False(ex.IsNetworkError);
    }

    [Fact]
    public void NetworkError_Creates_Network_Exception()
    {
        var ex = FlagKitException.NetworkError(ErrorCode.HttpTimeout, "Request timed out");

        Assert.Equal(ErrorCode.HttpTimeout, ex.Code);
        Assert.Contains("Request timed out", ex.Message);
        Assert.True(ex.IsNetworkError);
        Assert.False(ex.IsConfigError);
    }

    [Fact]
    public void EvaluationError_Creates_Evaluation_Exception()
    {
        var ex = FlagKitException.EvaluationError(ErrorCode.EvalFlagNotFound, "Flag not found");

        Assert.Equal(ErrorCode.EvalFlagNotFound, ex.Code);
        Assert.Contains("Flag not found", ex.Message);
        Assert.True(ex.IsEvaluationError);
    }

    [Fact]
    public void InternalError_Creates_Internal_Exception()
    {
        var ex = FlagKitException.InternalError(ErrorCode.SdkNotInitialized, "SDK not initialized");

        Assert.Equal(ErrorCode.SdkNotInitialized, ex.Code);
        Assert.Contains("SDK not initialized", ex.Message);
        Assert.True(ex.IsInternalError);
    }

    [Fact]
    public void InnerException_Is_Preserved()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new FlagKitException(ErrorCode.SdkNotInitialized, "outer", inner);

        Assert.Same(inner, ex.InnerException);
    }
}
