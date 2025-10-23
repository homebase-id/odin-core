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

    [Test]
    public async Task SenderShouldReceiveItsOwnMessages()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = new Mock<ILogger<RedisPubSub>>().Object;
        var redis1 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());
        var redis2 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());

        var pubSub1 = new RedisPubSub(logger, redis1, channelPrefix);
        var pubSub2 = new RedisPubSub(logger, redis2, channelPrefix);

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));
    }

    //

    [Test]
    public async Task ItShouldSendAndReceiveOnMultipleChannels()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel1 = "test-channel1";
        const string testChannel2 = "test-channel2";

        var logger = new Mock<ILogger<RedisPubSub>>().Object;
        var redis1 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());
        var redis2 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());

        var pubSub1 = new RedisPubSub(logger, redis1, channelPrefix);
        var pubSub2 = new RedisPubSub(logger, redis2, channelPrefix);

        var pubSub1Channel1MessageReceived = "";
        var pubSub2Channel1MessageReceived = "";
        var pubSub1Channel2MessageReceived = "";
        var pubSub2Channel2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel1, MessageFromSelf.Process, async message =>
        {
            pubSub1Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel1, MessageFromSelf.Process, async message =>
        {
            pubSub2Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub1.SubscribeAsync<string>(testChannel2, MessageFromSelf.Process, async message =>
        {
            pubSub1Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel2, MessageFromSelf.Process, async message =>
        {
            pubSub2Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel1, "Hello");
        await pubSub2.PublishAsync(testChannel2, "There");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo("There"));
        Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));
    }

    //

    [Test]
    public async Task SenderShouldNotReceiveItsOwnMessages()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = new Mock<ILogger<RedisPubSub>>().Object;
        var redis1 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());
        var redis2 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());

        var pubSub1 = new RedisPubSub(logger, redis1, channelPrefix);
        var pubSub2 = new RedisPubSub(logger, redis2, channelPrefix);

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
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
    public async Task ItShouldUnsubscribe()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = new Mock<ILogger<RedisPubSub>>().Object;
        var redis1 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());
        var redis2 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());

        var pubSub1 = new RedisPubSub(logger, redis1, channelPrefix);
        var pubSub2 = new RedisPubSub(logger, redis2, channelPrefix);

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        var unsubscribeToken = await pubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));

        pubSub1MessageReceived = "";
        pubSub2MessageReceived = "";

        await pubSub2.UnsubscribeAsync(testChannel, unsubscribeToken);

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello again");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello again"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo(""));

    }

}

#endif