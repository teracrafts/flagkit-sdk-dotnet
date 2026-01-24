using FlagKit.Utils;
using Moq;
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
}
