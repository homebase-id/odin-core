#nullable enable
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Tests.AppNotifications.Push;

public class PushNotificationOutboxRecordExpiryTests
{
    private static readonly UnixTimeUtc Enqueued = new(1_757_000_000_000);

    private static PushNotificationOutboxRecord Record(int? ttlSeconds) => new()
    {
        Timestamp = Enqueued,
        Options = new AppNotificationOptions { TimeToLiveSeconds = ttlSeconds },
    };

    [Test]
    public void NoTtl_NeverExpires()
    {
        var record = Record(null);
        Assert.That(record.IsExpired(Enqueued.AddSeconds(10_000_000)), Is.False, "a push without TTL has no deadline");
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void NonPositiveTtl_NeverExpires(int ttl)
    {
        var record = Record(ttl);
        Assert.That(record.IsExpired(Enqueued.AddSeconds(10_000_000)), Is.False, $"ttl {ttl} must be treated as unset");
    }

    [Test]
    public void NullOptions_NeverExpires()
    {
        var record = new PushNotificationOutboxRecord { Timestamp = Enqueued, Options = null! };
        Assert.That(record.IsExpired(Enqueued.AddSeconds(10_000_000)), Is.False);
    }

    [Test]
    public void BeforeTheDeadline_IsNotExpired()
    {
        var record = Record(60);
        Assert.That(record.IsExpired(Enqueued.AddSeconds(59)), Is.False, "59s into a 60s TTL is still live");
    }

    [Test]
    public void AtTheDeadline_IsNotExpired()
    {
        var record = Record(60);
        Assert.That(record.IsExpired(Enqueued.AddSeconds(60)), Is.False, "the deadline itself is the last live instant");
    }

    [Test]
    public void AfterTheDeadline_IsExpired()
    {
        var record = Record(60);
        Assert.That(record.IsExpired(Enqueued.AddSeconds(61)), Is.True, "61s into a 60s TTL is stale");
    }
}
