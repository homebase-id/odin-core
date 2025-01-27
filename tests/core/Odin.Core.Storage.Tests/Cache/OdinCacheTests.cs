using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

public class OdinCacheTests
{
    private RedisContainer? _redisContainer;
    private ILifetimeScope? _services;

    [SetUp]
    public async Task Setup()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:latest")
            .Build();
        await _redisContainer.StartAsync();
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
        }
    }

    //

    [Test]
    public async Task ItShouldSetAndGetLevel1()
    {
        _services = new ServiceCollection()
            .AddServices()
            .AddLevel1Cache()
            .BuildContainer();

        var cache = _services!.Resolve<IFusionCache>();

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

}