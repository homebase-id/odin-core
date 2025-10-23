using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.PubSub;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.PubSub;

#nullable enable

public class InProcPubSubTests
{
    [Test]
    public async Task SenderShouldReceiveItsOwnMessages()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

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

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

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

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

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

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

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

    //

}

