using FlagKit.Utils;
using Moq;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace FlagKit.Tests.Utils;

public class SecurityTests
{
    #region IsPotentialPIIField Tests

    [Theory]
    [InlineData("email")]
    [InlineData("userEmail")]
    [InlineData("EMAIL")]
    [InlineData("user_email")]
    public void IsPotentialPIIField_DetectsEmailFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("phoneNumber")]
    [InlineData("mobile")]
    [InlineData("telephone")]
    [InlineData("PHONE")]
    public void IsPotentialPIIField_DetectsPhoneFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("ssn")]
    [InlineData("socialSecurity")]
    [InlineData("social_security")]
    [InlineData("SSN")]
    public void IsPotentialPIIField_DetectsSSNFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("creditCard")]
    [InlineData("credit_card")]
    [InlineData("cardNumber")]
    [InlineData("card_number")]
    [InlineData("cvv")]
    [InlineData("CVV")]
    public void IsPotentialPIIField_DetectsCreditCardFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("passwd")]
    [InlineData("secret")]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("accessToken")]
    [InlineData("access_token")]
    [InlineData("refreshToken")]
    [InlineData("refresh_token")]
    [InlineData("authToken")]
    [InlineData("auth_token")]
    [InlineData("privateKey")]
    [InlineData("private_key")]
    [InlineData("token")]
    public void IsPotentialPIIField_DetectsAuthenticationFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("street")]
    [InlineData("zipCode")]
    [InlineData("zip_code")]
    [InlineData("postalCode")]
    [InlineData("postal_code")]
    public void IsPotentialPIIField_DetectsAddressFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("dateOfBirth")]
    [InlineData("date_of_birth")]
    [InlineData("dob")]
    [InlineData("birthDate")]
    [InlineData("birth_date")]
    public void IsPotentialPIIField_DetectsBirthDateFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("passport")]
    [InlineData("driverLicense")]
    [InlineData("driver_license")]
    [InlineData("nationalId")]
    [InlineData("national_id")]
    public void IsPotentialPIIField_DetectsIdentificationFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("bankAccount")]
    [InlineData("bank_account")]
    [InlineData("routingNumber")]
    [InlineData("routing_number")]
    [InlineData("iban")]
    [InlineData("swift")]
    [InlineData("IBAN")]
    [InlineData("SWIFT")]
    public void IsPotentialPIIField_DetectsBankingFields(string fieldName)
    {
        Assert.True(Security.IsPotentialPIIField(fieldName));
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("plan")]
    [InlineData("country")]
    [InlineData("featureEnabled")]
    [InlineData("role")]
    [InlineData("tier")]
    [InlineData("locale")]
    [InlineData("timezone")]
    public void IsPotentialPIIField_DoesNotFlagSafeFields(string fieldName)
    {
        Assert.False(Security.IsPotentialPIIField(fieldName));
    }

    [Fact]
    public void IsPotentialPIIField_ReturnsFalseForNullOrEmpty()
    {
        Assert.False(Security.IsPotentialPIIField(null!));
        Assert.False(Security.IsPotentialPIIField(""));
    }

    [Fact]
    public void IsPotentialPIIField_WithAdditionalPatterns_DetectsCustomPatterns()
    {
        var additionalPatterns = new[] { "customsecret", "sensitivedata" };

        Assert.True(Security.IsPotentialPIIField("myCustomSecret", additionalPatterns));
        Assert.True(Security.IsPotentialPIIField("sensitiveData", additionalPatterns));
        Assert.False(Security.IsPotentialPIIField("normalField", additionalPatterns));
    }

    #endregion

    #region DetectPotentialPII Tests

    [Fact]
    public void DetectPotentialPII_DetectsPIIInFlatObjects()
    {
        var data = new Dictionary<string, object?>
        {
            ["userId"] = "user-123",
            ["email"] = "user@example.com",
            ["plan"] = "premium"
        };

        var piiFields = Security.DetectPotentialPII(data);

        Assert.Contains("email", piiFields);
        Assert.DoesNotContain("userId", piiFields);
        Assert.DoesNotContain("plan", piiFields);
    }

    [Fact]
    public void DetectPotentialPII_DetectsPIIInNestedObjects()
    {
        var data = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                ["email"] = "user@example.com",
                ["phone"] = "123-456-7890"
            },
            ["settings"] = new Dictionary<string, object?>
            {
                ["darkMode"] = true
            }
        };

        var piiFields = Security.DetectPotentialPII(data);

        Assert.Contains("user.email", piiFields);
        Assert.Contains("user.phone", piiFields);
        Assert.DoesNotContain("settings.darkMode", piiFields);
    }

    [Fact]
    public void DetectPotentialPII_HandlesDeeplyNestedObjects()
    {
        var data = new Dictionary<string, object?>
        {
            ["profile"] = new Dictionary<string, object?>
            {
                ["contact"] = new Dictionary<string, object?>
                {
                    ["primaryEmail"] = "user@example.com"
                }
            }
        };

        var piiFields = Security.DetectPotentialPII(data);

        Assert.Contains("profile.contact.primaryEmail", piiFields);
    }

    [Fact]
    public void DetectPotentialPII_ReturnsEmptyArrayForSafeData()
    {
        var data = new Dictionary<string, object?>
        {
            ["userId"] = "user-123",
            ["plan"] = "premium",
            ["features"] = new[] { "dark-mode", "beta" }
        };

        var piiFields = Security.DetectPotentialPII(data);

        Assert.Empty(piiFields);
    }

    [Fact]
    public void DetectPotentialPII_ReturnsEmptyForNullData()
    {
        var piiFields = Security.DetectPotentialPII(null);

        Assert.Empty(piiFields);
    }

    [Fact]
    public void DetectPotentialPII_UsesCustomPrefix()
    {
        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com"
        };

        var piiFields = Security.DetectPotentialPII(data, "context");

        Assert.Contains("context.email", piiFields);
    }

    [Fact]
    public void DetectPotentialPII_WithAdditionalPatterns()
    {
        var additionalPatterns = new[] { "customsecret" };
        var data = new Dictionary<string, object?>
        {
            ["customSecret"] = "secret-value",
            ["normalField"] = "value"
        };

        var piiFields = Security.DetectPotentialPII(data, additionalPatterns);

        Assert.Contains("customSecret", piiFields);
        Assert.DoesNotContain("normalField", piiFields);
    }

    #endregion

    #region WarnIfPotentialPII Tests

    [Fact]
    public void WarnIfPotentialPII_LogsWarningWhenPIIDetected()
    {
        var mockLogger = new Mock<ILogger>();
        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com",
            ["phone"] = "123-456-7890"
        };

        Security.WarnIfPotentialPII(data, "context", mockLogger.Object);

        mockLogger.Verify(l => l.Warn(It.Is<string>(s =>
            s.Contains("Potential PII detected") &&
            s.Contains("email") &&
            s.Contains("context"))), Times.Once);
    }

    [Fact]
    public void WarnIfPotentialPII_IncludesCorrectAdviceForContext()
    {
        var mockLogger = new Mock<ILogger>();
        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com"
        };

        Security.WarnIfPotentialPII(data, "context", mockLogger.Object);

        mockLogger.Verify(l => l.Warn(It.Is<string>(s =>
            s.Contains("privateAttributes"))), Times.Once);
    }

    [Fact]
    public void WarnIfPotentialPII_IncludesCorrectAdviceForEvent()
    {
        var mockLogger = new Mock<ILogger>();
        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com"
        };

        Security.WarnIfPotentialPII(data, "event", mockLogger.Object);

        mockLogger.Verify(l => l.Warn(It.Is<string>(s =>
            s.Contains("removing sensitive data"))), Times.Once);
    }

    [Fact]
    public void WarnIfPotentialPII_DoesNotLogWhenNoPIIDetected()
    {
        var mockLogger = new Mock<ILogger>();
        var data = new Dictionary<string, object?>
        {
            ["userId"] = "user-123",
            ["plan"] = "premium"
        };

        Security.WarnIfPotentialPII(data, "context", mockLogger.Object);

        mockLogger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WarnIfPotentialPII_HandlesNullData()
    {
        var mockLogger = new Mock<ILogger>();

        // Should not throw
        Security.WarnIfPotentialPII(null, "event", mockLogger.Object);

        mockLogger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WarnIfPotentialPII_HandlesNullLogger()
    {
        var data = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com"
        };

        // Should not throw
        var exception = Record.Exception(() => Security.WarnIfPotentialPII(data, "event", null));
        Assert.Null(exception);
    }

    [Fact]
    public void WarnIfPotentialPII_WithConfig_RespectsWarnOnPotentialPIISetting()
    {
        var mockLogger = new Mock<ILogger>();
        var config = new SecurityConfig { WarnOnPotentialPII = false };
        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com"
        };

        Security.WarnIfPotentialPII(data, "context", mockLogger.Object, config);

        mockLogger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WarnIfPotentialPII_WithConfig_UsesAdditionalPatterns()
    {
        var mockLogger = new Mock<ILogger>();
        var config = new SecurityConfig
        {
            WarnOnPotentialPII = true,
            AdditionalPIIPatterns = new List<string> { "customsecret" }
        };
        var data = new Dictionary<string, object?>
        {
            ["customSecret"] = "secret-value"
        };

        Security.WarnIfPotentialPII(data, "context", mockLogger.Object, config);

        mockLogger.Verify(l => l.Warn(It.Is<string>(s =>
            s.Contains("customSecret"))), Times.Once);
    }

    #endregion

    #region IsServerKey Tests

    [Theory]
    [InlineData("srv_abc123")]
    [InlineData("srv_")]
    [InlineData("srv_longerkey12345")]
    public void IsServerKey_ReturnsTrueForServerKeys(string apiKey)
    {
        Assert.True(Security.IsServerKey(apiKey));
    }

    [Theory]
    [InlineData("sdk_abc123")]
    [InlineData("cli_abc123")]
    [InlineData("SRV_abc123")] // Case-sensitive
    [InlineData("")]
    [InlineData(null)]
    public void IsServerKey_ReturnsFalseForNonServerKeys(string? apiKey)
    {
        Assert.False(Security.IsServerKey(apiKey!));
    }

    #endregion

    #region IsClientKey Tests

    [Theory]
    [InlineData("sdk_abc123")]
    [InlineData("sdk_")]
    [InlineData("cli_abc123")]
    [InlineData("cli_")]
    public void IsClientKey_ReturnsTrueForClientKeys(string apiKey)
    {
        Assert.True(Security.IsClientKey(apiKey));
    }

    [Theory]
    [InlineData("srv_abc123")]
    [InlineData("SDK_abc123")] // Case-sensitive
    [InlineData("CLI_abc123")] // Case-sensitive
    [InlineData("")]
    [InlineData(null)]
    public void IsClientKey_ReturnsFalseForNonClientKeys(string? apiKey)
    {
        Assert.False(Security.IsClientKey(apiKey!));
    }

    #endregion

    #region IsBrowserEnvironment Tests

    [Fact]
    public void IsBrowserEnvironment_ReturnsFalseInNormalDotNetEnvironment()
    {
        // In normal .NET environment (not Blazor WebAssembly), this should return false
        var result = Security.IsBrowserEnvironment();

        // This test will return false when running in standard .NET
        Assert.False(result);
    }

    #endregion

    #region WarnIfServerKeyInBrowser Tests

    [Fact]
    public void WarnIfServerKeyInBrowser_DoesNotWarnForClientKeys()
    {
        var mockLogger = new Mock<ILogger>();
        var originalConsoleOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            Security.WarnIfServerKeyInBrowser("sdk_abc123", mockLogger.Object);

            mockLogger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
            Assert.DoesNotContain("WARNING", stringWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Fact]
    public void WarnIfServerKeyInBrowser_DoesNotWarnInNonBrowserEnvironment()
    {
        // In non-browser environment, even server keys should not trigger warning
        var mockLogger = new Mock<ILogger>();
        var originalConsoleOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            Security.WarnIfServerKeyInBrowser("srv_abc123", mockLogger.Object);

            // Since we're in a non-browser environment, no warning should be logged
            mockLogger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Fact]
    public void WarnIfServerKeyInBrowser_WithConfig_RespectsWarnOnServerKeyInBrowserSetting()
    {
        var mockLogger = new Mock<ILogger>();
        var config = new SecurityConfig { WarnOnServerKeyInBrowser = false };

        Security.WarnIfServerKeyInBrowser("srv_abc123", mockLogger.Object, config);

        mockLogger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WarnIfServerKeyInBrowser_HandlesNullLogger()
    {
        // Should not throw
        var exception = Record.Exception(() => Security.WarnIfServerKeyInBrowser("srv_abc123", null));
        Assert.Null(exception);
    }

    #endregion

    #region SecurityConfig Tests

    [Fact]
    public void SecurityConfig_DefaultWarnOnServerKeyInBrowser_IsTrue()
    {
        var config = new SecurityConfig();

        Assert.True(config.WarnOnServerKeyInBrowser);
    }

    [Fact]
    public void SecurityConfig_DefaultAdditionalPIIPatterns_IsEmpty()
    {
        var config = new SecurityConfig();

        Assert.NotNull(config.AdditionalPIIPatterns);
        Assert.Empty(config.AdditionalPIIPatterns);
    }

    [Fact]
    public void SecurityConfig_CanSetCustomValues()
    {
        var config = new SecurityConfig
        {
            WarnOnPotentialPII = false,
            WarnOnServerKeyInBrowser = false,
            AdditionalPIIPatterns = new List<string> { "custom1", "custom2" }
        };

        Assert.False(config.WarnOnPotentialPII);
        Assert.False(config.WarnOnServerKeyInBrowser);
        Assert.Equal(2, config.AdditionalPIIPatterns.Count);
        Assert.Contains("custom1", config.AdditionalPIIPatterns);
        Assert.Contains("custom2", config.AdditionalPIIPatterns);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FullWorkflow_DetectAndWarnAboutPII()
    {
        var mockLogger = new Mock<ILogger>();
        var config = new SecurityConfig
        {
            WarnOnPotentialPII = true,
            AdditionalPIIPatterns = new List<string> { "customsensitive" }
        };

        var userData = new Dictionary<string, object?>
        {
            ["userId"] = "user-123",
            ["profile"] = new Dictionary<string, object?>
            {
                ["email"] = "user@example.com",
                ["customSensitiveData"] = "secret-info"
            },
            ["preferences"] = new Dictionary<string, object?>
            {
                ["theme"] = "dark"
            }
        };

        // First, detect all PII
        var piiFields = Security.DetectPotentialPII(userData, config.AdditionalPIIPatterns);

        Assert.Equal(2, piiFields.Count);
        Assert.Contains("profile.email", piiFields);
        Assert.Contains("profile.customSensitiveData", piiFields);

        // Then, warn about it
        Security.WarnIfPotentialPII(userData, "context", mockLogger.Object, config);

        mockLogger.Verify(l => l.Warn(It.Is<string>(s =>
            s.Contains("profile.email") &&
            s.Contains("profile.customSensitiveData"))), Times.Once);
    }

    [Fact]
    public void FullWorkflow_APIKeyValidation()
    {
        // Server key
        Assert.True(Security.IsServerKey("srv_production_key"));
        Assert.False(Security.IsClientKey("srv_production_key"));

        // SDK client key
        Assert.True(Security.IsClientKey("sdk_client_key"));
        Assert.False(Security.IsServerKey("sdk_client_key"));

        // CLI client key
        Assert.True(Security.IsClientKey("cli_limited_key"));
        Assert.False(Security.IsServerKey("cli_limited_key"));

        // Invalid keys
        Assert.False(Security.IsServerKey("invalid_key"));
        Assert.False(Security.IsClientKey("invalid_key"));
    }

    #endregion

    #region Strict PII Mode Tests

    [Fact]
    public void CheckPIIStrict_ThrowsSecurityException_WhenPIIDetectedInStrictMode()
    {
        var config = new SecurityConfig
        {
            StrictPIIMode = true,
            WarnOnPotentialPII = true
        };

        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com"
        };

        var ex = Assert.Throws<SecurityException>(() =>
            Security.CheckPIIStrict(data, "context", null, config));

        Assert.Contains("email", ex.Message);
        Assert.Contains("Potential PII detected", ex.Message);
    }

    [Fact]
    public void CheckPIIStrict_WarnsOnly_WhenPIIDetectedWithoutStrictMode()
    {
        var mockLogger = new Mock<ILogger>();
        var config = new SecurityConfig
        {
            StrictPIIMode = false,
            WarnOnPotentialPII = true
        };

        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com"
        };

        // Should not throw
        var ex = Record.Exception(() =>
            Security.CheckPIIStrict(data, "context", mockLogger.Object, config));

        Assert.Null(ex);
        mockLogger.Verify(l => l.Warn(It.Is<string>(s =>
            s.Contains("email"))), Times.Once);
    }

    [Fact]
    public void CheckPIIStrict_DoesNotThrow_WhenPIIFieldIsInPrivateAttributes()
    {
        var config = new SecurityConfig
        {
            StrictPIIMode = true,
            WarnOnPotentialPII = true,
            PrivateAttributes = new List<string> { "email" }
        };

        var data = new Dictionary<string, object?>
        {
            ["email"] = "user@example.com"
        };

        // Should not throw because email is in private attributes
        var ex = Record.Exception(() =>
            Security.CheckPIIStrict(data, "context", null, config));

        Assert.Null(ex);
    }

    [Fact]
    public void CheckPIIStrict_DoesNotThrow_WhenNestedPIIFieldIsInPrivateAttributes()
    {
        var config = new SecurityConfig
        {
            StrictPIIMode = true,
            WarnOnPotentialPII = true,
            PrivateAttributes = new List<string> { "user.email" }
        };

        var data = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                ["email"] = "user@example.com"
            }
        };

        // Should not throw because user.email is in private attributes
        var ex = Record.Exception(() =>
            Security.CheckPIIStrict(data, "context", null, config));

        Assert.Null(ex);
    }

    [Fact]
    public void CheckPIIStrict_HandlesNullData()
    {
        var config = new SecurityConfig
        {
            StrictPIIMode = true,
            WarnOnPotentialPII = true
        };

        // Should not throw for null data
        var ex = Record.Exception(() =>
            Security.CheckPIIStrict(null, "context", null, config));

        Assert.Null(ex);
    }

    #endregion

    #region GetKeyId Tests

    [Theory]
    [InlineData("sdk_abc123def456", "sdk_abc1")]
    [InlineData("srv_xyz789abc", "srv_xyz7")]
    [InlineData("cli_test", "cli_test")]
    [InlineData("short", "short")]
    [InlineData("", "")]
    public void GetKeyId_ReturnsFirst8Characters(string apiKey, string expected)
    {
        Assert.Equal(expected, Security.GetKeyId(apiKey));
    }

    [Fact]
    public void GetKeyId_HandlesNullKey()
    {
        Assert.Equal(string.Empty, Security.GetKeyId(null!));
    }

    #endregion

    #region HMAC-SHA256 Signing Tests

    [Fact]
    public void GenerateHMACSHA256_ProducesConsistentSignatures()
    {
        var message = "test message";
        var key = "secret-key";

        var sig1 = Security.GenerateHMACSHA256(message, key);
        var sig2 = Security.GenerateHMACSHA256(message, key);

        Assert.Equal(sig1, sig2);
        Assert.Matches("^[a-f0-9]{64}$", sig1);
    }

    [Fact]
    public void GenerateHMACSHA256_DifferentMessages_DifferentSignatures()
    {
        var key = "secret-key";

        var sig1 = Security.GenerateHMACSHA256("message1", key);
        var sig2 = Security.GenerateHMACSHA256("message2", key);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void GenerateHMACSHA256_DifferentKeys_DifferentSignatures()
    {
        var message = "test message";

        var sig1 = Security.GenerateHMACSHA256(message, "key1");
        var sig2 = Security.GenerateHMACSHA256(message, "key2");

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void CreateRequestSignature_ReturnsValidSignatureAndTimestamp()
    {
        var body = """{"event": "test"}""";
        var apiKey = "sdk_abc123";

        var (signature, timestamp) = Security.CreateRequestSignature(body, apiKey);

        Assert.Matches("^[a-f0-9]{64}$", signature);
        Assert.True(timestamp > 0);
    }

    [Fact]
    public void CreateRequestSignature_UsesProvidedTimestamp()
    {
        var body = """{"test": true}""";
        var apiKey = "sdk_test";
        var expectedTimestamp = 1700000000000L;

        var (_, timestamp) = Security.CreateRequestSignature(body, apiKey, expectedTimestamp);

        Assert.Equal(expectedTimestamp, timestamp);
    }

    [Fact]
    public void VerifyRequestSignature_ValidSignature_ReturnsTrue()
    {
        var body = """{"event": "test"}""";
        var apiKey = "sdk_abc123";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var (signature, _) = Security.CreateRequestSignature(body, apiKey, timestamp);
        var isValid = Security.VerifyRequestSignature(body, signature, timestamp, apiKey);

        Assert.True(isValid);
    }

    [Fact]
    public void VerifyRequestSignature_ExpiredSignature_ReturnsFalse()
    {
        var body = """{"event": "test"}""";
        var apiKey = "sdk_abc123";
        var oldTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 600000; // 10 minutes ago

        var (signature, _) = Security.CreateRequestSignature(body, apiKey, oldTimestamp);
        var isValid = Security.VerifyRequestSignature(body, signature, oldTimestamp, apiKey, 300000); // 5 min max age

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyRequestSignature_WrongKey_ReturnsFalse()
    {
        var body = """{"event": "test"}""";
        var apiKey = "sdk_abc123";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var (signature, _) = Security.CreateRequestSignature(body, apiKey, timestamp);
        var isValid = Security.VerifyRequestSignature(body, signature, timestamp, "sdk_different");

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyRequestSignature_ModifiedBody_ReturnsFalse()
    {
        var body = """{"event": "test"}""";
        var apiKey = "sdk_abc123";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var (signature, _) = Security.CreateRequestSignature(body, apiKey, timestamp);
        var isValid = Security.VerifyRequestSignature("""{"event": "modified"}""", signature, timestamp, apiKey);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyRequestSignature_FutureTimestamp_ReturnsFalse()
    {
        var body = """{"event": "test"}""";
        var apiKey = "sdk_abc123";
        var futureTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000; // 1 minute in future

        var (signature, _) = Security.CreateRequestSignature(body, apiKey, futureTimestamp);
        var isValid = Security.VerifyRequestSignature(body, signature, futureTimestamp, apiKey);

        Assert.False(isValid);
    }

    #endregion

    #region IsProductionEnvironment Tests

    [Fact]
    public void IsProductionEnvironment_ReturnsBasedOnEnvironmentVariable()
    {
        // This test validates the method exists and returns a boolean
        // The actual environment variable check depends on the test environment
        var result = Security.IsProductionEnvironment();
        Assert.IsType<bool>(result);
    }

    #endregion
}

public class CacheEncryptionTests
{
    private const string TestApiKey = "sdk_test_key_12345";

    #region DeriveKey Tests

    [Fact]
    public void DeriveKey_ProducesConsistentKeys()
    {
        var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        var key1 = CacheEncryption.DeriveKey(TestApiKey, salt);
        var key2 = CacheEncryption.DeriveKey(TestApiKey, salt);

        Assert.Equal(key1, key2);
        Assert.Equal(32, key1.Length); // 256 bits
    }

    [Fact]
    public void DeriveKey_DifferentSalts_DifferentKeys()
    {
        var salt1 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var salt2 = new byte[] { 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

        var key1 = CacheEncryption.DeriveKey(TestApiKey, salt1);
        var key2 = CacheEncryption.DeriveKey(TestApiKey, salt2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentApiKeys_DifferentKeys()
    {
        var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        var key1 = CacheEncryption.DeriveKey("sdk_key_1", salt);
        var key2 = CacheEncryption.DeriveKey("sdk_key_2", salt);

        Assert.NotEqual(key1, key2);
    }

    #endregion

    #region Encrypt/Decrypt Tests

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_ReturnsOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");

        var encrypted = CacheEncryption.Encrypt(plaintext, TestApiKey);
        var decrypted = CacheEncryption.Decrypt(encrypted, TestApiKey);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutputEachTime()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");

        var encrypted1 = CacheEncryption.Encrypt(plaintext, TestApiKey);
        var encrypted2 = CacheEncryption.Encrypt(plaintext, TestApiKey);

        // Different nonce/salt means different output
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");
        var encrypted = CacheEncryption.Encrypt(plaintext, TestApiKey);

        Assert.Throws<CryptographicException>(() =>
            CacheEncryption.Decrypt(encrypted, "sdk_wrong_key"));
    }

    [Fact]
    public void Decrypt_WithTamperedData_ThrowsCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");
        var encrypted = CacheEncryption.Encrypt(plaintext, TestApiKey);

        // Tamper with the ciphertext
        encrypted[^1] ^= 0xFF;

        Assert.Throws<CryptographicException>(() =>
            CacheEncryption.Decrypt(encrypted, TestApiKey));
    }

    [Fact]
    public void Decrypt_WithTooShortData_ThrowsCryptographicException()
    {
        var tooShort = new byte[10];

        Assert.Throws<CryptographicException>(() =>
            CacheEncryption.Decrypt(tooShort, TestApiKey));
    }

    #endregion

    #region EncryptString/DecryptString Tests

    [Fact]
    public void EncryptString_DecryptString_RoundTrip()
    {
        var plaintext = "Hello, World! This is a test message.";

        var encrypted = CacheEncryption.EncryptString(plaintext, TestApiKey);
        var decrypted = CacheEncryption.DecryptString(encrypted, TestApiKey);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptString_ReturnsBase64String()
    {
        var plaintext = "Test message";

        var encrypted = CacheEncryption.EncryptString(plaintext, TestApiKey);

        // Verify it's valid base64
        var exception = Record.Exception(() => Convert.FromBase64String(encrypted));
        Assert.Null(exception);
    }

    [Fact]
    public void EncryptString_HandlesUnicodeCharacters()
    {
        var plaintext = "Hello, World! Special chars: "; // emoji and special chars

        var encrypted = CacheEncryption.EncryptString(plaintext, TestApiKey);
        var decrypted = CacheEncryption.DecryptString(encrypted, TestApiKey);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptString_HandlesEmptyString()
    {
        var plaintext = "";

        var encrypted = CacheEncryption.EncryptString(plaintext, TestApiKey);
        var decrypted = CacheEncryption.DecryptString(encrypted, TestApiKey);

        Assert.Equal(plaintext, decrypted);
    }

    #endregion

    #region EncryptObject/DecryptObject Tests

    [Fact]
    public void EncryptObject_DecryptObject_RoundTrip()
    {
        var testObject = new TestData
        {
            Id = 123,
            Name = "Test",
            IsActive = true
        };

        var encrypted = CacheEncryption.EncryptObject(testObject, TestApiKey);
        var decrypted = CacheEncryption.DecryptObject<TestData>(encrypted, TestApiKey);

        Assert.NotNull(decrypted);
        Assert.Equal(testObject.Id, decrypted.Id);
        Assert.Equal(testObject.Name, decrypted.Name);
        Assert.Equal(testObject.IsActive, decrypted.IsActive);
    }

    [Fact]
    public void EncryptObject_DecryptObject_HandlesDictionary()
    {
        var testObject = new Dictionary<string, object?>
        {
            ["key1"] = "value1",
            ["key2"] = 42,
            ["key3"] = true
        };

        var encrypted = CacheEncryption.EncryptObject(testObject, TestApiKey);
        var decrypted = CacheEncryption.DecryptObject<Dictionary<string, object?>>(encrypted, TestApiKey);

        Assert.NotNull(decrypted);
        Assert.Equal(3, decrypted.Count);
    }

    [Fact]
    public void EncryptObject_DecryptObject_HandlesList()
    {
        var testList = new List<string> { "item1", "item2", "item3" };

        var encrypted = CacheEncryption.EncryptObject(testList, TestApiKey);
        var decrypted = CacheEncryption.DecryptObject<List<string>>(encrypted, TestApiKey);

        Assert.NotNull(decrypted);
        Assert.Equal(testList, decrypted);
    }

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
    }

    #endregion
}
