using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Peer;

namespace Odin.Services.Tests.Base;

/// <summary>
/// A caller refused for a permission key it lacks gets a code a client can tell from any other refusal, and the keys any
/// one of which would have done.
/// </summary>
public class MissingPermissionKeyTests
{
    private static readonly PermissionContext NoKeys = new([], null);

    [Test]
    public void APeerSendWithoutUseTransitWriteNamesIt()
    {
        var e = Assert.Throws<OdinSecurityException>(() => NoKeys.AssertCanUsePeer(PeerAccess.Write));
        AssertMissing(e!, PermissionKeys.UseTransitWrite);
    }

    [Test]
    public void APeerReadWithoutUseTransitReadNamesIt()
    {
        var e = Assert.Throws<OdinSecurityException>(() => NoKeys.AssertCanUsePeer(PeerAccess.Read));
        AssertMissing(e!, PermissionKeys.UseTransitRead);
    }

    [Test]
    public void AMissingKeyIsNamed()
    {
        var e = Assert.Throws<OdinSecurityException>(() => NoKeys.AssertHasPermission(PermissionKeys.ReadConnections));
        AssertMissing(e!, PermissionKeys.ReadConnections);
    }

    [Test]
    public void WhenAnyOfSeveralKeysWouldDoAllAreNamed()
    {
        var e = Assert.Throws<OdinSecurityException>(() =>
            NoKeys.AssertHasAtLeastOnePermission(PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead));
        AssertMissing(e!, PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead);
    }

    private static void AssertMissing(OdinSecurityException e, params int[] keys)
    {
        Assert.That(e.ErrorCode, Is.EqualTo(OdinClientErrorCode.MissingPermissionKey), e.Message);
        Assert.That(e.Extensions, Is.Not.Null, "no extensions");
        Assert.That(e.Extensions!["permissionKeys"], Is.EqualTo(keys));
    }
}
