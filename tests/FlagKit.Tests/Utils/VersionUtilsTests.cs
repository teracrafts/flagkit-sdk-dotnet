using FlagKit.Utils;
using Xunit;

namespace FlagKit.Tests.Utils;

public class VersionUtilsTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.0.0", 1, 0, 0)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.0.0", 1, 0, 0)]  // Uppercase V
    [InlineData("V1.2.3", 1, 2, 3)]  // Uppercase V
    [InlineData("1.0.0-beta.1", 1, 0, 0)]
    [InlineData("1.2.3-alpha", 1, 2, 3)]
    [InlineData("1.2.3+build.123", 1, 2, 3)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("  1.2.3", 1, 2, 3)]  // Leading whitespace
    [InlineData("1.2.3  ", 1, 2, 3)]  // Trailing whitespace
    [InlineData("  1.2.3  ", 1, 2, 3)]  // Surrounding whitespace
    [InlineData("  v1.0.0  ", 1, 0, 0)]  // Whitespace with v prefix
    [InlineData("999999999.999999999.999999999", 999999999, 999999999, 999999999)]  // Max boundary
    public void ParseVersion_ValidVersions_ReturnsCorrectComponents(
        string version, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = VersionUtils.ParseVersion(version);

        Assert.NotNull(result);
        Assert.Equal(expectedMajor, result.Major);
        Assert.Equal(expectedMinor, result.Minor);
        Assert.Equal(expectedPatch, result.Patch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("a.b.c")]
    [InlineData("1.0.a")]
    [InlineData("1000000000.0.0")]  // Exceeds max
    [InlineData("0.1000000000.0")]  // Exceeds max in minor
    [InlineData("0.0.1000000000")]  // Exceeds max in patch
    public void ParseVersion_InvalidVersions_ReturnsNull(string? version)
    {
        var result = VersionUtils.ParseVersion(version);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("2.0.0", "1.0.0", false)]
    [InlineData("1.1.0", "1.0.0", false)]
    [InlineData("1.0.1", "1.0.0", false)]
    [InlineData("1.9.9", "2.0.0", true)]
    [InlineData("v1.0.0", "v1.0.1", true)]
    public void IsVersionLessThan_ReturnsCorrectResult(string a, string b, bool expected)
    {
        var result = VersionUtils.IsVersionLessThan(a, b);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("invalid", "1.0.0", false)]
    [InlineData("1.0.0", "invalid", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("1.0.0", null, false)]
    public void IsVersionLessThan_InvalidVersions_ReturnsFalse(string? a, string? b, bool expected)
    {
        var result = VersionUtils.IsVersionLessThan(a, b);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", true)]
    [InlineData("2.0.0", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "2.0.0", false)]
    [InlineData("1.0.0", "1.1.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    public void IsVersionAtLeast_ReturnsCorrectResult(string a, string b, bool expected)
    {
        var result = VersionUtils.IsVersionAtLeast(a, b);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.2.3", "1.2.3", 0)]
    public void CompareVersions_ReturnsCorrectSign(string a, string b, int expectedSign)
    {
        var result = VersionUtils.CompareVersions(a, b);

        if (expectedSign < 0)
            Assert.True(result < 0, $"Expected negative, got {result}");
        else if (expectedSign > 0)
            Assert.True(result > 0, $"Expected positive, got {result}");
        else
            Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("invalid", "1.0.0", 0)]
    [InlineData("1.0.0", "invalid", 0)]
    [InlineData(null, "1.0.0", 0)]
    [InlineData("1.0.0", null, 0)]
    [InlineData(null, null, 0)]
    public void CompareVersions_InvalidVersions_ReturnsZero(string? a, string? b, int expected)
    {
        var result = VersionUtils.CompareVersions(a, b);

        Assert.Equal(expected, result);
    }
}
