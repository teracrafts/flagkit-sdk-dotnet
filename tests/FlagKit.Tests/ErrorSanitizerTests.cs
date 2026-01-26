using FlagKit.Errors;
using Xunit;

namespace FlagKit.Tests;

public class ErrorSanitizerTests
{
    [Fact]
    public void Sanitize_ReturnsOriginalMessage_WhenDisabled()
    {
        var message = "Error at /home/user/secret/file.txt with key sdk_abc123456789";

        var result = ErrorSanitizer.Sanitize(message, enabled: false);

        Assert.Equal(message, result);
    }

    [Fact]
    public void Sanitize_ReturnsEmptyString_WhenMessageIsEmpty()
    {
        var result = ErrorSanitizer.Sanitize(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_ReturnsNull_WhenMessageIsNull()
    {
        var result = ErrorSanitizer.Sanitize(null!);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("/home/user/secret/file.txt", "[PATH]")]
    [InlineData("/var/log/app/error.log", "[PATH]")]
    [InlineData("/etc/passwd", "[PATH]")]
    public void Sanitize_RedactsUnixPaths(string path, string expected)
    {
        var message = $"Error at {path}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"Error at {expected}", result);
    }

    [Theory]
    [InlineData("C:\\Users\\Admin\\Documents\\secret.txt", "[PATH]")]
    [InlineData("D:\\Projects\\app\\config.json", "[PATH]")]
    public void Sanitize_RedactsWindowsPaths(string path, string expected)
    {
        var message = $"Error at {path}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"Error at {expected}", result);
    }

    [Theory]
    [InlineData("192.168.1.1", "[IP]")]
    [InlineData("10.0.0.1", "[IP]")]
    [InlineData("255.255.255.255", "[IP]")]
    [InlineData("127.0.0.1", "[IP]")]
    public void Sanitize_RedactsIPAddresses(string ip, string expected)
    {
        var message = $"Connection failed to {ip}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"Connection failed to {expected}", result);
    }

    [Theory]
    [InlineData("sdk_abc123456789", "sdk_[REDACTED]")]
    [InlineData("sdk_test-key-12345", "sdk_[REDACTED]")]
    public void Sanitize_RedactsSdkApiKeys(string key, string expected)
    {
        var message = $"Invalid key: {key}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"Invalid key: {expected}", result);
    }

    [Theory]
    [InlineData("srv_abc123456789", "srv_[REDACTED]")]
    [InlineData("srv_server-key-12345", "srv_[REDACTED]")]
    public void Sanitize_RedactsSrvApiKeys(string key, string expected)
    {
        var message = $"Invalid key: {key}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"Invalid key: {expected}", result);
    }

    [Theory]
    [InlineData("cli_abc123456789", "cli_[REDACTED]")]
    [InlineData("cli_client-key-12345", "cli_[REDACTED]")]
    public void Sanitize_RedactsCliApiKeys(string key, string expected)
    {
        var message = $"Invalid key: {key}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"Invalid key: {expected}", result);
    }

    [Theory]
    [InlineData("user@example.com", "[EMAIL]")]
    [InlineData("admin@company.org", "[EMAIL]")]
    [InlineData("test.user@domain.co.uk", "[EMAIL]")]
    public void Sanitize_RedactsEmailAddresses(string email, string expected)
    {
        var message = $"User email: {email}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"User email: {expected}", result);
    }

    [Theory]
    [InlineData("postgres://user:pass@localhost:5432/db", "[CONNECTION_STRING]")]
    [InlineData("mysql://root:secret@db.example.com/mydb", "[CONNECTION_STRING]")]
    [InlineData("mongodb://admin:password@cluster.mongodb.net/test", "[CONNECTION_STRING]")]
    [InlineData("redis://user:pass@redis.example.com:6379", "[CONNECTION_STRING]")]
    public void Sanitize_RedactsConnectionStrings(string connString, string expected)
    {
        var message = $"Connection: {connString}";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal($"Connection: {expected}", result);
    }

    [Fact]
    public void Sanitize_HandlesMultipleSensitivePatterns()
    {
        var message = "Error at /home/user/app connecting to 192.168.1.100 with key sdk_secret12345 for user@example.com";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal("Error at [PATH] connecting to [IP] with key sdk_[REDACTED] for [EMAIL]", result);
    }

    [Fact]
    public void Sanitize_PreservesNonSensitiveContent()
    {
        var message = "Flag 'my-feature' not found in cache";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal(message, result);
    }

    [Fact]
    public void ContainsSensitiveInfo_ReturnsFalse_ForNonSensitiveMessage()
    {
        var message = "Flag evaluation failed";

        var result = ErrorSanitizer.ContainsSensitiveInfo(message);

        Assert.False(result);
    }

    [Fact]
    public void ContainsSensitiveInfo_ReturnsTrue_ForMessageWithIP()
    {
        var message = "Connection to 192.168.1.1 failed";

        var result = ErrorSanitizer.ContainsSensitiveInfo(message);

        Assert.True(result);
    }

    [Fact]
    public void ContainsSensitiveInfo_ReturnsTrue_ForMessageWithApiKey()
    {
        var message = "Invalid API key: sdk_abc123456789";

        var result = ErrorSanitizer.ContainsSensitiveInfo(message);

        Assert.True(result);
    }

    [Fact]
    public void ContainsSensitiveInfo_ReturnsFalse_ForEmptyMessage()
    {
        var result = ErrorSanitizer.ContainsSensitiveInfo(string.Empty);

        Assert.False(result);
    }

    [Fact]
    public void ContainsSensitiveInfo_ReturnsFalse_ForNullMessage()
    {
        var result = ErrorSanitizer.ContainsSensitiveInfo(null!);

        Assert.False(result);
    }

    [Fact]
    public void FlagKitException_SanitizesMessage_WhenEnabled()
    {
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig { Enabled = true });

        var ex = new FlagKitException(ErrorCode.NetworkError, "Failed to connect to 192.168.1.1");

        Assert.Contains("[IP]", ex.Message);
        Assert.DoesNotContain("192.168.1.1", ex.Message);
    }

    [Fact]
    public void FlagKitException_DoesNotSanitize_WhenDisabled()
    {
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig { Enabled = false });

        var ex = new FlagKitException(ErrorCode.NetworkError, "Failed to connect to 192.168.1.1");

        Assert.Contains("192.168.1.1", ex.Message);

        // Reset to default
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig());
    }

    [Fact]
    public void FlagKitException_PreservesOriginalMessage_WhenConfigured()
    {
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig
        {
            Enabled = true,
            PreserveOriginal = true
        });

        var ex = new FlagKitException(ErrorCode.NetworkError, "Failed to connect to 192.168.1.1");

        Assert.Contains("[IP]", ex.Message);
        Assert.NotNull(ex.OriginalMessage);
        Assert.Contains("192.168.1.1", ex.OriginalMessage);

        // Reset to default
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig());
    }

    [Fact]
    public void FlagKitException_OriginalMessageIsNull_WhenPreserveNotEnabled()
    {
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig
        {
            Enabled = true,
            PreserveOriginal = false
        });

        var ex = new FlagKitException(ErrorCode.NetworkError, "Failed to connect to 192.168.1.1");

        Assert.Null(ex.OriginalMessage);

        // Reset to default
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig());
    }

    [Fact]
    public void FlagKitException_StaticMethods_UseSanitization()
    {
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig { Enabled = true });

        var ex = FlagKitException.NetworkError("Failed at /home/user/config.json");

        Assert.Contains("[PATH]", ex.Message);
        Assert.DoesNotContain("/home/user/config.json", ex.Message);

        // Reset to default
        FlagKitException.ConfigureSanitization(new ErrorSanitizationConfig());
    }

    [Fact]
    public void ErrorSanitizationConfig_DefaultValues()
    {
        var config = new ErrorSanitizationConfig();

        Assert.True(config.Enabled);
        Assert.False(config.PreserveOriginal);
    }

    [Fact]
    public void Sanitize_DoesNotRedactShortApiKeys()
    {
        // Keys shorter than 8 chars after prefix should not be redacted
        var message = "Key: sdk_abc";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal(message, result);
    }

    [Fact]
    public void Sanitize_ConnectionStrings_CaseInsensitive()
    {
        var message = "Connection: POSTGRES://user:pass@localhost/db";

        var result = ErrorSanitizer.Sanitize(message);

        Assert.Equal("Connection: [CONNECTION_STRING]", result);
    }
}
