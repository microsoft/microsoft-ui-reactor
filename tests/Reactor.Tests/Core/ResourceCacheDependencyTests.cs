using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public class ResourceCacheDependencyTests
{
    [Fact]
    public void Colliding_Dependencies_Miss_Until_The_Value_And_Dependencies_Are_Replaced()
    {
        using var cache = new QueryCache();
        cache.SetResource("key", 42, TimeSpan.Zero, TimeSpan.FromMinutes(1), [1, 32]);
        Assert.False(cache.TryGetResource<int>("key", [2, 1], out _));
        Assert.True(cache.TryGetResource<int>("key", [1, 32], out var entry));
        Assert.Equal(42, entry.Value);

        cache.SetResource("key", 99, TimeSpan.Zero, TimeSpan.FromMinutes(1), [2, 1]);
        Assert.False(cache.TryGetResource<int>("key", [1, 32], out _));
        Assert.True(cache.TryGetResource<int>("key", [2, 1], out entry));
        Assert.Equal(99, entry.Value);
        Assert.True(cache.TryGet<int>("key", out var publicRead));
        Assert.Equal(99, publicRead.Value);
    }

    [Fact]
    public void Public_Set_Retains_Explicit_Key_Behavior_And_Clears_A_Previous_Tag()
    {
        using var cache = new QueryCache();
        cache.SetResource("key", "tagged", TimeSpan.Zero, TimeSpan.FromMinutes(1), [1]);
        cache.Set("key", "explicit", TimeSpan.Zero, TimeSpan.FromMinutes(1));
        Assert.True(cache.TryGetResource<string>("key", [2], out var entry));
        Assert.Equal("explicit", entry.Value);
    }

    [Theory]
    [InlineData(60, 5)]
    [InlineData(5, 60)]
    public void Colliding_Write_Uses_Last_CacheTime_Without_Adding_A_Subscription(int firstSeconds, int lastSeconds)
    {
        using var cache = new QueryCache();
        var now = DateTime.UtcNow;
        cache.UtcNow = () => now;
        cache.Subscribe("key");
        cache.SetResource("key", "A", TimeSpan.Zero, TimeSpan.FromSeconds(firstSeconds), [1, 32]);
        cache.SetResource("key", "B", TimeSpan.Zero, TimeSpan.FromSeconds(lastSeconds), [2, 1]);
        Assert.True(cache.TryGetResource<string>("key", [2, 1], out var entry));
        Assert.Equal(1, entry.SubscriberCount);
        cache.Unsubscribe("key");
        now += TimeSpan.FromSeconds(lastSeconds - 1);
        Assert.Empty(cache.EvictNow());
        now += TimeSpan.FromSeconds(2);
        Assert.Contains("key", cache.EvictNow());
        Assert.False(cache.TryGetResource<string>("key", [1, 32], out _));
        Assert.False(cache.TryGetResource<string>("key", [2, 1], out _));
    }
}
