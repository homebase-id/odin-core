using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Concurrency;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Odin.Core.Storage.Tests.Concurrency;

#nullable enable

#if RUN_REDIS_TESTS

public class RedisLockTests
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
        _services?.Dispose();
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
            _redisContainer = null;
        }
    }

    //

    private async Task RegisterServicesAsync()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:latest")
            .Build();
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        var redisConfig = _redisContainer?.GetConnectionString() ?? throw new InvalidOperationException();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));
        services.AddSingleton<INodeLock, RedisLock>();

        var builder = new ContainerBuilder();
        builder.Populate(services);

        _services = builder.Build();
    }

    //

    [Test]
    public async Task LockAsync_AcquiresAndReleasesLockSuccessfully()
    {
        await RegisterServicesAsync();

        var lockKey = NodeLockKey.Create("testlock1");
        var redisKey = "odin:lock:" + lockKey;

        var connectionMultiplexer = _services!.Resolve<IConnectionMultiplexer>();
        var db = connectionMultiplexer.GetDatabase();
        var redisLock = _services!.Resolve<INodeLock>();

        await using (await redisLock.LockAsync(lockKey))
        {
            var exists = await db.KeyExistsAsync(redisKey);
            Assert.That(exists, Is.True, "Lock key should exist in Redis after acquiring the lock.");
        }

        // Allow a short time for the Lua script to execute.
        await Task.Delay(50);

        {
            var exists = await db.KeyExistsAsync(redisKey);
            Assert.That(exists, Is.False, "Lock key should be removed from Redis after releasing the lock.");
        }
    }

    //

    [Test]
    public async Task LockAsync_ThrowsTimeoutException_WhenLockIsAlreadyHeld()
    {
        await RegisterServicesAsync();

        var lockKey = NodeLockKey.Create("testlock1");
        var redisLock = _services!.Resolve<INodeLock>();

        // Acquire the lock and hold it.
        await using (await redisLock.LockAsync(lockKey, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)))
        {
            var ex = Assert.ThrowsAsync<RedisLockException>(async () =>
            {
                await redisLock.LockAsync(lockKey, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
            });
            Assert.That(ex, Is.Not.Null, "A RedisLockException should be thrown when lock acquisition times out.");
        }

        // Allow a short time for the Lua script to execute.
        await Task.Delay(50);

        await using (await redisLock.LockAsync(lockKey, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10)))
        {
            Assert.Pass();
        }
    }

    //

    [Test]
    public async Task LockAsync_AllowsSequentialLocking()
    {
        await RegisterServicesAsync();

        var lockKey = NodeLockKey.Create("testlock1");
        var redisKey = "odin:lock:" + lockKey;

        var connectionMultiplexer = _services!.Resolve<IConnectionMultiplexer>();
        var db = connectionMultiplexer.GetDatabase();
        var redisLock = _services!.Resolve<INodeLock>();

        // Acquire and release the lock sequentially several times.
        for (var i = 0; i < 3; i++)
        {
            await using (var lockHandle = await redisLock.LockAsync(lockKey))
            {
                bool exists = await db.KeyExistsAsync(redisKey);
                Assert.That(exists, Is.True, "Lock key should exist while the lock is held.");
            }
            // After releasing, ensure the key is removed.
            bool existsAfter = await db.KeyExistsAsync(redisKey);
            Assert.That(existsAfter, Is.False, "Lock key should be removed after releasing the lock.");
        }
    }

    //

    [Test]
    public async Task LockAsync_CancellationToken_CancelsTask()
    {
        await RegisterServicesAsync();

        var lockKey = NodeLockKey.Create("testlock1");
        var redisLock = _services!.Resolve<INodeLock>();

        // Acquire the lock so that the next attempt will have to wait.
        await using (await redisLock.LockAsync(lockKey, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)))
        {
            // Set up a cancellation token that cancels very quickly.
            using (var cts = new CancellationTokenSource(50))
            {
                // Expect a TaskCanceledException (or OperationCanceledException) due to cancellation.
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                {
                    await redisLock.LockAsync(lockKey, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), cts.Token);
                });
            }
        }
    }

    //

    [Test]
    public async Task LockAsync_ThrowsOnBadTimeoutParams()
    {
        await RegisterServicesAsync();

        var lockKey = NodeLockKey.Create("testlock1");
        var redisLock = _services!.Resolve<INodeLock>();

        var ex = Assert.ThrowsAsync<RedisLockException>(async () =>
            await redisLock.LockAsync(
                lockKey,
                TimeSpan.FromSeconds(0),
                TimeSpan.FromSeconds(1),
                CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo("timeout must be greater than zero"));

        ex = Assert.ThrowsAsync<RedisLockException>(async () =>
            await redisLock.LockAsync(
                lockKey,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(0),
                CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo("forcedRelease must be greater than zero"));

        ex = Assert.ThrowsAsync<RedisLockException>(async () =>
            await redisLock.LockAsync(
                lockKey,
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(10),
                CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo($"timeout must be less than forcedRelease"));

    }


}

#endif