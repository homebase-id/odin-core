using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Storage.PubSub;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Odin.Core.Storage.Tests.PubSub;

#nullable enable

#if RUN_REDIS_TESTS

public class RedisPubSubTests
{
    private RedisContainer? _redisContainer;

    [SetUp]
    public void Setup()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:latest")
            .Build();
        _redisContainer!.StartAsync().Wait();
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
    }

    //

    private ILifetimeScope RegisterServicesAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        var redisConfig = _redisContainer?.GetConnectionString() ?? throw new InvalidOperationException();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.AddSystemPubSub(true);
        builder.AddTenantPubSub(true, "frodo.me");

        return builder.Build();
    }

    //

    [Test]
    public async Task SenderShouldNotReceiveItsOwnMessages()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = new Mock<ILogger<RedisPubSub>>().Object;
        var redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());

        var pubSub1 = new RedisPubSub(logger, redis, channelPrefix);
        var pubSub2 = new RedisPubSub(logger, redis, channelPrefix);

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));
    }

    //

    [Test]
    public async Task SystemDoesntReceiveMessagesFromSelf()
    {
        const string testChannel = "test-channel";

        var services = RegisterServicesAsync();

        var systemPubSub = services.Resolve<ISystemPubSub>();

        var systemReceived = false;

        await systemPubSub.SubscribeAsync<string>(testChannel, async message =>
        {
            systemReceived = true;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await systemPubSub.PublishAsync(testChannel, "Hello from System!");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(systemReceived, Is.False);

        services.Dispose();
    }

    //

    [Test]
    public async Task TenantDoesntReceiveMessagesFromSelf()
    {
        const string testChannel = "test-channel";

        var services1 = RegisterServicesAsync();
        var services2 = RegisterServicesAsync();

        var tenantPubSub1 = services1.Resolve<ITenantPubSub>();
        var tenantPubSub2 = services2.Resolve<ITenantPubSub>();

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

        Assert.That(pubSub1MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));

        services1.Dispose();
        services2.Dispose();
    }

    //

}

#endif