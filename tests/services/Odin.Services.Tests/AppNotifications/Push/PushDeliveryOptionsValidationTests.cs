#nullable enable
using System;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Tests.AppNotifications.Push;

public class PushDeliveryOptionsValidationTests
{
    private static AppNotificationOptions Valid() => new()
    {
        TypeId = Guid.NewGuid(),
    };

    [Test]
    public void MinimalOptions_AreValid()
    {
        Assert.DoesNotThrow(() => PushDeliveryOptionsValidation.AssertValid(Valid()));
    }

    [Test]
    public void EmptyTypeId_IsRejected()
    {
        var options = Valid();
        options.TypeId = Guid.Empty;

        var ex = Assert.Throws<OdinClientException>(() => PushDeliveryOptionsValidation.AssertValid(options));

        Assert.That(ex!.Message, Does.Contain("TypeId"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(86401)]
    public void TtlOutsideOneSecondToOneDay_IsRejected(int ttl)
    {
        var options = Valid();
        options.TimeToLiveSeconds = ttl;

        var ex = Assert.Throws<OdinClientException>(() => PushDeliveryOptionsValidation.AssertValid(options));

        Assert.That(ex!.Message, Does.Contain("TimeToLiveSeconds").And.Contain(ttl.ToString()));
    }

    [TestCase(1)]
    [TestCase(45)]
    [TestCase(86400)]
    public void TtlWithinRange_IsAccepted(int ttl)
    {
        var options = Valid();
        options.TimeToLiveSeconds = ttl;

        Assert.DoesNotThrow(() => PushDeliveryOptionsValidation.AssertValid(options));
    }

    [Test]
    public void CollapseIdOf64Chars_IsAccepted()
    {
        var options = Valid();
        options.CollapseId = new string('c', 64);

        Assert.DoesNotThrow(() => PushDeliveryOptionsValidation.AssertValid(options));
    }

    [Test]
    public void CollapseIdOf65Chars_IsRejected()
    {
        var options = Valid();
        options.CollapseId = new string('c', 65);

        var ex = Assert.Throws<OdinClientException>(() => PushDeliveryOptionsValidation.AssertValid(options));

        Assert.That(ex!.Message, Does.Contain("CollapseId").And.Contain("65"));
    }
}
