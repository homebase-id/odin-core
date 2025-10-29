using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Json;
using Odin.Core.Storage.PubSub;
using Odin.Core.Tasks;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Odin.Core.Storage.Tests.PubSub;

#nullable enable

public class SystemPubSubTests
{
    public enum PubSubType
    {
        InProc,
        Redis
    }

    private RedisContainer? _redisContainer;

    [SetUp]
    public void Setup()
    {
#if RUN_REDIS_TESTS
        _redisContainer = new RedisBuilder()
            .WithImage("redis:latest")
            .Build();
        _redisContainer!.StartAsync().BlockingWait();
#endif
    }

    //

    [TearDown]
    public void TearDown()
    {
        if (_redisContainer != null)
        {
            _redisContainer.StopAsync().BlockingWait();
            _redisContainer.DisposeAsync().AsTask().BlockingWait();
            _redisContainer = null;
        }
    }

    //

    private ILifetimeScope RegisterServicesAsync(PubSubType pubSubType)
    {
        var redis = pubSubType == PubSubType.Redis;
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        if (redis)
        {
            var redisConfig = _redisContainer?.GetConnectionString() ?? throw new InvalidOperationException();
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));
        }

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.AddSystemPubSub(redis);

        return builder.Build();
    }


    [Test]
    [TestCase(PubSubType.InProc)]
#if RUN_REDIS_TESTS
    [TestCase(PubSubType.Redis)]
#endif
    public async Task SystemDoesReceiveMessagesFromSelf(PubSubType pubSubType)
    {
        const string testChannel = "test-channel";

        var services = RegisterServicesAsync(pubSubType);

        var systemPubSub = services.Resolve<ISystemPubSub>();

        var systemReceived = false;

        await systemPubSub.SubscribeAsync(testChannel, async message =>
        {
            systemReceived = true;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await systemPubSub.PublishAsync(testChannel, JsonEnvelope.Create("Hello from System!"));

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(systemReceived, Is.True);

        services.Dispose();
    }

}

