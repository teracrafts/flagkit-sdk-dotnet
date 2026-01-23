using System.Text.Json.Serialization;

namespace FlagKit;

/// <summary>
/// Types of feature flags.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlagType
{
    Boolean,
    String,
    Number,
    Json
}

public static class FlagTypeExtensions
{
    public static FlagType Infer(object? value) => value switch
    {
        bool => FlagType.Boolean,
        string => FlagType.String,
        int or long or float or double or decimal => FlagType.Number,
        _ => FlagType.Json
    };
}
