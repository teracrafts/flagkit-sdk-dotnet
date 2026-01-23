using FlagKit.Core;
using FlagKit.Types;
using Xunit;

namespace FlagKit.Tests;

public class CacheTests
{
    [Fact]
    public void Set_And_Get_Returns_Value()
    {
        var cache = new Cache<string, string>();

        cache.Set("key", "value");
        var result = cache.Get("key");

        Assert.Equal("value", result);
    }

    [Fact]
    public void Get_NonExistent_Returns_Null()
    {
        var cache = new Cache<string, string>();

        var result = cache.Get("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void Has_Returns_True_For_Existing_Key()
    {
        var cache = new Cache<string, string>();
        cache.Set("key", "value");

        Assert.True(cache.Has("key"));
    }

    [Fact]
    public void Has_Returns_False_For_NonExistent_Key()
    {
        var cache = new Cache<string, string>();

        Assert.False(cache.Has("nonexistent"));
    }

    [Fact]
    public void Remove_Deletes_Entry()
    {
        var cache = new Cache<string, string>();
        cache.Set("key", "value");

        cache.Remove("key");

        Assert.False(cache.Has("key"));
    }

    [Fact]
    public void Clear_Removes_All_Entries()
    {
        var cache = new Cache<string, string>();
        cache.Set("key1", "value1");
        cache.Set("key2", "value2");

        cache.Clear();

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Expired_Entry_Is_Not_Returned()
    {
        var cache = new Cache<string, string>(ttl: TimeSpan.FromMilliseconds(1));
        cache.Set("key", "value");

        Thread.Sleep(10);

        Assert.Null(cache.Get("key"));
    }

    [Fact]
    public void Custom_TTL_Overrides_Default()
    {
        var cache = new Cache<string, string>(ttl: TimeSpan.FromHours(1));
        cache.Set("key", "value", TimeSpan.FromMilliseconds(1));

        Thread.Sleep(10);

        Assert.Null(cache.Get("key"));
    }

    [Fact]
    public void Eviction_When_Over_MaxSize()
    {
        var cache = new Cache<string, int>(maxSize: 3, ttl: TimeSpan.FromHours(1));

        cache.Set("key1", 1);
        Thread.Sleep(5);
        cache.Set("key2", 2);
        Thread.Sleep(5);
        cache.Set("key3", 3);
        Thread.Sleep(5);
        cache.Set("key4", 4);

        Assert.True(cache.Count <= 3);
    }

    [Fact]
    public void FlagCache_SetAll_Stores_Multiple_Flags()
    {
        var cache = new FlagCache();
        var flags = new List<FlagState>
        {
            new() { Key = "flag1", Value = new FlagValue.BoolFlagValue(true) },
            new() { Key = "flag2", Value = new FlagValue.StringFlagValue("test") }
        };

        cache.SetAll(flags);

        Assert.Equal(2, cache.Count);
        Assert.NotNull(cache.Get("flag1"));
        Assert.NotNull(cache.Get("flag2"));
    }

    [Fact]
    public void FlagCache_GetAll_Returns_All_Flags()
    {
        var cache = new FlagCache();
        cache.Set("flag1", new FlagState { Key = "flag1", Value = new FlagValue.BoolFlagValue(true) });
        cache.Set("flag2", new FlagState { Key = "flag2", Value = new FlagValue.StringFlagValue("test") });

        var all = cache.GetAll();

        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey("flag1"));
        Assert.True(all.ContainsKey("flag2"));
    }
}
