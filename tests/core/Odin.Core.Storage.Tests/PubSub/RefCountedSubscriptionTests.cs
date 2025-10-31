using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Json;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.PubSub;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.PubSub;

public class RefCountedSubscriptionTests
{
    [Test]
    public async Task IsShouldSubscribeAndUnsubscribe()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSubBroker>(logStore);

        var broker = new InProcPubSubBroker(logger);
        var pubSub1 = new InProcPubSub(broker, channelPrefix);

        var pubSub1MessageCount = 0;

        var refCountedSubscription = new RefCountedSubscription(pubSub1, testChannel, async envelope =>
        {
            ++pubSub1MessageCount;
            await Task.CompletedTask;
        });

        Assert.That(refCountedSubscription.SubscriptionCount, Is.EqualTo(0));
        Assert.That(refCountedSubscription.IsSubscribed, Is.False);

        await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello"));
        await Task.Delay(200);
        Assert.That(pubSub1MessageCount, Is.EqualTo(0));

        await refCountedSubscription.SubscribeAsync();

        Assert.That(refCountedSubscription.SubscriptionCount, Is.EqualTo(1));
        Assert.That(refCountedSubscription.IsSubscribed, Is.True);

        await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello"));
        await Task.Delay(200);
        Assert.That(pubSub1MessageCount, Is.EqualTo(1));

        await refCountedSubscription.SubscribeAsync();

        Assert.That(refCountedSubscription.SubscriptionCount, Is.EqualTo(2));
        Assert.That(refCountedSubscription.IsSubscribed, Is.True);

        await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello"));
        await Task.Delay(200);
        Assert.That(pubSub1MessageCount, Is.EqualTo(2));

        await refCountedSubscription.UnsubscribeAsync();

        Assert.That(refCountedSubscription.SubscriptionCount, Is.EqualTo(1));
        Assert.That(refCountedSubscription.IsSubscribed, Is.True);

        await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello"));
        await Task.Delay(200);
        Assert.That(pubSub1MessageCount, Is.EqualTo(3));

        await refCountedSubscription.UnsubscribeAsync();

        Assert.That(refCountedSubscription.SubscriptionCount, Is.EqualTo(0));
        Assert.That(refCountedSubscription.IsSubscribed, Is.False);

        await pubSub1.PublishAsync(testChannel, JsonEnvelope.Create("Hello"));
        await Task.Delay(200);
        Assert.That(pubSub1MessageCount, Is.EqualTo(3));
    }
}