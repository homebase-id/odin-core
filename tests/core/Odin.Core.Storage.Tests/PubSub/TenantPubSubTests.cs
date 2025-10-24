using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.PubSub;
using Odin.Core.Tasks;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Odin.Core.Storage.Tests.PubSub;

#nullable enable

public class TenantPubSubTests
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
        builder.AddTenantPubSub("frodo.me", redis);

        return builder.Build();
    }


    [Test]
    [TestCase(PubSubType.InProc)]
#if RUN_REDIS_TESTS
    [TestCase(PubSubType.Redis)]
#endif
    public async Task TenantDoesReceiveMessagesFromSelf(PubSubType pubSubType)
    {
        const string testChannel = "test-channel";

        var services = RegisterServicesAsync(pubSubType);

        var tenantPubSub1 = services.Resolve<ITenantPubSub>();
        var tenantPubSub2 = services.Resolve<ITenantPubSub>();

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await tenantPubSub1.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await tenantPubSub2.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await tenantPubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));

        services.Dispose();
    }

    //

}