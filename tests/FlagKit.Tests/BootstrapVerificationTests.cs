using System.Security;
using FlagKit.Utils;
using Xunit;

namespace FlagKit.Tests;

public class BootstrapVerificationTests
{
    private const string TestApiKey = "sdk_test_key_12345";

    #region CanonicalizeObject Tests

    [Fact]
    public void CanonicalizeObject_SortsKeysAlphabetically()
    {
        var obj = new Dictionary<string, object?>
        {
            ["zebra"] = "last",
            ["alpha"] = "first",
            ["middle"] = "mid"
        };

        var canonical = Security.CanonicalizeObject(obj);

        Assert.Equal("{\"alpha\":\"first\",\"middle\":\"mid\",\"zebra\":\"last\"}", canonical);
    }

    [Fact]
    public void CanonicalizeObject_HandlesEmptyDictionary()
    {
        var obj = new Dictionary<string, object?>();

        var canonical = Security.CanonicalizeObject(obj);

        Assert.Equal("{}", canonical);
    }

    [Fact]
    public void CanonicalizeObject_HandlesNull()
    {
        var canonical = Security.CanonicalizeObject(null!);

        Assert.Equal("{}", canonical);
    }

    [Fact]
    public void CanonicalizeObject_HandlesDifferentValueTypes()
    {
        var obj = new Dictionary<string, object?>
        {
            ["bool_true"] = true,
            ["bool_false"] = false,
            ["int_val"] = 42,
            ["double_val"] = 3.14,
            ["null_val"] = null,
            ["string_val"] = "hello"
        };

        var canonical = Security.CanonicalizeObject(obj);

        Assert.Contains("\"bool_false\":false", canonical);
        Assert.Contains("\"bool_true\":true", canonical);
        Assert.Contains("\"int_val\":42", canonical);
        Assert.Contains("\"null_val\":null", canonical);
        Assert.Contains("\"string_val\":\"hello\"", canonical);
    }

    [Fact]
    public void CanonicalizeObject_HandlesNestedObjects()
    {
        var obj = new Dictionary<string, object?>
        {
            ["nested"] = new Dictionary<string, object?>
            {
                ["z_key"] = "z",
                ["a_key"] = "a"
            }
        };

        var canonical = Security.CanonicalizeObject(obj);

        Assert.Contains("\"nested\":{\"a_key\":\"a\",\"z_key\":\"z\"}", canonical);
    }

    [Fact]
    public void CanonicalizeObject_HandlesSpecialCharacters()
    {
        var obj = new Dictionary<string, object?>
        {
            ["special"] = "line1\nline2\ttab\"quote\\backslash"
        };

        var canonical = Security.CanonicalizeObject(obj);

        Assert.Contains("\\n", canonical);
        Assert.Contains("\\t", canonical);
        Assert.Contains("\\\"", canonical);
        Assert.Contains("\\\\", canonical);
    }

    [Fact]
    public void CanonicalizeObject_ProducesConsistentOutput()
    {
        var obj1 = new Dictionary<string, object?>
        {
            ["b"] = 2,
            ["a"] = 1
        };

        var obj2 = new Dictionary<string, object?>
        {
            ["a"] = 1,
            ["b"] = 2
        };

        var canonical1 = Security.CanonicalizeObject(obj1);
        var canonical2 = Security.CanonicalizeObject(obj2);

        Assert.Equal(canonical1, canonical2);
    }

    #endregion

    #region VerifyBootstrapSignature Tests

    [Fact]
    public void VerifyBootstrapSignature_ValidSignature_ReturnsTrue()
    {
        var flags = new Dictionary<string, object?>
        {
            ["feature_a"] = true,
            ["feature_b"] = "variant1"
        };

        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey);
        var config = new BootstrapVerificationConfig { Enabled = true };

        var (valid, error) = Security.VerifyBootstrapSignature(bootstrap, TestApiKey, config);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void VerifyBootstrapSignature_InvalidSignature_ReturnsFalse()
    {
        var flags = new Dictionary<string, object?>
        {
            ["feature_a"] = true
        };

        var bootstrap = new BootstrapConfig
        {
            Flags = flags,
            Signature = "invalid_signature",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var config = new BootstrapVerificationConfig { Enabled = true };

        var (valid, error) = Security.VerifyBootstrapSignature(bootstrap, TestApiKey, config);

        Assert.False(valid);
        Assert.Contains("Invalid bootstrap signature", error);
    }

    [Fact]
    public void VerifyBootstrapSignature_WrongApiKey_ReturnsFalse()
    {
        var flags = new Dictionary<string, object?>
        {
            ["feature_a"] = true
        };

        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey);
        var config = new BootstrapVerificationConfig { Enabled = true };

        var (valid, error) = Security.VerifyBootstrapSignature(bootstrap, "sdk_different_key", config);

        Assert.False(valid);
        Assert.Contains("Invalid bootstrap signature", error);
    }

    [Fact]
    public void VerifyBootstrapSignature_ExpiredTimestamp_ReturnsFalse()
    {
        var flags = new Dictionary<string, object?>
        {
            ["feature_a"] = true
        };

        // Create bootstrap with timestamp 25 hours ago
        var oldTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (25 * 60 * 60 * 1000);
        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey, oldTimestamp);
        var config = new BootstrapVerificationConfig
        {
            Enabled = true,
            MaxAge = 86400000 // 24 hours
        };

        var (valid, error) = Security.VerifyBootstrapSignature(bootstrap, TestApiKey, config);

        Assert.False(valid);
        Assert.Contains("expired", error);
    }

    [Fact]
    public void VerifyBootstrapSignature_FutureTimestamp_ReturnsFalse()
    {
        var flags = new Dictionary<string, object?>
        {
            ["feature_a"] = true
        };

        // Create bootstrap with timestamp 1 hour in the future
        var futureTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (60 * 60 * 1000);
        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey, futureTimestamp);
        var config = new BootstrapVerificationConfig { Enabled = true };

        var (valid, error) = Security.VerifyBootstrapSignature(bootstrap, TestApiKey, config);

        Assert.False(valid);
        Assert.Contains("future", error);
    }

    [Fact]
    public void VerifyBootstrapSignature_VerificationDisabled_ReturnsTrue()
    {
        var bootstrap = new BootstrapConfig
        {
            Flags = new Dictionary<string, object?> { ["test"] = true },
            Signature = "invalid_signature",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var config = new BootstrapVerificationConfig { Enabled = false };

        var (valid, error) = Security.VerifyBootstrapSignature(bootstrap, TestApiKey, config);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void VerifyBootstrapSignature_NoSignature_ReturnsTrue()
    {
        var bootstrap = new BootstrapConfig
        {
            Flags = new Dictionary<string, object?> { ["test"] = true }
        };

        var config = new BootstrapVerificationConfig { Enabled = true };

        var (valid, error) = Security.VerifyBootstrapSignature(bootstrap, TestApiKey, config);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void VerifyBootstrapSignature_NullBootstrap_ReturnsFalse()
    {
        var config = new BootstrapVerificationConfig { Enabled = true };

        var (valid, error) = Security.VerifyBootstrapSignature(null!, TestApiKey, config);

        Assert.False(valid);
        Assert.Contains("null", error);
    }

    [Fact]
    public void VerifyBootstrapSignature_TamperedFlags_ReturnsFalse()
    {
        var originalFlags = new Dictionary<string, object?>
        {
            ["feature_a"] = true
        };

        var bootstrap = Security.CreateSignedBootstrap(originalFlags, TestApiKey);

        // Tamper with the flags
        var tamperedBootstrap = new BootstrapConfig
        {
            Flags = new Dictionary<string, object?>
            {
                ["feature_a"] = false  // Changed from true to false
            },
            Signature = bootstrap.Signature,
            Timestamp = bootstrap.Timestamp
        };

        var config = new BootstrapVerificationConfig { Enabled = true };

        var (valid, error) = Security.VerifyBootstrapSignature(tamperedBootstrap, TestApiKey, config);

        Assert.False(valid);
        Assert.Contains("Invalid bootstrap signature", error);
    }

    #endregion

    #region CreateSignedBootstrap Tests

    [Fact]
    public void CreateSignedBootstrap_CreatesValidSignature()
    {
        var flags = new Dictionary<string, object?>
        {
            ["feature_a"] = true,
            ["feature_b"] = "test"
        };

        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey);

        Assert.NotNull(bootstrap);
        Assert.NotNull(bootstrap.Signature);
        Assert.NotNull(bootstrap.Timestamp);
        Assert.Equal(flags, bootstrap.Flags);
        Assert.Matches("^[a-f0-9]{64}$", bootstrap.Signature);
    }

    [Fact]
    public void CreateSignedBootstrap_UsesProvidedTimestamp()
    {
        var flags = new Dictionary<string, object?> { ["test"] = true };
        var expectedTimestamp = 1700000000000L;

        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey, expectedTimestamp);

        Assert.Equal(expectedTimestamp, bootstrap.Timestamp);
    }

    [Fact]
    public void CreateSignedBootstrap_DifferentKeys_DifferentSignatures()
    {
        var flags = new Dictionary<string, object?> { ["test"] = true };
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var bootstrap1 = Security.CreateSignedBootstrap(flags, "sdk_key_1", timestamp);
        var bootstrap2 = Security.CreateSignedBootstrap(flags, "sdk_key_2", timestamp);

        Assert.NotEqual(bootstrap1.Signature, bootstrap2.Signature);
    }

    #endregion

    #region FlagKitClient Bootstrap Integration Tests

    [Fact]
    public void FlagKitClient_LegacyBootstrap_StillWorks()
    {
        var options = new FlagKitOptions
        {
            ApiKey = TestApiKey,
            Bootstrap = new Dictionary<string, object>
            {
                ["legacy_flag"] = true
            }
        };

        using var client = new FlagKitClient(options);

        Assert.True(client.HasFlag("legacy_flag"));
        Assert.True(client.GetBooleanValue("legacy_flag", false));
    }

    [Fact]
    public void FlagKitClient_BootstrapConfig_WithoutSignature_LoadsFlags()
    {
        var options = new FlagKitOptions
        {
            ApiKey = TestApiKey,
            BootstrapConfig = new BootstrapConfig
            {
                Flags = new Dictionary<string, object?>
                {
                    ["unsigned_flag"] = true
                }
            }
        };

        using var client = new FlagKitClient(options);

        Assert.True(client.HasFlag("unsigned_flag"));
        Assert.True(client.GetBooleanValue("unsigned_flag", false));
    }

    [Fact]
    public void FlagKitClient_BootstrapConfig_WithValidSignature_LoadsFlags()
    {
        var flags = new Dictionary<string, object?>
        {
            ["signed_flag"] = true,
            ["string_flag"] = "hello"
        };

        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey);

        var options = new FlagKitOptions
        {
            ApiKey = TestApiKey,
            BootstrapConfig = bootstrap,
            BootstrapVerification = new BootstrapVerificationConfig { Enabled = true }
        };

        using var client = new FlagKitClient(options);

        Assert.True(client.HasFlag("signed_flag"));
        Assert.True(client.GetBooleanValue("signed_flag", false));
        Assert.Equal("hello", client.GetStringValue("string_flag", "default"));
    }

    [Fact]
    public void FlagKitClient_BootstrapConfig_InvalidSignature_OnFailureError_Throws()
    {
        var bootstrap = new BootstrapConfig
        {
            Flags = new Dictionary<string, object?> { ["test"] = true },
            Signature = "invalid_signature",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var options = new FlagKitOptions
        {
            ApiKey = TestApiKey,
            BootstrapConfig = bootstrap,
            BootstrapVerification = new BootstrapVerificationConfig
            {
                Enabled = true,
                OnFailure = "error"
            }
        };

        Assert.Throws<SecurityException>(() => new FlagKitClient(options));
    }

    [Fact]
    public void FlagKitClient_BootstrapConfig_InvalidSignature_OnFailureIgnore_LoadsFlags()
    {
        var bootstrap = new BootstrapConfig
        {
            Flags = new Dictionary<string, object?> { ["test"] = true },
            Signature = "invalid_signature",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var options = new FlagKitOptions
        {
            ApiKey = TestApiKey,
            BootstrapConfig = bootstrap,
            BootstrapVerification = new BootstrapVerificationConfig
            {
                Enabled = true,
                OnFailure = "ignore"
            }
        };

        using var client = new FlagKitClient(options);

        // Flags should still be loaded despite invalid signature
        Assert.True(client.HasFlag("test"));
    }

    [Fact]
    public void FlagKitClient_BootstrapConfig_InvalidSignature_OnFailureWarn_LoadsFlags()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var bootstrap = new BootstrapConfig
            {
                Flags = new Dictionary<string, object?> { ["test"] = true },
                Signature = "invalid_signature",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var options = new FlagKitOptions
            {
                ApiKey = TestApiKey,
                BootstrapConfig = bootstrap,
                BootstrapVerification = new BootstrapVerificationConfig
                {
                    Enabled = true,
                    OnFailure = "warn"
                }
            };

            using var client = new FlagKitClient(options);

            // Flags should still be loaded
            Assert.True(client.HasFlag("test"));

            // Warning should have been logged
            var output = sw.ToString();
            Assert.Contains("WARNING", output);
            Assert.Contains("Bootstrap verification failed", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void FlagKitClient_BootstrapConfig_ExpiredTimestamp_OnFailureError_Throws()
    {
        var flags = new Dictionary<string, object?> { ["test"] = true };
        var oldTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (25 * 60 * 60 * 1000);
        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey, oldTimestamp);

        var options = new FlagKitOptions
        {
            ApiKey = TestApiKey,
            BootstrapConfig = bootstrap,
            BootstrapVerification = new BootstrapVerificationConfig
            {
                Enabled = true,
                MaxAge = 86400000, // 24 hours
                OnFailure = "error"
            }
        };

        Assert.Throws<SecurityException>(() => new FlagKitClient(options));
    }

    [Fact]
    public void FlagKitClient_BootstrapConfig_VerificationDisabled_LoadsFlags()
    {
        var bootstrap = new BootstrapConfig
        {
            Flags = new Dictionary<string, object?> { ["test"] = true },
            Signature = "any_signature", // Invalid signature but verification is disabled
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var options = new FlagKitOptions
        {
            ApiKey = TestApiKey,
            BootstrapConfig = bootstrap,
            BootstrapVerification = new BootstrapVerificationConfig
            {
                Enabled = false
            }
        };

        using var client = new FlagKitClient(options);

        Assert.True(client.HasFlag("test"));
    }

    #endregion

    #region BootstrapVerificationConfig Tests

    [Fact]
    public void BootstrapVerificationConfig_DefaultValues()
    {
        var config = new BootstrapVerificationConfig();

        Assert.True(config.Enabled);
        Assert.Equal(86400000, config.MaxAge);
        Assert.Equal("warn", config.OnFailure);
    }

    [Fact]
    public void BootstrapVerificationConfig_CustomValues()
    {
        var config = new BootstrapVerificationConfig
        {
            Enabled = false,
            MaxAge = 3600000,
            OnFailure = "error"
        };

        Assert.False(config.Enabled);
        Assert.Equal(3600000, config.MaxAge);
        Assert.Equal("error", config.OnFailure);
    }

    #endregion

    #region BootstrapConfig Tests

    [Fact]
    public void BootstrapConfig_DefaultValues()
    {
        var config = new BootstrapConfig();

        Assert.NotNull(config.Flags);
        Assert.Empty(config.Flags);
        Assert.Null(config.Signature);
        Assert.Null(config.Timestamp);
    }

    [Fact]
    public void BootstrapConfig_CustomValues()
    {
        var config = new BootstrapConfig
        {
            Flags = new Dictionary<string, object?> { ["flag"] = true },
            Signature = "test_signature",
            Timestamp = 1700000000000L
        };

        Assert.Single(config.Flags);
        Assert.Equal("test_signature", config.Signature);
        Assert.Equal(1700000000000L, config.Timestamp);
    }

    #endregion

    #region Builder Tests

    [Fact]
    public void Builder_BootstrapConfig_SetsCorrectly()
    {
        var flags = new Dictionary<string, object?> { ["test"] = true };
        var bootstrap = Security.CreateSignedBootstrap(flags, TestApiKey);

        var options = FlagKitOptions.CreateBuilder(TestApiKey)
            .BootstrapConfig(bootstrap)
            .Build();

        Assert.NotNull(options.BootstrapConfig);
        Assert.Equal(bootstrap.Flags, options.BootstrapConfig.Flags);
        Assert.Equal(bootstrap.Signature, options.BootstrapConfig.Signature);
    }

    [Fact]
    public void Builder_BootstrapVerification_SetsCorrectly()
    {
        var verificationConfig = new BootstrapVerificationConfig
        {
            Enabled = false,
            MaxAge = 1000,
            OnFailure = "ignore"
        };

        var options = FlagKitOptions.CreateBuilder(TestApiKey)
            .BootstrapVerification(verificationConfig)
            .Build();

        Assert.False(options.BootstrapVerification.Enabled);
        Assert.Equal(1000, options.BootstrapVerification.MaxAge);
        Assert.Equal("ignore", options.BootstrapVerification.OnFailure);
    }

    #endregion
}
