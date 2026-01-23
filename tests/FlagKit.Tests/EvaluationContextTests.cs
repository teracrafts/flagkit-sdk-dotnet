using Xunit;

namespace FlagKit.Tests;

public class EvaluationContextTests
{
    [Fact]
    public void WithUserId_Sets_UserId()
    {
        var context = new EvaluationContext().WithUserId("user-123");

        Assert.Equal("user-123", context.UserId);
    }

    [Fact]
    public void WithAttribute_Adds_Attribute()
    {
        var context = new EvaluationContext()
            .WithAttribute("plan", "premium");

        Assert.Equal("premium", context["plan"]?.StringValue);
    }

    [Fact]
    public void WithAttributes_Adds_Multiple_Attributes()
    {
        var context = new EvaluationContext()
            .WithAttributes(new Dictionary<string, object?>
            {
                ["plan"] = "premium",
                ["beta"] = true
            });

        Assert.Equal("premium", context["plan"]?.StringValue);
        Assert.True(context["beta"]?.BoolValue);
    }

    [Fact]
    public void Merge_Combines_Contexts()
    {
        var base1 = new EvaluationContext()
            .WithUserId("user-1")
            .WithAttribute("plan", "free");

        var override1 = new EvaluationContext()
            .WithUserId("user-2")
            .WithAttribute("beta", true);

        var merged = base1.Merge(override1);

        Assert.Equal("user-2", merged.UserId);
        Assert.Equal("free", merged["plan"]?.StringValue);
        Assert.True(merged["beta"]?.BoolValue);
    }

    [Fact]
    public void Merge_With_Null_Returns_Original()
    {
        var context = new EvaluationContext().WithUserId("user-1");

        var merged = context.Merge(null);

        Assert.Same(context, merged);
    }

    [Fact]
    public void StripPrivateAttributes_Removes_Private_Attributes()
    {
        var context = new EvaluationContext()
            .WithAttribute("_privateKey", "secret")
            .WithAttribute("publicKey", "visible");

        var stripped = context.StripPrivateAttributes();

        Assert.Null(stripped["_privateKey"]);
        Assert.Equal("visible", stripped["publicKey"]?.StringValue);
    }

    [Fact]
    public void IsEmpty_Returns_True_For_Empty_Context()
    {
        var context = new EvaluationContext();

        Assert.True(context.IsEmpty);
    }

    [Fact]
    public void IsEmpty_Returns_False_With_UserId()
    {
        var context = new EvaluationContext().WithUserId("user-1");

        Assert.False(context.IsEmpty);
    }

    [Fact]
    public void ToDictionary_Includes_UserId_And_Attributes()
    {
        var context = new EvaluationContext()
            .WithUserId("user-123")
            .WithAttribute("plan", "premium");

        var dict = context.ToDictionary();

        Assert.Equal("user-123", dict["userId"]);
        Assert.NotNull(dict["attributes"]);
    }

    [Fact]
    public void Builder_Creates_Context_With_All_Types()
    {
        var context = EvaluationContext.CreateBuilder()
            .UserId("user-123")
            .Attribute("active", true)
            .Attribute("name", "Test")
            .Attribute("age", 25L)
            .Attribute("score", 99.5)
            .Build();

        Assert.Equal("user-123", context.UserId);
        Assert.True(context["active"]?.BoolValue);
        Assert.Equal("Test", context["name"]?.StringValue);
        Assert.Equal(25L, context["age"]?.IntValue);
        Assert.Equal(99.5, context["score"]?.NumberValue);
    }
}
