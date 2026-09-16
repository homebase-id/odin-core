using System;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Tests.Peer;

public class OutboxRetryLaterTests
{
    private static readonly UnixTimeUtc Now = new(1_757_000_000_000);

    [Test]
    public void HonoursARetryAfterInsideTheRange()
    {
        var next = OutboxRetryLater.NextRun(TimeSpan.FromHours(1), Now);
        Assert.That(next.milliseconds, Is.EqualTo(Now.milliseconds + 3600 * 1000));
    }

    [Test]
    public void RaisesATinyRetryAfterToTheFloor()
    {
        // A recipient asking us back in a second would otherwise become a resend loop
        var next = OutboxRetryLater.NextRun(TimeSpan.FromSeconds(1), Now);
        Assert.That(next.milliseconds, Is.EqualTo(Now.milliseconds + (long)OutboxRetryLater.MinDelay.TotalMilliseconds));
    }

    [Test]
    public void TreatsANegativeRetryAfterAsTheFloor()
    {
        var next = OutboxRetryLater.NextRun(TimeSpan.FromSeconds(-30), Now);
        Assert.That(next.milliseconds, Is.EqualTo(Now.milliseconds + (long)OutboxRetryLater.MinDelay.TotalMilliseconds));
    }

    [Test]
    public void CapsALongRetryAfterAtTheCeiling()
    {
        var next = OutboxRetryLater.NextRun(TimeSpan.FromDays(2), Now);
        Assert.That(next.milliseconds, Is.EqualTo(Now.milliseconds + (long)OutboxRetryLater.MaxDelay.TotalMilliseconds));
    }

    [Test]
    public void PausedAndOutOfQuotaDelaysAreBothHonouredAsSent()
    {
        var paused = OutboxRetryLater.NextRun(TimeSpan.FromSeconds(600), Now);
        var outOfQuota = OutboxRetryLater.NextRun(TimeSpan.FromSeconds(3600), Now);

        Assert.That(paused.milliseconds, Is.EqualTo(Now.milliseconds + 600 * 1000));
        Assert.That(outOfQuota.milliseconds, Is.EqualTo(Now.milliseconds + 3600 * 1000));
    }

    [Test]
    public void ItemIsNotExpiredBeforeTheMaxAge()
    {
        var maxAge = TimeSpan.FromDays(7);
        var added = new UnixTimeUtc(Now.milliseconds - (long)maxAge.TotalMilliseconds + 1);

        Assert.That(OutboxRetryLater.IsExpired(added, Now, maxAge), Is.False);
    }

    [Test]
    public void ItemIsExpiredAtTheMaxAge()
    {
        var maxAge = TimeSpan.FromDays(7);
        var added = new UnixTimeUtc(Now.milliseconds - (long)maxAge.TotalMilliseconds);

        Assert.That(OutboxRetryLater.IsExpired(added, Now, maxAge), Is.True);
    }

    [Test]
    public void AFreshItemIsNotExpired()
    {
        Assert.That(OutboxRetryLater.IsExpired(Now, Now, TimeSpan.FromDays(7)), Is.False);
    }
}
