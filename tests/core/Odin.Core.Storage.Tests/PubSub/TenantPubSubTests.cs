using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.PubSub;
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
        _redisContainer!.StartAsync().Wait();
#endif
    }

    //

    [TearDown]
    public async Task TearDown()
    {
#if RUN_REDIS_TESTS
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
            _redisContainer = null;
        }
#endif
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

        var services1 = RegisterServicesAsync(pubSubType);
        var services2 = RegisterServicesAsync(pubSubType);

        var tenantPubSub1 = services1.Resolve<ITenantPubSub>();
        var tenantPubSub2 = services2.Resolve<ITenantPubSub>();

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await tenantPubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await tenantPubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await tenantPubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));

        services1.Dispose();
        services2.Dispose();
    }

    //

    [Test]
    [TestCase(PubSubType.InProc)]
#if RUN_REDIS_TESTS
    [TestCase(PubSubType.Redis)]
#endif
    public async Task TenantDoesntReceiveMessagesFromSelf(PubSubType pubSubType)
    {
        const string testChannel = "test-channel";

        var services1 = RegisterServicesAsync(pubSubType);
        var services2 = RegisterServicesAsync(pubSubType);

        var tenantPubSub1 = services1.Resolve<ITenantPubSub>();
        var tenantPubSub2 = services2.Resolve<ITenantPubSub>();

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await tenantPubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await tenantPubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await tenantPubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));

        services1.Dispose();
        services2.Dispose();
    }

    //

}