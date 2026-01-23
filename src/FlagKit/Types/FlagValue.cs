using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlagKit.Types;

/// <summary>
/// A type-safe wrapper for flag values.
/// </summary>
[JsonConverter(typeof(FlagValueJsonConverter))]
public abstract record FlagValue
{
    public abstract object? ToObject();
    public abstract FlagType InferredType { get; }

    public bool? BoolValue => (this as BoolFlagValue)?.Value;
    public string? StringValue => this switch
    {
        StringFlagValue s => s.Value,
        BoolFlagValue b => b.Value.ToString(),
        IntFlagValue i => i.Value.ToString(),
        DoubleFlagValue d => d.Value.ToString(),
        _ => null
    };
    public double? NumberValue => this switch
    {
        DoubleFlagValue d => d.Value,
        IntFlagValue i => i.Value,
        _ => null
    };
    public long? IntValue => this switch
    {
        IntFlagValue i => i.Value,
        DoubleFlagValue d => (long)d.Value,
        _ => null
    };
    public Dictionary<string, object?>? JsonValue => (this as JsonObjectFlagValue)?.Value
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToObject());

    public static FlagValue From(object? value) => value switch
    {
        null => NullFlagValue.Instance,
        bool b => new BoolFlagValue(b),
        string s => new StringFlagValue(s),
        int i => new IntFlagValue(i),
        long l => new IntFlagValue(l),
        float f => new DoubleFlagValue(f),
        double d => new DoubleFlagValue(d),
        decimal dec => new DoubleFlagValue((double)dec),
        Dictionary<string, object?> dict => new JsonObjectFlagValue(
            dict.ToDictionary(kvp => kvp.Key, kvp => From(kvp.Value))),
        IEnumerable<object?> arr => new JsonArrayFlagValue(arr.Select(From).ToList()),
        _ => NullFlagValue.Instance
    };

    public record BoolFlagValue(bool Value) : FlagValue
    {
        public override object ToObject() => Value;
        public override FlagType InferredType => FlagType.Boolean;
    }

    public record StringFlagValue(string Value) : FlagValue
    {
        public override object ToObject() => Value;
        public override FlagType InferredType => FlagType.String;
    }

    public record IntFlagValue(long Value) : FlagValue
    {
        public override object ToObject() => Value;
        public override FlagType InferredType => FlagType.Number;
    }

    public record DoubleFlagValue(double Value) : FlagValue
    {
        public override object ToObject() => Value;
        public override FlagType InferredType => FlagType.Number;
    }

    public record JsonObjectFlagValue(Dictionary<string, FlagValue> Value) : FlagValue
    {
        public override object ToObject() => Value.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToObject());
        public override FlagType InferredType => FlagType.Json;
    }

    public record JsonArrayFlagValue(List<FlagValue> Value) : FlagValue
    {
        public override object? ToObject() => Value.Select(v => v.ToObject()).ToList();
        public override FlagType InferredType => FlagType.Json;
    }

    public record NullFlagValue : FlagValue
    {
        public static NullFlagValue Instance { get; } = new();
        public override object? ToObject() => null;
        public override FlagType InferredType => FlagType.Json;
    }
}

public class FlagValueJsonConverter : JsonConverter<FlagValue>
{
    public override FlagValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => FlagValue.NullFlagValue.Instance,
            JsonTokenType.True => new FlagValue.BoolFlagValue(true),
            JsonTokenType.False => new FlagValue.BoolFlagValue(false),
            JsonTokenType.String => new FlagValue.StringFlagValue(reader.GetString()!),
            JsonTokenType.Number when reader.TryGetInt64(out var l) => new FlagValue.IntFlagValue(l),
            JsonTokenType.Number => new FlagValue.DoubleFlagValue(reader.GetDouble()),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            _ => throw new JsonException($"Unexpected token type: {reader.TokenType}")
        };
    }

    private FlagValue.JsonObjectFlagValue ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var dict = new Dictionary<string, FlagValue>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!;
            reader.Read();
            dict[key] = Read(ref reader, typeof(FlagValue), options);
        }
        return new FlagValue.JsonObjectFlagValue(dict);
    }

    private FlagValue.JsonArrayFlagValue ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<FlagValue>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            list.Add(Read(ref reader, typeof(FlagValue), options));
        }
        return new FlagValue.JsonArrayFlagValue(list);
    }

    public override void Write(Utf8JsonWriter writer, FlagValue value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case FlagValue.NullFlagValue:
                writer.WriteNullValue();
                break;
            case FlagValue.BoolFlagValue b:
                writer.WriteBooleanValue(b.Value);
                break;
            case FlagValue.StringFlagValue s:
                writer.WriteStringValue(s.Value);
                break;
            case FlagValue.IntFlagValue i:
                writer.WriteNumberValue(i.Value);
                break;
            case FlagValue.DoubleFlagValue d:
                writer.WriteNumberValue(d.Value);
                break;
            case FlagValue.JsonObjectFlagValue obj:
                writer.WriteStartObject();
                foreach (var (key, val) in obj.Value)
                {
                    writer.WritePropertyName(key);
                    Write(writer, val, options);
                }
                writer.WriteEndObject();
                break;
            case FlagValue.JsonArrayFlagValue arr:
                writer.WriteStartArray();
                foreach (var item in arr.Value)
                {
                    Write(writer, item, options);
                }
                writer.WriteEndArray();
                break;
        }
    }
}
