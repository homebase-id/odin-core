#nullable enable
using System;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Dto;
using Odin.Core.Serialization;
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

    [TestCase(PushKind.Hangup)]
    [TestCase(PushKind.Handled)]
    public void CallEnd_DerivesTheSameCollapseIdAsTheRingItEnds(PushKind kind)
    {
        var callId = Guid.NewGuid();
        var ring = new AppNotificationOptions { Kind = PushKind.Ring, TagId = callId };
        var hangup = new AppNotificationOptions { Kind = kind, TagId = callId };

        PushKindDefaults.Apply(ring);
        PushKindDefaults.Apply(hangup);

        Assert.That(hangup.CollapseId, Is.EqualTo(ring.CollapseId), "the hangup must replace the ring on the device");
        Assert.That(hangup.TimeToLiveSeconds, Is.EqualTo(ring.TimeToLiveSeconds), "a stale hangup must not arrive on its own");
        Assert.That(hangup.Silent, Is.False, "an alert can only be replaced by an alert; the relay silences a Handled to a CallKit device");
        Assert.That(hangup.TimeSensitive, Is.False, "the call is over; nothing to interrupt for");
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

    [TestCase("ring", PushKind.Ring)]
    [TestCase("hangup", PushKind.Hangup)]
    [TestCase("handled", PushKind.Handled)]
    public void AppWireKind_Deserializes(string wire, PushKind expected)
    {
        var options = OdinSystemSerializer.Deserialize<AppNotificationOptions>($$"""{"kind":"{{wire}}"}""")!;

        Assert.That(options.Kind, Is.EqualTo(expected));
    }

    [Test]
    public void UnknownWireKind_IsRefused()
    {
        // So a host that predates a kind answers 400 to the whole request rather than sending it as Notify.
        Assert.Throws<JsonException>(() => OdinSystemSerializer.Deserialize<AppNotificationOptions>("""{"kind":"later"}"""));
    }
}
