using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using Testcontainers.Redis;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

public class Level2CacheTests
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
        builder.AddCacheLevels("some-prefix");

        _services = builder.Build();
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

        var cache = _services!.Resolve<ILevel2Cache>();

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

        var cache = _services!.Resolve<ILevel2Cache>();

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

        var cache = _services!.Resolve<ILevel2Cache>();

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

        var cache = _services!.Resolve<ILevel2Cache>();

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

        var cache = _services!.Resolve<ILevel2Cache>();

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

        // Ensure it is gone
        var record2 = cache.GetOrDefault<PocoA?>(key);
        Assert.That(record2, Is.Null);
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

        var cache = _services!.Resolve<ILevel2Cache>();

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

