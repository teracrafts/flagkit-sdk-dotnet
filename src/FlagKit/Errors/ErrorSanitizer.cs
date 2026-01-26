using System.Text.RegularExpressions;

namespace FlagKit.Errors;

/// <summary>
/// Sanitizes error messages to remove sensitive information and prevent information leakage.
/// </summary>
public static class ErrorSanitizer
{
    private static readonly (Regex Pattern, string Replacement)[] Patterns = new[]
    {
        // Unix-style paths
        (new Regex(@"/(?:[\w.-]+/)+[\w.-]+", RegexOptions.Compiled), "[PATH]"),
        // Windows-style paths
        (new Regex(@"[A-Za-z]:\\(?:[\w\s.-]+\\)+[\w.-]*", RegexOptions.Compiled), "[PATH]"),
        // IP addresses
        (new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled), "[IP]"),
        // SDK API keys
        (new Regex(@"sdk_[a-zA-Z0-9_-]{8,}", RegexOptions.Compiled), "sdk_[REDACTED]"),
        // Server API keys
        (new Regex(@"srv_[a-zA-Z0-9_-]{8,}", RegexOptions.Compiled), "srv_[REDACTED]"),
        // CLI API keys
        (new Regex(@"cli_[a-zA-Z0-9_-]{8,}", RegexOptions.Compiled), "cli_[REDACTED]"),
        // Email addresses
        (new Regex(@"[\w.+-]+@[\w.-]+\.\w+", RegexOptions.Compiled), "[EMAIL]"),
        // Database connection strings
        (new Regex(@"(?i)(?:postgres|mysql|mongodb|redis)://[^\s]+", RegexOptions.Compiled), "[CONNECTION_STRING]"),
    };

    /// <summary>
    /// Sanitizes an error message by removing sensitive information.
    /// </summary>
    /// <param name="message">The original error message.</param>
    /// <param name="enabled">Whether sanitization is enabled. If false, returns the original message.</param>
    /// <returns>The sanitized error message, or the original if sanitization is disabled.</returns>
    public static string Sanitize(string message, bool enabled = true)
    {
        if (!enabled || string.IsNullOrEmpty(message))
        {
            return message;
        }

        var result = message;
        foreach (var (pattern, replacement) in Patterns)
        {
            result = pattern.Replace(result, replacement);
        }

        return result;
    }

    /// <summary>
    /// Checks if a message contains potentially sensitive information.
    /// </summary>
    /// <param name="message">The message to check.</param>
    /// <returns>True if the message contains sensitive patterns; otherwise, false.</returns>
    public static bool ContainsSensitiveInfo(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        foreach (var (pattern, _) in Patterns)
        {
            if (pattern.IsMatch(message))
            {
                return true;
            }
        }

        return false;
    }
}
