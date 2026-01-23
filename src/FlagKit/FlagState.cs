using System.Text.Json.Serialization;

namespace FlagKit;

/// <summary>
/// Represents the state of a feature flag.
/// </summary>
public record FlagState
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    public required FlagValue Value { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("flagType")]
    public FlagType? FlagType { get; init; }

    [JsonPropertyName("lastModified")]
    public string? LastModified { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    [JsonIgnore]
    public FlagType EffectiveFlagType => FlagType ?? Value.InferredType;

    [JsonIgnore]
    public bool BoolValue => Value.BoolValue ?? false;

    [JsonIgnore]
    public string? StringValue => Value.StringValue;

    [JsonIgnore]
    public double NumberValue => Value.NumberValue ?? 0.0;

    [JsonIgnore]
    public long IntValue => Value.IntValue ?? 0;

    [JsonIgnore]
    public Dictionary<string, object?>? JsonValue => Value.JsonValue;
}
