#if RUN_REDIS_TESTS
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
/// The node's shared Redis connection is closed once, by the container that owns it: FusionCache's backplane closes
/// the multiplexer it is handed, and two closes at once could throw inside StackExchange.Redis (#2069).
/// </summary>
public class SharedRedisConnectionDisposeTests
{
    private RedisContainer _redis = null!;

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

    [Test]
    public async Task OnlyTheContainerClosesTheSharedConnection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            CloseCountingMultiplexer.Wrap(ConnectionMultiplexer.Connect(_redis.GetConnectionString())));
        services.AddCoreCacheServices(new CacheConfiguration
        {
            MemoryCacheSizeLimit = long.MaxValue,
            MemoryCacheCompactionPercentage = 0.25,
            Level2CacheType = Level2CacheType.Redis
        });
        var provider = services.BuildServiceProvider();

        // Through the backplane too, so it has subscribed and will unsubscribe on dispose
        var fusion = provider.GetRequiredService<IFusionCache>();
        await fusion.SetAsync("key", "value");
        await fusion.RemoveAsync("key");

        var connection = (CloseCountingMultiplexer)(object)provider.GetRequiredService<IConnectionMultiplexer>();
        await provider.DisposeAsync();

        // The backplane unsubscribes on a thread-pool thread, so a second close would come late: wait for one, with room
        // to spare on a slow runner (before the fix it came within a second)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (connection.Closes.Length < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.That(connection.Closes, Is.EqualTo(new[] { nameof(IAsyncDisposable.DisposeAsync) }),
            "only the container's dispose closes the shared connection");
    }

    /// <summary>A multiplexer that records each close or dispose it is asked for</summary>
    public class CloseCountingMultiplexer : DispatchProxy
    {
        private IConnectionMultiplexer _inner = null!;
        private readonly ConcurrentQueue<string> _closes = new();

        public string[] Closes => _closes.ToArray();

        public static IConnectionMultiplexer Wrap(IConnectionMultiplexer inner)
        {
            var proxy = Create<IConnectionMultiplexer, CloseCountingMultiplexer>();
            ((CloseCountingMultiplexer)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name is nameof(IConnectionMultiplexer.Close) or nameof(IConnectionMultiplexer.CloseAsync)
                or nameof(IDisposable.Dispose) or nameof(IAsyncDisposable.DisposeAsync))
            {
                _closes.Enqueue(method.Name);
            }

            try
            {
                return method.Invoke(_inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // The cache handles the multiplexer's own exceptions, as in production, not reflection's wrapper
                ExceptionDispatchInfo.Throw(e.InnerException);
                throw;
            }
        }
    }
}
#endif
