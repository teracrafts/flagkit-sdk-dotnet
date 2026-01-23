using System.Text.Json;
using Xunit;

namespace FlagKit.Tests;

public class FlagValueTests
{
    [Fact]
    public void BoolFlagValue_Properties()
    {
        var value = new FlagValue.BoolFlagValue(true);

        Assert.True(value.BoolValue);
        Assert.Equal(FlagType.Boolean, value.InferredType);
        Assert.Equal(true, value.ToObject());
    }

    [Fact]
    public void StringFlagValue_Properties()
    {
        var value = new FlagValue.StringFlagValue("test");

        Assert.Equal("test", value.StringValue);
        Assert.Equal(FlagType.String, value.InferredType);
        Assert.Equal("test", value.ToObject());
    }

    [Fact]
    public void IntFlagValue_Properties()
    {
        var value = new FlagValue.IntFlagValue(42);

        Assert.Equal(42L, value.IntValue);
        Assert.Equal(42.0, value.NumberValue);
        Assert.Equal(FlagType.Number, value.InferredType);
        Assert.Equal(42L, value.ToObject());
    }

    [Fact]
    public void DoubleFlagValue_Properties()
    {
        var value = new FlagValue.DoubleFlagValue(3.14);

        Assert.Equal(3.14, value.NumberValue);
        Assert.Equal(3L, value.IntValue);
        Assert.Equal(FlagType.Number, value.InferredType);
        Assert.Equal(3.14, value.ToObject());
    }

    [Fact]
    public void NullFlagValue_Properties()
    {
        var value = FlagValue.NullFlagValue.Instance;

        Assert.Null(value.BoolValue);
        Assert.Null(value.StringValue);
        Assert.Null(value.NumberValue);
        Assert.Null(value.IntValue);
        Assert.Null(value.ToObject());
    }

    [Fact]
    public void From_Bool()
    {
        var value = FlagValue.From(true);

        Assert.IsType<FlagValue.BoolFlagValue>(value);
        Assert.True(value.BoolValue);
    }

    [Fact]
    public void From_String()
    {
        var value = FlagValue.From("test");

        Assert.IsType<FlagValue.StringFlagValue>(value);
        Assert.Equal("test", value.StringValue);
    }

    [Fact]
    public void From_Int()
    {
        var value = FlagValue.From(42);

        Assert.IsType<FlagValue.IntFlagValue>(value);
        Assert.Equal(42L, value.IntValue);
    }

    [Fact]
    public void From_Long()
    {
        var value = FlagValue.From(42L);

        Assert.IsType<FlagValue.IntFlagValue>(value);
        Assert.Equal(42L, value.IntValue);
    }

    [Fact]
    public void From_Double()
    {
        var value = FlagValue.From(3.14);

        Assert.IsType<FlagValue.DoubleFlagValue>(value);
        Assert.Equal(3.14, value.NumberValue);
    }

    [Fact]
    public void From_Null()
    {
        var value = FlagValue.From(null);

        Assert.IsType<FlagValue.NullFlagValue>(value);
    }

    [Fact]
    public void From_Dictionary()
    {
        var value = FlagValue.From(new Dictionary<string, object?>
        {
            ["key"] = "value"
        });

        Assert.IsType<FlagValue.JsonObjectFlagValue>(value);
        var dict = value.JsonValue;
        Assert.NotNull(dict);
        Assert.Equal("value", dict["key"]);
    }

    [Fact]
    public void JsonSerialization_BoolValue()
    {
        var value = new FlagValue.BoolFlagValue(true);

        var json = JsonSerializer.Serialize(value);
        var deserialized = JsonSerializer.Deserialize<FlagValue>(json);

        Assert.IsType<FlagValue.BoolFlagValue>(deserialized);
        Assert.True(deserialized?.BoolValue);
    }

    [Fact]
    public void JsonSerialization_StringValue()
    {
        var value = new FlagValue.StringFlagValue("test");

        var json = JsonSerializer.Serialize(value);
        var deserialized = JsonSerializer.Deserialize<FlagValue>(json);

        Assert.IsType<FlagValue.StringFlagValue>(deserialized);
        Assert.Equal("test", deserialized?.StringValue);
    }

    [Fact]
    public void JsonSerialization_IntValue()
    {
        var value = new FlagValue.IntFlagValue(42);

        var json = JsonSerializer.Serialize(value);
        var deserialized = JsonSerializer.Deserialize<FlagValue>(json);

        Assert.IsType<FlagValue.IntFlagValue>(deserialized);
        Assert.Equal(42L, deserialized?.IntValue);
    }

    [Fact]
    public void JsonSerialization_DoubleValue()
    {
        var value = new FlagValue.DoubleFlagValue(3.14);

        var json = JsonSerializer.Serialize(value);
        var deserialized = JsonSerializer.Deserialize<FlagValue>(json);

        Assert.IsType<FlagValue.DoubleFlagValue>(deserialized);
        Assert.Equal(3.14, deserialized?.NumberValue);
    }

    [Fact]
    public void JsonSerialization_NullValue()
    {
        var value = FlagValue.NullFlagValue.Instance;

        var json = JsonSerializer.Serialize(value);
        var deserialized = JsonSerializer.Deserialize<FlagValue>(json);

        Assert.IsType<FlagValue.NullFlagValue>(deserialized);
    }

    [Fact]
    public void JsonSerialization_ObjectValue()
    {
        var value = new FlagValue.JsonObjectFlagValue(new Dictionary<string, FlagValue>
        {
            ["nested"] = new FlagValue.StringFlagValue("value")
        });

        var json = JsonSerializer.Serialize(value);
        var deserialized = JsonSerializer.Deserialize<FlagValue>(json);

        Assert.IsType<FlagValue.JsonObjectFlagValue>(deserialized);
        Assert.Equal("value", deserialized?.JsonValue?["nested"]);
    }

    [Fact]
    public void JsonSerialization_ArrayValue()
    {
        var value = new FlagValue.JsonArrayFlagValue(new List<FlagValue>
        {
            new FlagValue.StringFlagValue("a"),
            new FlagValue.StringFlagValue("b")
        });

        var json = JsonSerializer.Serialize(value);
        var deserialized = JsonSerializer.Deserialize<FlagValue>(json);

        Assert.IsType<FlagValue.JsonArrayFlagValue>(deserialized);
    }
}
