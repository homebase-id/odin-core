#if RUN_REDIS_TESTS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using StackExchange.Redis;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

/// <summary>
/// #1909 across nodes: node A misses and loads the old value; node B's write invalidates; then A's fill lands. B's
/// remove never passes through A's wrapper, so only a remove count shared in Redis can stop A from storing what B's
/// write replaced, in A's memory and in the Redis cache every node reads.
/// </summary>
public class CrossNodeStaleFillTests
{
    private RedisContainer _redis = null!;
    private readonly List<ServiceProvider> _nodes = [];

    [OneTimeSetUp]
    public async Task StartRedis()
    {
        _redis = new RedisBuilder("redis:latest").Build();
        await _redis.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopRedis()
    {
        await _redis.DisposeAsync();
    }

    [TearDown]
    public async Task StopNodes()
    {
        foreach (var node in _nodes)
        {
            await node.DisposeAsync();
        }
        _nodes.Clear();
    }

    [Test]
    public async Task AFillOnOneNodeThatLoadedBeforeAnotherNodesRemoveDoesNotOutliveIt()
    {
        var nodeA = StartNode();
        var nodeB = StartNode();
        List<string> tags = ["row"];

        var stored = "old";
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var slowReaderOnA = nodeA.GetOrSetAsync("key", async _ =>
        {
            var value = stored; // node A's database read, before the write below
            loaded.SetResult();
            await releaseFill.Task;
            return value;
        }, TimeSpan.FromHours(1), tags: tags);

        await loaded.Task;
        stored = "new"; // node B's write commits...
        await nodeB.RemoveByTagAsync(tags); // ...and invalidates, after A's read
        releaseFill.SetResult();
        Assert.That(await slowReaderOnA, Is.EqualTo("old"), "the slow reader itself still answers what it read");

        var onB = await nodeB.GetOrSetAsync("key", _ => Task.FromResult(stored), TimeSpan.FromHours(1), tags: tags);
        Assert.That(onB, Is.EqualTo("new"), "node B must not get what its own write replaced, from A's fill in Redis");
        var onA = await nodeA.GetOrSetAsync("key", _ => Task.FromResult(stored), TimeSpan.FromHours(1), tags: tags);
        Assert.That(onA, Is.EqualTo("new"), "node A must not keep what B's write replaced");
    }

    // A node: its own FusionCache and memory cache, sharing the test's Redis for L2, the backplane and the count
    private ITenantLevel2Cache StartNode()
    {
        var services = new ServiceCollection();
        var connection = _redis.GetConnectionString();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connection));
        services.AddCoreCacheServices(new CacheConfiguration
        {
            MemoryCacheSizeLimit = long.MaxValue,
            MemoryCacheCompactionPercentage = 0.25,
            Level2CacheType = Level2CacheType.Redis
        });
        var provider = services.BuildServiceProvider();
        _nodes.Add(provider);

        return new TenantLevel2Cache(new CacheKeyPrefix("tenant"), provider.GetRequiredService<IFusionCache>(),
            provider.GetRequiredService<IRemovalCounterFactory>());
    }
}
#endif
