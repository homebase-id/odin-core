using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

public class LevelXCacheTests
{
    private RedisContainer? _redisContainer;
    private ILifetimeScope? _services;

    [SetUp]
    public void Setup()
    {
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
            _redisContainer = null;
        }
        _services?.Dispose();
        _services = null;
    }

    //

    private async Task RegisterServicesAsync(Level2CacheType level2CacheType)
    {
        if (level2CacheType == Level2CacheType.Redis)
        {
            _redisContainer = new RedisBuilder()
                .WithImage("redis:latest")
                .Build();
            await _redisContainer.StartAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddCoreCacheServices(new CacheConfiguration
        {
            Level2CacheType = level2CacheType,
            Level2Configuration = _redisContainer?.GetConnectionString() ?? ""
        });

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.AddGlobalCaches();
        builder.AddTenantCaches("frodo.me");

        _services = builder.Build();
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldCreateCorrectCacheKeys(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var globalLevel1Cache = _services!.Resolve<IGlobalLevel1Cache>();
        Assert.That(globalLevel1Cache.CacheKeyPrefix, Is.EqualTo("global:L1"));

        var globalLevel2Cache = _services!.Resolve<IGlobalLevel2Cache>();
        Assert.That(globalLevel2Cache.CacheKeyPrefix, Is.EqualTo("global:L2"));

        var globalLevel1CacheGeneric = _services!.Resolve<IGlobalLevel1Cache<LevelXCacheTests>>();
        Assert.That(globalLevel1CacheGeneric.CacheKeyPrefix, Is.EqualTo($"global:{GetType().FullName}:L1"));

        var globalLevel2CacheGeneric = _services!.Resolve<IGlobalLevel2Cache<LevelXCacheTests>>();
        Assert.That(globalLevel2CacheGeneric.CacheKeyPrefix, Is.EqualTo($"global:{GetType().FullName}:L2"));

        var tenantLevel1Cache = _services!.Resolve<ITenantLevel1Cache>();
        Assert.That(tenantLevel1Cache.CacheKeyPrefix, Is.EqualTo("frodo.me:L1"));

        var tenantLevel2Cache = _services!.Resolve<ITenantLevel2Cache>();
        Assert.That(tenantLevel2Cache.CacheKeyPrefix, Is.EqualTo("frodo.me:L2"));

        var tenantLevel1CacheGeneric = _services!.Resolve<ITenantLevel1Cache<LevelXCacheTests>>();
        Assert.That(tenantLevel1CacheGeneric.CacheKeyPrefix, Is.EqualTo($"frodo.me:{GetType().FullName}:L1"));

        var tenantLevel2CacheGeneric = _services!.Resolve<ITenantLevel2Cache<LevelXCacheTests>>();
        Assert.That(tenantLevel2CacheGeneric.CacheKeyPrefix, Is.EqualTo($"frodo.me:{GetType().FullName}:L2"));
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldGetAndSet(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();

        var record1 = await cache.GetOrSetAsync<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id),
            TimeSpan.FromSeconds(30)
        );

        var record2 = cache.GetOrSet<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id).Result,
            TimeSpan.FromSeconds(30)
        );

        Assert.That(record1!.Id, Is.EqualTo(record2!.Id));
        Assert.That(record1.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record1.Uuid, Is.EqualTo(record2.Uuid));

        var cacheKeyPrefix = _services!.Resolve<CacheKeyPrefix>();
        var key = $"{cacheKeyPrefix.Prefix}:L2:poco:{id}";
        var fusion = _services!.Resolve<IFusionCache>();
        var record3 = fusion.TryGet<PocoA?>(key);

        Assert.That(record3.Value!.Id, Is.EqualTo(record2!.Id));
        Assert.That(record3.Value!.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record3.Value!.Uuid, Is.EqualTo(record2.Uuid));
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldGetAndSetGeneric(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache<LevelXCacheTests>>();

        var id = Guid.NewGuid();

        var record1 = await cache.GetOrSetAsync<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id),
            TimeSpan.FromSeconds(30)
        );

        var record2 = cache.GetOrSet<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id).Result,
            TimeSpan.FromSeconds(30)
        );

        Assert.That(record1!.Id, Is.EqualTo(record2!.Id));
        Assert.That(record1.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record1.Uuid, Is.EqualTo(record2.Uuid));

        var cacheKeyPrefix = _services!.Resolve<CacheKeyPrefix>();
        var key = $"{cacheKeyPrefix.Prefix}:{GetType().FullName}:L2:poco:{id}";
        var fusion = _services!.Resolve<IFusionCache>();
        var record3 = fusion.TryGet<PocoA?>(key);

        Assert.That(record3.Value!.Id, Is.EqualTo(record2!.Id));
        Assert.That(record3.Value!.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record3.Value!.Uuid, Is.EqualTo(record2.Uuid));
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldGetNonNullValues(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();

        var record1 = await cache.GetOrSetAsync<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id),
            TimeSpan.FromSeconds(30)
        );

        var record2 = await cache.TryGetAsync<PocoA?>(
            $"poco:{id}"
        );

        Assert.That(record1!.Id, Is.EqualTo(record2.Value!.Id));
        Assert.That(record1.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record1.Uuid, Is.EqualTo(record2.Value!.Uuid));
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldGetNullValues(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();

        var record1 = await cache.GetOrSetAsync<PocoA?>(
            $"poco:{id}",
            _ => Task.FromResult<PocoA?>(null!),
            TimeSpan.FromSeconds(30)
        );

        var record2 = await cache.TryGetAsync<PocoA?>(
            $"poco:{id}"
        );

        var record3 = await cache.TryGetAsync<PocoA?>(
            $"notthere"
        );


        Assert.That(record1, Is.Null);
        Assert.That(record2.HasValue, Is.True);
        Assert.That(record2.Value, Is.Null);
        Assert.That(record3.HasValue, Is.False);

    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldSetAndRetrieveValues(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();
        var key = $"poco:{id}";
        var expectedRecord = new PocoA { Id = id, Uuid = Guid.NewGuid() };

        // Set the value in cache
        await cache.SetAsync(key, expectedRecord, TimeSpan.FromMinutes(10));

        // Retrieve the value
        var actualRecord = cache.GetOrDefault<PocoA?>(key);

        Assert.That(actualRecord, Is.Not.Null);
        Assert.That(actualRecord!.Id, Is.EqualTo(expectedRecord.Id));
        Assert.That(actualRecord.Uuid, Is.EqualTo(expectedRecord.Uuid));
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldRemoveValues(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();
        var key = $"poco:{id}";
        var expectedRecord = new PocoA { Id = id, Uuid = Guid.NewGuid() };

        // Set the value in cache
        await cache.SetAsync(key, expectedRecord, TimeSpan.FromMinutes(10));

        // Ensure it's retrievable
        var record1 = cache.GetOrDefault<PocoA?>(key);
        Assert.That(record1, Is.Not.Null);

        // Remove the value
        await cache.RemoveAsync(key);

        // Let redis catch up
        await Task.Delay(100);

        // Ensure it is gone
        var record2 = cache.GetOrDefault<PocoA?>(key);
        Assert.That(record2, Is.Null);

        // Once more to make sure we dont blow up when removing a non-existing key
        await cache.RemoveAsync(key);
        await Task.Delay(100);

        Assert.Pass();
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldCheckKeyExistence(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();
        var key = $"poco:{id}";
        var expectedRecord = new PocoA { Id = id, Uuid = Guid.NewGuid() };

        // Set the value in cache
        await cache.SetAsync(key, expectedRecord, TimeSpan.FromMinutes(10));

        // Ensure it exists
        var contains = await cache.ContainsAsync(key);
        Assert.That(contains, Is.True);

        // Remove the value
        await cache.RemoveAsync(key);

        // Ensure it does not exist
        contains = await cache.ContainsAsync(key);
        Assert.That(contains, Is.False);
    }


    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldRespectExpiration(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();
        var key = $"poco:{id}";
        var expectedRecord = new PocoA { Id = id, Uuid = Guid.NewGuid() };

        // Set the value with a short expiration
        await cache.SetAsync(key, expectedRecord, TimeSpan.FromMilliseconds(500));

        // Ensure it's retrievable
        var record1 = cache.GetOrDefault<PocoA?>(key);
        Assert.That(record1, Is.Not.Null);

        // Wait for expiration
        await Task.Delay(1000);

        // Ensure it's gone
        var record2 = cache.GetOrDefault<PocoA?>(key);
        Assert.That(record2, Is.Null);
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldRemoveByTag(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<ITenantLevel2Cache>();

        var id = Guid.NewGuid();
        var key = $"poco:{id}";
        var expectedRecord = new PocoA { Id = id, Uuid = Guid.NewGuid() };
        var tags = new[] { "foo", "bar" };

        // Set the value with a short expiration
        await cache.SetAsync(key, expectedRecord, TimeSpan.FromSeconds(500), tags);

        // Ensure it's retrievable
        var record1 = cache.GetOrDefault<PocoA?>(key);
        Assert.That(record1, Is.Not.Null);

        await cache.RemoveByTagAsync("foo");

        // Let redis catch up
        await Task.Delay(100);

        // Ensure it's gone
        var record2 = cache.GetOrDefault<PocoA?>(key);
        Assert.That(record2, Is.Null);
    }


    //

    #region POCO

    private Task<PocoA?> GetProductFromDbAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult<PocoA?>(null);
        }

        var record = new PocoA
        {
            Id = id,
            Uuid = Guid.NewGuid(),
        };

        return Task.FromResult(record)!;
    }

    // SEB:NOTE this must be public for the message pack serializer to work
    public class PocoA
    {
        public Guid Id { get; init; }
        public Guid Uuid { get; init; }
    }

    #endregion

}

