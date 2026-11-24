using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Tests.AppNotifications;

/// <summary>What a web push subscription is sent (#2006): an app's, its own payloads; the owner console's, everything.</summary>
public class WebPushContentForTests
{
    private static readonly Guid Journal = Guid.NewGuid();
    private static readonly Guid Chat = Guid.NewGuid();

    private static PushNotificationContent Content(params Guid[] appIds) => new()
    {
        Payloads = appIds.Select(id => new PushNotificationPayload { Options = new AppNotificationOptions { AppId = id } }).ToList()
    };

    [Test]
    public void AnAppsSubscription_GetsOnlyItsOwnPayloads()
    {
        var sent = PushNotificationService.WebPushContentFor(new PushNotificationSubscription { AppId = Journal }, Content(Chat, Journal));
        Assert.That(sent!.Payloads.Select(p => p.Options.AppId), Is.EqualTo(new List<Guid> { Journal }));
    }

    [Test]
    public void AnAppsSubscription_IsSentNothing_WhenNoPayloadIsItsOwn()
    {
        Assert.That(PushNotificationService.WebPushContentFor(new PushNotificationSubscription { AppId = Journal }, Content(Chat)), Is.Null);
    }

    [Test]
    public void TheOwnerConsolesSubscription_AndAnOlderOne_GetEverything()
    {
        var content = Content(Chat, Journal);
        Assert.That(PushNotificationService.WebPushContentFor(new PushNotificationSubscription { AppId = null }, content), Is.SameAs(content));
    }
}
