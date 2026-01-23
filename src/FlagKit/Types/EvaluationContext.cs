namespace FlagKit.Types;

/// <summary>
/// Context for flag evaluation containing user and custom attributes.
/// </summary>
public record EvaluationContext
{
    private const string PrivateAttributePrefix = "_";

    public string? UserId { get; init; }
    public IReadOnlyDictionary<string, FlagValue> Attributes { get; init; } =
        new Dictionary<string, FlagValue>();

    public EvaluationContext WithUserId(string userId) =>
        this with { UserId = userId };

    public EvaluationContext WithAttribute(string key, object? value) =>
        this with
        {
            Attributes = new Dictionary<string, FlagValue>(Attributes)
            {
                [key] = FlagValue.From(value)
            }
        };

    public EvaluationContext WithAttributes(IDictionary<string, object?> attrs) =>
        this with
        {
            Attributes = new Dictionary<string, FlagValue>(Attributes)
                .Concat(attrs.Select(kvp => new KeyValuePair<string, FlagValue>(kvp.Key, FlagValue.From(kvp.Value))))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

    public EvaluationContext Merge(EvaluationContext? other)
    {
        if (other == null) return this;

        return this with
        {
            UserId = other.UserId ?? UserId,
            Attributes = new Dictionary<string, FlagValue>(Attributes)
                .Concat(other.Attributes)
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value)
        };
    }

    public EvaluationContext StripPrivateAttributes() =>
        this with
        {
            Attributes = Attributes
                .Where(kvp => !kvp.Key.StartsWith(PrivateAttributePrefix))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

    public bool IsEmpty => UserId == null && !Attributes.Any();

    public FlagValue? this[string key] =>
        Attributes.TryGetValue(key, out var value) ? value : null;

    public Dictionary<string, object?> ToDictionary()
    {
        var result = new Dictionary<string, object?>();
        if (UserId != null) result["userId"] = UserId;
        if (Attributes.Any())
        {
            result["attributes"] = Attributes.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToObject());
        }
        return result;
    }

    public class Builder
    {
        private string? _userId;
        private readonly Dictionary<string, FlagValue> _attributes = new();

        public Builder UserId(string userId)
        {
            _userId = userId;
            return this;
        }

        public Builder Attribute(string key, bool value)
        {
            _attributes[key] = new FlagValue.BoolFlagValue(value);
            return this;
        }

        public Builder Attribute(string key, string value)
        {
            _attributes[key] = new FlagValue.StringFlagValue(value);
            return this;
        }

        public Builder Attribute(string key, long value)
        {
            _attributes[key] = new FlagValue.IntFlagValue(value);
            return this;
        }

        public Builder Attribute(string key, double value)
        {
            _attributes[key] = new FlagValue.DoubleFlagValue(value);
            return this;
        }

        public EvaluationContext Build() =>
            new() { UserId = _userId, Attributes = _attributes };
    }

    public static Builder CreateBuilder() => new();
}
