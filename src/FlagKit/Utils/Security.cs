using System.Runtime.InteropServices;

namespace FlagKit.Utils;

/// <summary>
/// Simple logger interface for security warnings.
/// </summary>
public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>
/// Security configuration options.
/// </summary>
public class SecurityConfig
{
    /// <summary>
    /// Warn about potential PII in context/events. Default: true in development.
    /// </summary>
    public bool WarnOnPotentialPII { get; set; } = !IsProduction();

    /// <summary>
    /// Warn when server keys are used in browser-like environments (Blazor WebAssembly). Default: true.
    /// </summary>
    public bool WarnOnServerKeyInBrowser { get; set; } = true;

    /// <summary>
    /// Custom PII patterns to detect in addition to built-in patterns.
    /// </summary>
    public List<string> AdditionalPIIPatterns { get; set; } = new();

    private static bool IsProduction()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Security utilities for FlagKit SDK.
/// </summary>
public static class Security
{
    /// <summary>
    /// Common PII field patterns (case-insensitive).
    /// </summary>
    private static readonly string[] PIIPatterns =
    {
        "email",
        "phone",
        "telephone",
        "mobile",
        "ssn",
        "social_security",
        "socialSecurity",
        "credit_card",
        "creditCard",
        "card_number",
        "cardNumber",
        "cvv",
        "password",
        "passwd",
        "secret",
        "token",
        "api_key",
        "apiKey",
        "private_key",
        "privateKey",
        "access_token",
        "accessToken",
        "refresh_token",
        "refreshToken",
        "auth_token",
        "authToken",
        "address",
        "street",
        "zip_code",
        "zipCode",
        "postal_code",
        "postalCode",
        "date_of_birth",
        "dateOfBirth",
        "dob",
        "birth_date",
        "birthDate",
        "passport",
        "driver_license",
        "driverLicense",
        "national_id",
        "nationalId",
        "bank_account",
        "bankAccount",
        "routing_number",
        "routingNumber",
        "iban",
        "swift"
    };

    /// <summary>
    /// Checks if a field name potentially contains PII.
    /// </summary>
    /// <param name="fieldName">The field name to check.</param>
    /// <returns>True if the field name matches a PII pattern.</returns>
    public static bool IsPotentialPIIField(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return false;

        var lowerName = fieldName.ToLowerInvariant();
        return PIIPatterns.Any(pattern => lowerName.Contains(pattern.ToLowerInvariant()));
    }

    /// <summary>
    /// Checks if a field name potentially contains PII, including additional custom patterns.
    /// </summary>
    /// <param name="fieldName">The field name to check.</param>
    /// <param name="additionalPatterns">Additional patterns to check.</param>
    /// <returns>True if the field name matches a PII pattern.</returns>
    public static bool IsPotentialPIIField(string fieldName, IEnumerable<string>? additionalPatterns)
    {
        if (IsPotentialPIIField(fieldName))
            return true;

        if (additionalPatterns == null)
            return false;

        var lowerName = fieldName.ToLowerInvariant();
        return additionalPatterns.Any(pattern => lowerName.Contains(pattern.ToLowerInvariant()));
    }

    /// <summary>
    /// Detects potential PII in a dictionary and returns the field paths.
    /// </summary>
    /// <param name="data">The data to scan for PII.</param>
    /// <param name="prefix">Optional prefix for nested field paths.</param>
    /// <returns>A list of field paths that potentially contain PII.</returns>
    public static List<string> DetectPotentialPII(Dictionary<string, object?>? data, string prefix = "")
    {
        var piiFields = new List<string>();

        if (data == null)
            return piiFields;

        foreach (var (key, value) in data)
        {
            var fullPath = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";

            if (IsPotentialPIIField(key))
            {
                piiFields.Add(fullPath);
            }

            // Recursively check nested dictionaries
            if (value is Dictionary<string, object?> nestedDict)
            {
                var nestedPII = DetectPotentialPII(nestedDict, fullPath);
                piiFields.AddRange(nestedPII);
            }
            else if (value is IDictionary<string, object> genericDict)
            {
                var converted = genericDict.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
                var nestedPII = DetectPotentialPII(converted, fullPath);
                piiFields.AddRange(nestedPII);
            }
        }

        return piiFields;
    }

    /// <summary>
    /// Detects potential PII in a dictionary using custom additional patterns.
    /// </summary>
    /// <param name="data">The data to scan for PII.</param>
    /// <param name="additionalPatterns">Additional patterns to check.</param>
    /// <param name="prefix">Optional prefix for nested field paths.</param>
    /// <returns>A list of field paths that potentially contain PII.</returns>
    public static List<string> DetectPotentialPII(
        Dictionary<string, object?>? data,
        IEnumerable<string>? additionalPatterns,
        string prefix = "")
    {
        var piiFields = new List<string>();

        if (data == null)
            return piiFields;

        foreach (var (key, value) in data)
        {
            var fullPath = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";

            if (IsPotentialPIIField(key, additionalPatterns))
            {
                piiFields.Add(fullPath);
            }

            // Recursively check nested dictionaries
            if (value is Dictionary<string, object?> nestedDict)
            {
                var nestedPII = DetectPotentialPII(nestedDict, additionalPatterns, fullPath);
                piiFields.AddRange(nestedPII);
            }
            else if (value is IDictionary<string, object> genericDict)
            {
                var converted = genericDict.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
                var nestedPII = DetectPotentialPII(converted, additionalPatterns, fullPath);
                piiFields.AddRange(nestedPII);
            }
        }

        return piiFields;
    }

    /// <summary>
    /// Logs a warning if potential PII is detected in the data.
    /// </summary>
    /// <param name="data">The data to scan for PII.</param>
    /// <param name="dataType">The type of data being scanned (e.g., "context", "event").</param>
    /// <param name="logger">Optional logger for output.</param>
    public static void WarnIfPotentialPII(Dictionary<string, object?>? data, string dataType, ILogger? logger)
    {
        if (data == null || logger == null)
            return;

        var piiFields = DetectPotentialPII(data);

        if (piiFields.Count > 0)
        {
            var advice = dataType.Equals("context", StringComparison.OrdinalIgnoreCase)
                ? "Consider adding these to privateAttributes."
                : "Consider removing sensitive data from events.";

            logger.Warn(
                $"[FlagKit Security] Potential PII detected in {dataType} data: {string.Join(", ", piiFields)}. {advice}");
        }
    }

    /// <summary>
    /// Logs a warning if potential PII is detected, using security config for additional patterns.
    /// </summary>
    /// <param name="data">The data to scan for PII.</param>
    /// <param name="dataType">The type of data being scanned.</param>
    /// <param name="logger">Optional logger for output.</param>
    /// <param name="config">Security configuration with additional patterns.</param>
    public static void WarnIfPotentialPII(
        Dictionary<string, object?>? data,
        string dataType,
        ILogger? logger,
        SecurityConfig? config)
    {
        if (data == null || logger == null)
            return;

        if (config != null && !config.WarnOnPotentialPII)
            return;

        var additionalPatterns = config?.AdditionalPIIPatterns;
        var piiFields = additionalPatterns?.Count > 0
            ? DetectPotentialPII(data, additionalPatterns)
            : DetectPotentialPII(data);

        if (piiFields.Count > 0)
        {
            var advice = dataType.Equals("context", StringComparison.OrdinalIgnoreCase)
                ? "Consider adding these to privateAttributes."
                : "Consider removing sensitive data from events.";

            logger.Warn(
                $"[FlagKit Security] Potential PII detected in {dataType} data: {string.Join(", ", piiFields)}. {advice}");
        }
    }

    /// <summary>
    /// Checks if an API key is a server key (starts with "srv_").
    /// </summary>
    /// <param name="apiKey">The API key to check.</param>
    /// <returns>True if the key is a server key.</returns>
    public static bool IsServerKey(string apiKey)
    {
        return !string.IsNullOrEmpty(apiKey) && apiKey.StartsWith("srv_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if an API key is a client/SDK key (starts with "sdk_" or "cli_").
    /// </summary>
    /// <param name="apiKey">The API key to check.</param>
    /// <returns>True if the key is a client key.</returns>
    public static bool IsClientKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return false;

        return apiKey.StartsWith("sdk_", StringComparison.Ordinal)
               || apiKey.StartsWith("cli_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if the current environment is a browser-like environment (Blazor WebAssembly).
    /// </summary>
    /// <returns>True if running in a browser-like environment.</returns>
    public static bool IsBrowserEnvironment()
    {
        // Check for Blazor WebAssembly runtime
        return RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"))
               || RuntimeInformation.OSDescription.Contains("Browser", StringComparison.OrdinalIgnoreCase)
               || Type.GetType("Microsoft.AspNetCore.Components.WebAssembly.Hosting.WebAssemblyHost") != null;
    }

    /// <summary>
    /// Warns if a server key is being used in a browser-like environment (Blazor WebAssembly).
    /// </summary>
    /// <param name="apiKey">The API key to check.</param>
    /// <param name="logger">Optional logger for output.</param>
    public static void WarnIfServerKeyInBrowser(string apiKey, ILogger? logger)
    {
        if (!IsBrowserEnvironment() || !IsServerKey(apiKey))
            return;

        var message =
            "[FlagKit Security] WARNING: Server keys (srv_) should not be used in browser environments. " +
            "This exposes your server key in client-side code, which is a security risk. " +
            "Use SDK keys (sdk_) for client-side applications instead. " +
            "See: https://docs.flagkit.dev/sdk/security#api-keys";

        // Always output to console for visibility
        Console.WriteLine(message);

        // Also log through the SDK logger if available
        logger?.Warn(message);
    }

    /// <summary>
    /// Warns if a server key is being used in a browser-like environment, respecting config.
    /// </summary>
    /// <param name="apiKey">The API key to check.</param>
    /// <param name="logger">Optional logger for output.</param>
    /// <param name="config">Security configuration.</param>
    public static void WarnIfServerKeyInBrowser(string apiKey, ILogger? logger, SecurityConfig? config)
    {
        if (config != null && !config.WarnOnServerKeyInBrowser)
            return;

        WarnIfServerKeyInBrowser(apiKey, logger);
    }
}
