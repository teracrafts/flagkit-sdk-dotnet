using System.Text.RegularExpressions;

namespace FlagKit.Utils;

/// <summary>
/// Semantic version comparison utilities for SDK version metadata handling.
/// These utilities are used to compare the current SDK version against
/// server-provided version requirements (min, recommended, latest).
/// </summary>
public static class VersionUtils
{
    /// <summary>
    /// Parsed semantic version components.
    /// </summary>
    public record SemanticVersion(int Major, int Minor, int Patch);

    /// <summary>
    /// Pattern for parsing semantic version strings.
    /// Allows optional 'v' or 'V' prefix and pre-release/build suffixes (which are ignored for comparison).
    /// </summary>
    private static readonly Regex VersionPattern = new(@"^[vV]?(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Maximum allowed value for version components (defensive limit).
    /// </summary>
    private const int MaxVersionComponent = 999999999;

    /// <summary>
    /// Parse a semantic version string into numeric components.
    /// Returns null if the version is not a valid semver.
    /// </summary>
    /// <param name="version">The version string to parse (e.g., "1.0.0", "v1.2.3", "1.0.0-beta.1").</param>
    /// <returns>A SemanticVersion record, or null if parsing fails.</returns>
    public static SemanticVersion? ParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var match = VersionPattern.Match(version.Trim());
        if (!match.Success)
        {
            return null;
        }

        // Use TryParse for defensive parsing (handles overflow)
        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return null;
        }

        // Validate components are within reasonable bounds
        if (major < 0 || major > MaxVersionComponent ||
            minor < 0 || minor > MaxVersionComponent ||
            patch < 0 || patch > MaxVersionComponent)
        {
            return null;
        }

        return new SemanticVersion(major, minor, patch);
    }

    /// <summary>
    /// Compare two semantic versions.
    /// Returns:
    ///   - negative number if a &lt; b
    ///   - 0 if a == b
    ///   - positive number if a &gt; b
    /// Returns 0 if either version is invalid.
    /// </summary>
    /// <param name="a">First version string.</param>
    /// <param name="b">Second version string.</param>
    /// <returns>Comparison result.</returns>
    public static int CompareVersions(string? a, string? b)
    {
        var parsedA = ParseVersion(a);
        var parsedB = ParseVersion(b);

        if (parsedA == null || parsedB == null)
        {
            return 0;
        }

        // Compare major
        if (parsedA.Major != parsedB.Major)
        {
            return parsedA.Major - parsedB.Major;
        }

        // Compare minor
        if (parsedA.Minor != parsedB.Minor)
        {
            return parsedA.Minor - parsedB.Minor;
        }

        // Compare patch
        return parsedA.Patch - parsedB.Patch;
    }

    /// <summary>
    /// Check if version a is less than version b.
    /// </summary>
    /// <param name="a">First version string.</param>
    /// <param name="b">Second version string.</param>
    /// <returns>True if a &lt; b.</returns>
    public static bool IsVersionLessThan(string? a, string? b)
    {
        return CompareVersions(a, b) < 0;
    }

    /// <summary>
    /// Check if version a is greater than or equal to version b.
    /// </summary>
    /// <param name="a">First version string.</param>
    /// <param name="b">Second version string.</param>
    /// <returns>True if a &gt;= b.</returns>
    public static bool IsVersionAtLeast(string? a, string? b)
    {
        return CompareVersions(a, b) >= 0;
    }
}
