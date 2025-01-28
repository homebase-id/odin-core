using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using Testcontainers.Redis;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

public class OdinCacheTests
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

        services.AddLogging();

        services.AddSingleton(new OdinCacheKeyPrefix("some-prefix"));
        services.AddSingleton<IOdinCache, OdinCache>();
        services.AddCoreCacheServices(new OdinCacheOptions
        {
            Level2CacheType = level2CacheType,
            Level2Configuration = _redisContainer?.GetConnectionString() ?? ""
        });

        var builder = new ContainerBuilder();
        builder.Populate(services);
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

        var cache = _services!.Resolve<IOdinCache>();

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

        var cache = _services!.Resolve<IOdinCache>();

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

        var cache = _services!.Resolve<IOdinCache>();

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

    public class PocoA
    {
        public Guid Id { get; set; }
        public Guid Uuid { get; set; }
    }

    #endregion

}

