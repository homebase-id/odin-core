using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.PubSub;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Core.Storage.Tests.PubSub;

#nullable enable

public class InProcPubSubTests
{
    [Test]
    public async Task SenderShouldReceiveItsOwnMessages()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);

        var pubSub1MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(200);

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel\""));

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(200);

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));

        pubSub1.Dispose();

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));
    }

    //

    [Test]
    public async Task SenderShouldReceiveItsOwnMessagesAdditionalSubscriber()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);
        var pubSub2 = new InProcPubSub(broker, channelPrefix);

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

        await Task.Delay(200); // Give some time for subscriptions to be set up

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel\""));

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(200); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));

        pubSub1.Dispose();
        pubSub2.Dispose();

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));
    }

    //

    [Test]
    public async Task ItShouldSendAndReceiveOnMultipleChannels()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel1 = "test-channel1";
        const string testChannel2 = "test-channel2";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);
        var pubSub2 = new InProcPubSub(broker, channelPrefix);

        var pubSub1Channel1MessageReceived = "";
        var pubSub2Channel1MessageReceived = "";
        var pubSub1Channel2MessageReceived = "";
        var pubSub2Channel2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel1, async message =>
        {
            pubSub1Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel1, async message =>
        {
            pubSub2Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub1.SubscribeAsync<string>(testChannel2, async message =>
        {
            pubSub1Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel2, async message =>
        {
            pubSub2Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(200); // Give some time for subscriptions to be set up

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel1\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel2\""));

        await pubSub1.PublishAsync(testChannel1, "Hello");
        await pubSub2.PublishAsync(testChannel2, "There");

        await Task.Delay(200); // Give some time for messages to be processed

        Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo("There"));
        Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));

        pubSub1.Dispose();
        pubSub2.Dispose();

        await Task.Delay(200); // Give some time for messages to be processed

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel1\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel2\""));
    }

    //

    [Test]
    public async Task ItShouldUnsubscribe()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);
        var pubSub2 = new InProcPubSub(broker, channelPrefix);

        var pubSub1MessageReceived1 = "";
        var pubSub1MessageReceived2 = "";
        var pubSub2MessageReceived1 = "";
        var pubSub2MessageReceived2 = "";

        var unsubscribeToken11 = await pubSub1.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub1MessageReceived1 = message;
            await Task.CompletedTask;
        });

        var unsubscribeToken12 = await pubSub1.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub1MessageReceived2 = message;
            await Task.CompletedTask;
        });

        var unsubscribeToken21 = await pubSub2.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub2MessageReceived1 = message;
            await Task.CompletedTask;
        });

        var unsubscribeToken22 = await pubSub2.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub2MessageReceived2 = message;
            await Task.CompletedTask;
        });

        await Task.Delay(200); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(200); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived1, Is.EqualTo("Hello"));
        Assert.That(pubSub1MessageReceived2, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived1, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived2, Is.EqualTo("Hello"));

        pubSub1MessageReceived1 = "";
        pubSub1MessageReceived2 = "";
        pubSub2MessageReceived1 = "";
        pubSub2MessageReceived2 = "";

        await pubSub2.UnsubscribeAsync(testChannel, unsubscribeToken21);

        await Task.Delay(200); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello again");

        await Task.Delay(200); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived1, Is.EqualTo("Hello again"));
        Assert.That(pubSub1MessageReceived2, Is.EqualTo("Hello again"));
        Assert.That(pubSub2MessageReceived1, Is.EqualTo(""));
        Assert.That(pubSub2MessageReceived2, Is.EqualTo("Hello again"));

        pubSub1MessageReceived1 = "";
        pubSub1MessageReceived2 = "";
        pubSub2MessageReceived1 = "";
        pubSub2MessageReceived2 = "";

        await pubSub2.UnsubscribeAsync(testChannel, unsubscribeToken22);

        await Task.Delay(200); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello again again");

        await Task.Delay(200); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived1, Is.EqualTo("Hello again again"));
        Assert.That(pubSub1MessageReceived2, Is.EqualTo("Hello again again"));
        Assert.That(pubSub2MessageReceived1, Is.EqualTo(""));
        Assert.That(pubSub2MessageReceived2, Is.EqualTo(""));

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Not.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));

        await pubSub1.UnsubscribeAsync(testChannel, unsubscribeToken11);
        await pubSub1.UnsubscribeAsync(testChannel, unsubscribeToken12);

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));

        logStore.Clear();

        unsubscribeToken21 = await pubSub2.SubscribeAsync<string>(testChannel, async message =>
        {
            pubSub2MessageReceived1 = message;
            await Task.CompletedTask;
        });

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Not.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(200);

        Assert.That(pubSub2MessageReceived1, Is.EqualTo("Hello"));

        await pubSub2.UnsubscribeAsync(testChannel, unsubscribeToken21);

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));

        pubSub1.Dispose();
        pubSub2.Dispose();

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));
    }

    //

    [Test]
    public async Task ItShouldUnsubscribeAll()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel1 = "test-channel1";
        const string testChannel2 = "test-channel2";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);
        var pubSub2 = new InProcPubSub(broker, channelPrefix);

        var pubSub1Channel1MessageReceived = "";
        var pubSub2Channel1MessageReceived = "";
        var pubSub1Channel2MessageReceived = "";
        var pubSub2Channel2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel1, async message =>
        {
            pubSub1Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel1, async message =>
        {
            pubSub2Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub1.SubscribeAsync<string>(testChannel2, async message =>
        {
            pubSub1Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel2, async message =>
        {
            pubSub2Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(200);

        await pubSub1.PublishAsync(testChannel1, "Hello");
        await pubSub2.PublishAsync(testChannel2, "There");

        await Task.Delay(200); // Give some time for messages to be processed

        Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo("There"));
        Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel1\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel2\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Not.Contain("Stopped processing messages for channel \"my-prefix:test-channel1\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Not.Contain("Stopped processing messages for channel \"my-prefix:test-channel2\""));

        logStore.Clear();
        await pubSub1.UnsubscribeAllAsync();

        await Task.Delay(200);

        pubSub1Channel1MessageReceived = "";
        pubSub2Channel1MessageReceived = "";
        pubSub1Channel2MessageReceived = "";
        pubSub2Channel2MessageReceived = "";

        await pubSub1.PublishAsync(testChannel1, "Hello");
        await pubSub2.PublishAsync(testChannel2, "There");

        await Task.Delay(200);

        Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Not.Contain("Stopped processing messages for channel \"my-prefix:test-channel1\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Not.Contain("Stopped processing messages for channel \"my-prefix:test-channel2\""));

        logStore.Clear();
        await pubSub2.UnsubscribeAllAsync();

        await Task.Delay(200);

        pubSub1Channel1MessageReceived = "";
        pubSub2Channel1MessageReceived = "";
        pubSub1Channel2MessageReceived = "";
        pubSub2Channel2MessageReceived = "";

        await pubSub1.PublishAsync(testChannel1, "Hello");
        await pubSub2.PublishAsync(testChannel2, "There");

        await Task.Delay(200);

        Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo(""));

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel1\""));
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel2\""));
    }

    //

    [Test]
    public async Task HandlerErrorsShouldNotBlowUp()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);

        await pubSub1.SubscribeAsync<string>(testChannel,  _ => throw new Exception("oh no"));

        await Task.Delay(200);

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel\""));

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Error], Does.Contain("Handler failed: \"oh no\""));

        pubSub1.Dispose();

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));
    }

    //

    [Test]
    public async Task ItShouldHandleSendingAndReceivingDifferentTypes()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);

        var pubSub1StringReceived = "";
        var pubSub1IntReceived = 0;

        await pubSub1.SubscribeAsync<string?>(testChannel, async (string? message) =>
        {
            pubSub1StringReceived = message;
            await Task.CompletedTask;
        });

        await pubSub1.SubscribeAsync<int>(testChannel, async (int number) =>
        {
            pubSub1IntReceived = number;
            await Task.CompletedTask;
        });

        await Task.Delay(200);

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel\""));

        await pubSub1.PublishAsync(testChannel, "Hello");
        await pubSub1.PublishAsync(testChannel, 123);

        await Task.Delay(200);

        Assert.That(pubSub1StringReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub1IntReceived, Is.EqualTo(123));

        pubSub1.Dispose();

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));
    }

    //

    [Test]
    public async Task ItShouldSendAndReceiveNullValues()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);

        var pubSub1StringReceived = "";

        await pubSub1.SubscribeAsync<string?>(testChannel, async message =>
        {
            pubSub1StringReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(200);

        var logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Started processing messages for channel \"my-prefix:test-channel\""));

        await pubSub1.PublishAsync<string?>(testChannel, null);

        await Task.Delay(200);

        Assert.That(pubSub1StringReceived, Is.Null);

        pubSub1.Dispose();

        await Task.Delay(200);

        logEvents = logStore.GetLogMessages();
        Assert.That(logEvents[LogEventLevel.Debug], Does.Contain("Stopped processing messages for channel \"my-prefix:test-channel\""));
    }





}

