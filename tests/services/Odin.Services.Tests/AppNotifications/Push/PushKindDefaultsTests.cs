#nullable enable
using System;
using NUnit.Framework;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Tests.AppNotifications.Push;

public class PushKindDefaultsTests
{
    [Test]
    public void Ring_DerivesCallDefaults()
    {
        var tagId = Guid.NewGuid();
        var options = new AppNotificationOptions { Kind = PushKind.Ring, TagId = tagId };

        PushKindDefaults.Apply(options);

        Assert.That(options.TimeToLiveSeconds, Is.EqualTo(PushKindDefaults.RingTimeToLiveSeconds));
        Assert.That(options.CollapseId, Is.EqualTo($"call-{tagId:N}"));
        Assert.That(options.TimeSensitive, Is.True);
        Assert.That(options.Silent, Is.False);
    }

    [Test]
    public void Ring_KeepsExplicitOverrides()
    {
        var options = new AppNotificationOptions
        {
            Kind = PushKind.Ring, TagId = Guid.NewGuid(), TimeToLiveSeconds = 20, CollapseId = "mine",
        };

        PushKindDefaults.Apply(options);

        Assert.That(options.TimeToLiveSeconds, Is.EqualTo(20), "an explicit TTL wins");
        Assert.That(options.CollapseId, Is.EqualTo("mine"), "an explicit collapse id wins");
    }

    [Test]
    public void Ring_WithoutTagId_HasNoCollapseId()
    {
        var options = new AppNotificationOptions { Kind = PushKind.Ring };

        PushKindDefaults.Apply(options);

        Assert.That(options.CollapseId, Is.Null, "nothing to derive a collapse id from");
        Assert.That(options.TimeToLiveSeconds, Is.EqualTo(PushKindDefaults.RingTimeToLiveSeconds));
    }

    [Test]
    public void Wake_IsSilent()
    {
        var options = new AppNotificationOptions { Kind = PushKind.Wake };

        PushKindDefaults.Apply(options);

        Assert.That(options.Silent, Is.True);
        Assert.That(options.TimeToLiveSeconds, Is.Null, "a wake-up derives no TTL");
        Assert.That(options.TimeSensitive, Is.False);
    }

    [Test]
    public void Notify_ChangesNothing()
    {
        var options = new AppNotificationOptions { TagId = Guid.NewGuid() };

        PushKindDefaults.Apply(options);

        Assert.That(options.Kind, Is.EqualTo(PushKind.Notify), "the default kind is Notify");
        Assert.That(options.TimeToLiveSeconds, Is.Null);
        Assert.That(options.CollapseId, Is.Null);
        Assert.That(options.TimeSensitive, Is.False);
        Assert.That(options.Silent, Is.False);
    }
}
