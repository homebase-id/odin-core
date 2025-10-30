using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Json;
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

        await pubSub1.SubscribeAsync(testChannel, async message =>
        {
            pubSub1MessageReceived = message.DeserializeMessage<string>();
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync(testChannel, async message =>
        {
            pubSub2MessageReceived = message.DeserializeMessage<string>();
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello"));

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

         await pubSub1.SubscribeAsync(testChannel1, async message =>
         {
             pubSub1Channel1MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await pubSub2.SubscribeAsync(testChannel1, async message =>
         {
             pubSub2Channel1MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await pubSub1.SubscribeAsync(testChannel2, async message =>
         {
             pubSub1Channel2MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await pubSub2.SubscribeAsync(testChannel2, async message =>
         {
             pubSub2Channel2MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await Task.Delay(500); // Give some time for subscriptions to be set up

         await pubSub1.PublishAsync(testChannel1, JsonEnvelope.Create("Hello"));
         await pubSub2.PublishAsync(testChannel2, JsonEnvelope.Create("There"));

         await Task.Delay(500); // Give some time for messages to be processed

         Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo("Hello"));
         Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
         Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo("There"));
         Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));
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

         var pubSub1MessageReceived1 = "";
         var pubSub1MessageReceived2 = "";
         var pubSub2MessageReceived1 = "";
         var pubSub2MessageReceived2 = "";

         await pubSub1.SubscribeAsync(testChannel, async message =>
         {
             pubSub1MessageReceived1 = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await pubSub1.SubscribeAsync(testChannel, async message =>
         {
             pubSub1MessageReceived2 = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         var subscription1 = await pubSub2.SubscribeAsync(testChannel, async message =>
         {
             pubSub2MessageReceived1 = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         var subscription2 = await pubSub2.SubscribeAsync(testChannel, async message =>
         {
             pubSub2MessageReceived2 = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await Task.Delay(500); // Give some time for subscriptions to be set up

         await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello"));

         await Task.Delay(500); // Give some time for messages to be processed

         Assert.That(pubSub1MessageReceived1, Is.EqualTo("Hello"));
         Assert.That(pubSub1MessageReceived2, Is.EqualTo("Hello"));
         Assert.That(pubSub2MessageReceived1, Is.EqualTo("Hello"));
         Assert.That(pubSub2MessageReceived2, Is.EqualTo("Hello"));

         pubSub1MessageReceived1 = "";
         pubSub1MessageReceived2 = "";
         pubSub2MessageReceived1 = "";
         pubSub2MessageReceived2 = "";

         await subscription1.UnsubscribeAsync();

         await Task.Delay(500); // Give some time for subscriptions to be set up

         await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello again"));

         await Task.Delay(500); // Give some time for messages to be processed

         Assert.That(pubSub1MessageReceived1, Is.EqualTo("Hello again"));
         Assert.That(pubSub1MessageReceived2, Is.EqualTo("Hello again"));
         Assert.That(pubSub2MessageReceived1, Is.EqualTo(""));
         Assert.That(pubSub2MessageReceived2, Is.EqualTo("Hello again"));

         pubSub1MessageReceived1 = "";
         pubSub1MessageReceived2 = "";
         pubSub2MessageReceived1 = "";
         pubSub2MessageReceived2 = "";

         await subscription2.UnsubscribeAsync();

         await Task.Delay(500); // Give some time for subscriptions to be set up

         await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello again again"));

         await Task.Delay(500); // Give some time for messages to be processed

         Assert.That(pubSub1MessageReceived1, Is.EqualTo("Hello again again"));
         Assert.That(pubSub1MessageReceived2, Is.EqualTo("Hello again again"));
         Assert.That(pubSub2MessageReceived1, Is.EqualTo(""));
         Assert.That(pubSub2MessageReceived2, Is.EqualTo(""));
     }

     //

     [Test]
     public async Task ItShouldUnsubscribeAll()
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

         await pubSub1.SubscribeAsync(testChannel1, async message =>
         {
             pubSub1Channel1MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await pubSub2.SubscribeAsync(testChannel1, async message =>
         {
             pubSub2Channel1MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await pubSub1.SubscribeAsync(testChannel2, async message =>
         {
             pubSub1Channel2MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await pubSub2.SubscribeAsync(testChannel2, async message =>
         {
             pubSub2Channel2MessageReceived = message.DeserializeMessage<string>();
             await Task.CompletedTask;
         });

         await Task.Delay(500);

         await pubSub1.PublishAsync(testChannel1, JsonEnvelope.Create("Hello"));
         await pubSub2.PublishAsync(testChannel2, JsonEnvelope.Create("There"));

         await Task.Delay(500); // Give some time for messages to be processed

         Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo("Hello"));
         Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
         Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo("There"));
         Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));

         await pubSub1.UnsubscribeAllAsync();

         await Task.Delay(500);

         pubSub1Channel1MessageReceived = "";
         pubSub2Channel1MessageReceived = "";
         pubSub1Channel2MessageReceived = "";
         pubSub2Channel2MessageReceived = "";

         await pubSub1.PublishAsync(testChannel1, JsonEnvelope.Create("Hello"));
         await pubSub2.PublishAsync(testChannel2, JsonEnvelope.Create("There"));

         await Task.Delay(500);

         Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo(""));
         Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
         Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo(""));
         Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));

         await pubSub2.UnsubscribeAllAsync();

         await Task.Delay(500);

         pubSub1Channel1MessageReceived = "";
         pubSub2Channel1MessageReceived = "";
         pubSub1Channel2MessageReceived = "";
         pubSub2Channel2MessageReceived = "";

         await pubSub1.PublishAsync(testChannel1, JsonEnvelope.Create("Hello"));
         await pubSub2.PublishAsync(testChannel2, JsonEnvelope.Create("There"));

         await Task.Delay(500);

         Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo(""));
         Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo(""));
         Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo(""));
         Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo(""));
     }

     //

     [Test]
     public async Task ItShouldSendAndReceiveNullValues()
     {
         const string channelPrefix = "my-prefix";
         const string testChannel = "test-channel";

         var logger = new Mock<ILogger<RedisPubSub>>().Object;
         var redis1 = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());

         var pubSub1 = new RedisPubSub(logger, redis1, channelPrefix);

         var pubSub1MessageReceived = "";

         await pubSub1.SubscribeAsync(testChannel, async message =>
         {
             pubSub1MessageReceived = message.DeserializeMessage<string?>();
             await Task.CompletedTask;
         });

         await Task.Delay(500); // Give some time for subscriptions to be set up

         await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create<string?>(null));

         await Task.Delay(500); // Give some time for messages to be processed

         Assert.That(pubSub1MessageReceived, Is.Null);
     }



}

#endif