namespace FlagKit.Types;

/// <summary>
/// Result of evaluating a feature flag.
/// </summary>
public record EvaluationResult
{
    public required string FlagKey { get; init; }
    public required FlagValue Value { get; init; }
    public bool Enabled { get; init; }
    public EvaluationReason Reason { get; init; } = EvaluationReason.Default;
    public int Version { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public bool BoolValue => Value.BoolValue ?? false;
    public string? StringValue => Value.StringValue;
    public double NumberValue => Value.NumberValue ?? 0.0;
    public long IntValue => Value.IntValue ?? 0;
    public Dictionary<string, object?>? JsonValue => Value.JsonValue;

    public static EvaluationResult DefaultResult(string key, FlagValue defaultValue, EvaluationReason reason) =>
        new()
        {
            FlagKey = key,
            Value = defaultValue,
            Enabled = false,
            Reason = reason
        };
}
