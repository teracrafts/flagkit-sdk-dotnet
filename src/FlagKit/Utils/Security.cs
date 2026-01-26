using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

    /// <summary>
    /// When enabled, throws SecurityException instead of warning when PII is detected without PrivateAttributes.
    /// Default: false.
    /// </summary>
    public bool StrictPIIMode { get; set; } = false;

    /// <summary>
    /// Fields that should be treated as private and not trigger PII warnings/exceptions.
    /// </summary>
    public List<string> PrivateAttributes { get; set; } = new();

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

    /// <summary>
    /// Checks for potential PII and throws SecurityException if strict mode is enabled.
    /// </summary>
    /// <param name="data">The data to scan for PII.</param>
    /// <param name="dataType">The type of data being scanned.</param>
    /// <param name="logger">Optional logger for output.</param>
    /// <param name="config">Security configuration.</param>
    /// <exception cref="SecurityException">Thrown when PII is detected in strict mode.</exception>
    public static void CheckPIIStrict(
        Dictionary<string, object?>? data,
        string dataType,
        ILogger? logger,
        SecurityConfig? config)
    {
        if (data == null)
            return;

        var privateAttributes = config?.PrivateAttributes ?? new List<string>();
        var additionalPatterns = config?.AdditionalPIIPatterns;

        var piiFields = additionalPatterns?.Count > 0
            ? DetectPotentialPII(data, additionalPatterns)
            : DetectPotentialPII(data);

        // Filter out fields that are in PrivateAttributes
        var unprotectedPIIFields = piiFields
            .Where(field => !IsFieldProtected(field, privateAttributes))
            .ToList();

        if (unprotectedPIIFields.Count == 0)
            return;

        var advice = dataType.Equals("context", StringComparison.OrdinalIgnoreCase)
            ? "Add these fields to privateAttributes or remove the PII data."
            : "Remove sensitive data from events.";

        var message =
            $"[FlagKit Security] Potential PII detected in {dataType} data: {string.Join(", ", unprotectedPIIFields)}. {advice}";

        if (config?.StrictPIIMode == true)
        {
            throw new SecurityException(message);
        }
        else if (config?.WarnOnPotentialPII != false)
        {
            logger?.Warn(message);
        }
    }

    /// <summary>
    /// Checks if a field path is protected by private attributes.
    /// </summary>
    private static bool IsFieldProtected(string fieldPath, List<string> privateAttributes)
    {
        // Check for exact match
        if (privateAttributes.Contains(fieldPath, StringComparer.OrdinalIgnoreCase))
            return true;

        // Check if any part of the field path is protected
        var parts = fieldPath.Split('.');
        var currentPath = "";
        foreach (var part in parts)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";
            if (privateAttributes.Contains(currentPath, StringComparer.OrdinalIgnoreCase))
                return true;
            if (privateAttributes.Contains(part, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the first 8 characters of an API key for identification.
    /// This is safe to expose as it doesn't reveal the full key.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    /// <returns>The key ID (first 8 characters).</returns>
    public static string GetKeyId(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return string.Empty;

        return apiKey.Length >= 8 ? apiKey[..8] : apiKey;
    }

    /// <summary>
    /// Generates HMAC-SHA256 signature for a message.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="key">The secret key.</param>
    /// <returns>The hex-encoded signature.</returns>
    public static string GenerateHMACSHA256(string message, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Creates a request signature for signing POST requests.
    /// </summary>
    /// <param name="body">The request body.</param>
    /// <param name="apiKey">The API key to use for signing.</param>
    /// <param name="timestamp">Optional timestamp (defaults to current time).</param>
    /// <returns>The signature and timestamp.</returns>
    public static (string Signature, long Timestamp) CreateRequestSignature(
        string body,
        string apiKey,
        long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var message = $"{ts}.{body}";
        var signature = GenerateHMACSHA256(message, apiKey);

        return (signature, ts);
    }

    /// <summary>
    /// Verifies a request signature.
    /// </summary>
    /// <param name="body">The request body.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="timestamp">The timestamp from the request.</param>
    /// <param name="apiKey">The API key used for signing.</param>
    /// <param name="maxAgeMs">Maximum age of the signature in milliseconds (default: 5 minutes).</param>
    /// <returns>True if the signature is valid.</returns>
    public static bool VerifyRequestSignature(
        string body,
        string signature,
        long timestamp,
        string apiKey,
        long maxAgeMs = 300000)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var age = currentTime - timestamp;

        // Check if the signature is too old or in the future
        if (age > maxAgeMs || age < 0)
            return false;

        var message = $"{timestamp}.{body}";
        var expectedSignature = GenerateHMACSHA256(message, apiKey);

        return string.Equals(signature, expectedSignature, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the current environment is production.
    /// </summary>
    /// <returns>True if running in production.</returns>
    public static bool IsProductionEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Canonicalizes an object for consistent signature generation.
    /// Keys are sorted alphabetically and values are serialized deterministically.
    /// </summary>
    /// <param name="obj">The dictionary to canonicalize.</param>
    /// <returns>A canonical string representation of the object.</returns>
    public static string CanonicalizeObject(Dictionary<string, object?> obj)
    {
        if (obj == null || obj.Count == 0)
            return "{}";

        var sortedKeys = obj.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var parts = new List<string>();

        foreach (var key in sortedKeys)
        {
            var value = obj[key];
            var valueStr = CanonicalizeValue(value);
            parts.Add($"\"{EscapeJsonString(key)}\":{valueStr}");
        }

        return "{" + string.Join(",", parts) + "}";
    }

    /// <summary>
    /// Canonicalizes a value for signature generation.
    /// </summary>
    private static string CanonicalizeValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            string s => $"\"{EscapeJsonString(s)}\"",
            int or long or short or byte => value.ToString()!,
            float or double or decimal => FormatNumber(Convert.ToDouble(value)),
            Dictionary<string, object?> dict => CanonicalizeObject(dict),
            IDictionary<string, object> dict => CanonicalizeObject(dict.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)),
            JsonElement jsonElement => CanonicalizeJsonElement(jsonElement),
            System.Collections.IEnumerable arr when arr is not string => "[" + string.Join(",", arr.Cast<object?>().Select(CanonicalizeValue)) + "]",
            _ => $"\"{EscapeJsonString(value.ToString() ?? "")}\"",
        };
    }

    /// <summary>
    /// Canonicalizes a JsonElement for signature generation.
    /// </summary>
    private static string CanonicalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => FormatNumber(element.GetDouble()),
            JsonValueKind.String => $"\"{EscapeJsonString(element.GetString() ?? "")}\"",
            JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(CanonicalizeJsonElement)) + "]",
            JsonValueKind.Object => CanonicalizeJsonObject(element),
            _ => "null"
        };
    }

    /// <summary>
    /// Canonicalizes a JSON object element with sorted keys.
    /// </summary>
    private static string CanonicalizeJsonObject(JsonElement element)
    {
        var properties = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"\"{EscapeJsonString(p.Name)}\":{CanonicalizeJsonElement(p.Value)}");
        return "{" + string.Join(",", properties) + "}";
    }

    /// <summary>
    /// Formats a number for canonical JSON representation.
    /// </summary>
    private static string FormatNumber(double value)
    {
        // Use invariant culture to ensure consistent decimal separators
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
        {
            return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Escapes a string for JSON representation.
    /// </summary>
    private static string EscapeJsonString(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append($"\\u{(int)c:x4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Verifies a bootstrap signature using HMAC-SHA256.
    /// </summary>
    /// <param name="bootstrap">The bootstrap configuration containing flags, signature, and timestamp.</param>
    /// <param name="apiKey">The API key used for signature verification.</param>
    /// <param name="config">The verification configuration.</param>
    /// <returns>A tuple containing whether the signature is valid and an optional error message.</returns>
    public static (bool Valid, string? Error) VerifyBootstrapSignature(
        BootstrapConfig bootstrap,
        string apiKey,
        BootstrapVerificationConfig config)
    {
        if (bootstrap == null)
            return (false, "Bootstrap config is null");

        if (!config.Enabled)
            return (true, null);

        if (string.IsNullOrEmpty(bootstrap.Signature))
            return (true, null); // No signature to verify, allow unsigned bootstrap

        // Verify timestamp if present
        if (bootstrap.Timestamp.HasValue)
        {
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var age = currentTime - bootstrap.Timestamp.Value;

            if (age > config.MaxAge)
            {
                return (false, $"Bootstrap data has expired. Age: {age}ms, MaxAge: {config.MaxAge}ms");
            }

            if (age < 0)
            {
                return (false, "Bootstrap timestamp is in the future");
            }
        }

        // Create canonical representation and compute expected signature
        var canonicalData = CanonicalizeObject(bootstrap.Flags);
        var message = bootstrap.Timestamp.HasValue
            ? $"{bootstrap.Timestamp.Value}.{canonicalData}"
            : canonicalData;

        var expectedSignature = GenerateHMACSHA256(message, apiKey);

        // Use constant-time comparison to prevent timing attacks
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant());
        var actualBytes = Encoding.UTF8.GetBytes(bootstrap.Signature.ToLowerInvariant());

        if (expectedBytes.Length != actualBytes.Length)
        {
            return (false, "Invalid bootstrap signature");
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return (false, "Invalid bootstrap signature");
        }

        return (true, null);
    }

    /// <summary>
    /// Creates a signed bootstrap configuration.
    /// </summary>
    /// <param name="flags">The flag values to include in the bootstrap.</param>
    /// <param name="apiKey">The API key to use for signing.</param>
    /// <param name="timestamp">Optional timestamp (defaults to current time).</param>
    /// <returns>A BootstrapConfig with signature.</returns>
    public static BootstrapConfig CreateSignedBootstrap(
        Dictionary<string, object?> flags,
        string apiKey,
        long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var canonicalData = CanonicalizeObject(flags);
        var message = $"{ts}.{canonicalData}";
        var signature = GenerateHMACSHA256(message, apiKey);

        return new BootstrapConfig
        {
            Flags = flags,
            Signature = signature,
            Timestamp = ts
        };
    }
}

/// <summary>
/// Provides AES-256-CBC + HMAC-SHA256 encryption for cache data (Encrypt-then-MAC).
/// Uses PBKDF2 to derive the encryption key from the API key.
/// This provides authenticated encryption compatible with all .NET platforms.
/// </summary>
public static class CacheEncryption
{
    private const int KeySize = 32; // 256 bits for AES-256
    private const int IvSize = 16; // 128 bits for AES CBC IV
    private const int HmacSize = 32; // 256 bits for HMAC-SHA256
    private const int SaltSize = 16; // 128 bits for PBKDF2 salt
    private const int Iterations = 100000; // PBKDF2 iterations

    /// <summary>
    /// Derives encryption and HMAC keys from an API key using PBKDF2.
    /// </summary>
    /// <param name="apiKey">The API key to derive from.</param>
    /// <param name="salt">The salt for key derivation.</param>
    /// <returns>A tuple containing the encryption key and HMAC key.</returns>
    public static (byte[] EncryptionKey, byte[] HmacKey) DeriveKeys(string apiKey, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            apiKey,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        // Derive 64 bytes: 32 for encryption key + 32 for HMAC key
        var keyMaterial = pbkdf2.GetBytes(KeySize * 2);
        var encryptionKey = new byte[KeySize];
        var hmacKey = new byte[KeySize];

        Buffer.BlockCopy(keyMaterial, 0, encryptionKey, 0, KeySize);
        Buffer.BlockCopy(keyMaterial, KeySize, hmacKey, 0, KeySize);

        return (encryptionKey, hmacKey);
    }

    /// <summary>
    /// Derives an encryption key from an API key using PBKDF2.
    /// </summary>
    /// <param name="apiKey">The API key to derive from.</param>
    /// <param name="salt">The salt for key derivation.</param>
    /// <returns>The derived key bytes.</returns>
    public static byte[] DeriveKey(string apiKey, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            apiKey,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }

    /// <summary>
    /// Encrypts data using AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC).
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="apiKey">The API key to derive the encryption key from.</param>
    /// <returns>The encrypted data with salt, IV, HMAC, and ciphertext.</returns>
    public static byte[] Encrypt(byte[] plaintext, string apiKey)
    {
        // Generate random salt and IV
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);

        // Derive keys
        var (encryptionKey, hmacKey) = DeriveKeys(apiKey, salt);

        // Encrypt using AES-CBC
        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] ciphertext;
        using (var encryptor = aes.CreateEncryptor())
        using (var ms = new MemoryStream())
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(plaintext, 0, plaintext.Length);
            cs.FlushFinalBlock();
            ciphertext = ms.ToArray();
        }

        // Calculate HMAC over salt + IV + ciphertext
        var dataToAuthenticate = new byte[SaltSize + IvSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, dataToAuthenticate, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, dataToAuthenticate, SaltSize, IvSize);
        Buffer.BlockCopy(ciphertext, 0, dataToAuthenticate, SaltSize + IvSize, ciphertext.Length);

        byte[] hmac;
        using (var hmacSha256 = new HMACSHA256(hmacKey))
        {
            hmac = hmacSha256.ComputeHash(dataToAuthenticate);
        }

        // Combine: salt + IV + HMAC + ciphertext
        var result = new byte[SaltSize + IvSize + HmacSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, result, SaltSize, IvSize);
        Buffer.BlockCopy(hmac, 0, result, SaltSize + IvSize, HmacSize);
        Buffer.BlockCopy(ciphertext, 0, result, SaltSize + IvSize + HmacSize, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Encrypts a string using AES-256-CBC + HMAC-SHA256.
    /// </summary>
    /// <param name="plaintext">The string to encrypt.</param>
    /// <param name="apiKey">The API key to derive the encryption key from.</param>
    /// <returns>The base64-encoded encrypted data.</returns>
    public static string EncryptString(string plaintext, string apiKey)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encryptedBytes = Encrypt(plaintextBytes, apiKey);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// Decrypts data that was encrypted with AES-256-CBC + HMAC-SHA256.
    /// </summary>
    /// <param name="encryptedData">The encrypted data (salt + IV + HMAC + ciphertext).</param>
    /// <param name="apiKey">The API key to derive the decryption key from.</param>
    /// <returns>The decrypted data.</returns>
    /// <exception cref="CryptographicException">Thrown if decryption or authentication fails.</exception>
    public static byte[] Decrypt(byte[] encryptedData, string apiKey)
    {
        if (encryptedData.Length < SaltSize + IvSize + HmacSize + 1)
            throw new CryptographicException("Encrypted data is too short");

        // Extract components
        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        var storedHmac = new byte[HmacSize];
        var ciphertextLength = encryptedData.Length - SaltSize - IvSize - HmacSize;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(encryptedData, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(encryptedData, SaltSize, iv, 0, IvSize);
        Buffer.BlockCopy(encryptedData, SaltSize + IvSize, storedHmac, 0, HmacSize);
        Buffer.BlockCopy(encryptedData, SaltSize + IvSize + HmacSize, ciphertext, 0, ciphertextLength);

        // Derive keys
        var (encryptionKey, hmacKey) = DeriveKeys(apiKey, salt);

        // Verify HMAC
        var dataToAuthenticate = new byte[SaltSize + IvSize + ciphertextLength];
        Buffer.BlockCopy(salt, 0, dataToAuthenticate, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, dataToAuthenticate, SaltSize, IvSize);
        Buffer.BlockCopy(ciphertext, 0, dataToAuthenticate, SaltSize + IvSize, ciphertextLength);

        byte[] computedHmac;
        using (var hmacSha256 = new HMACSHA256(hmacKey))
        {
            computedHmac = hmacSha256.ComputeHash(dataToAuthenticate);
        }

        // Constant-time comparison to prevent timing attacks
        if (!CryptographicOperations.FixedTimeEquals(storedHmac, computedHmac))
            throw new CryptographicException("Authentication failed: HMAC mismatch");

        // Decrypt using AES-CBC
        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(ciphertext);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var resultStream = new MemoryStream();
        cs.CopyTo(resultStream);
        return resultStream.ToArray();
    }

    /// <summary>
    /// Decrypts a base64-encoded string that was encrypted with AES-256-CBC + HMAC-SHA256.
    /// </summary>
    /// <param name="encryptedBase64">The base64-encoded encrypted data.</param>
    /// <param name="apiKey">The API key to derive the decryption key from.</param>
    /// <returns>The decrypted string.</returns>
    /// <exception cref="CryptographicException">Thrown if decryption or authentication fails.</exception>
    public static string DecryptString(string encryptedBase64, string apiKey)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedBase64);
        var decryptedBytes = Decrypt(encryptedBytes, apiKey);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <summary>
    /// Encrypts an object as JSON using AES-256-CBC + HMAC-SHA256.
    /// </summary>
    /// <typeparam name="T">The type of object to encrypt.</typeparam>
    /// <param name="obj">The object to encrypt.</param>
    /// <param name="apiKey">The API key to derive the encryption key from.</param>
    /// <returns>The base64-encoded encrypted data.</returns>
    public static string EncryptObject<T>(T obj, string apiKey)
    {
        var json = JsonSerializer.Serialize(obj);
        return EncryptString(json, apiKey);
    }

    /// <summary>
    /// Decrypts and deserializes an object that was encrypted with EncryptObject.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="encryptedBase64">The base64-encoded encrypted data.</param>
    /// <param name="apiKey">The API key to derive the decryption key from.</param>
    /// <returns>The decrypted and deserialized object.</returns>
    /// <exception cref="CryptographicException">Thrown if decryption or authentication fails.</exception>
    public static T? DecryptObject<T>(string encryptedBase64, string apiKey)
    {
        var json = DecryptString(encryptedBase64, apiKey);
        return JsonSerializer.Deserialize<T>(json);
    }
}
