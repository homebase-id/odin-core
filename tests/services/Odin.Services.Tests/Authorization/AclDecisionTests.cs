using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authorization.Acl;

namespace Odin.Services.Tests.Authorization;

/// <summary>
/// The one ACL decision both the caller path and the identity (feed distribution) path use: the security
/// groups are a ladder, and named circles and identities narrow the ACL together (in any named circle, or any
/// named identity) rather than replacing its security group.
/// </summary>
public class AclDecisionTests
{
    private static readonly Guid Friends = Guid.NewGuid();
    private static readonly OdinId Sam = new("sam.dotyou.cloud");
    private static readonly OdinId Pippin = new("pippin.dotyou.cloud");
    private static readonly OdinId Merry = new("merry.dotyou.cloud");

    private static AccessControlList Acl(SecurityGroupType group, params Guid[] circles) => new()
    {
        RequiredSecurityGroup = group,
        CircleIdList = circles.Length == 0 ? null : [..circles]
    };

    private static AccessControlList Named(SecurityGroupType group, Guid[] circles, params OdinId[] identities)
    {
        var acl = Acl(group, circles);
        acl.OdinIdList = [..identities.Select(i => i.DomainName)];
        return acl;
    }

    private static bool Allows(AccessControlList acl, SecurityGroupType level, params Guid[] circles) =>
        DriveAclAuthorizationService.Allows(acl, level, new List<Guid>(circles), null);

    private static bool Allows(AccessControlList acl, SecurityGroupType level, OdinId reader, params Guid[] circles) =>
        DriveAclAuthorizationService.Allows(acl, level, new List<Guid>(circles), reader);

    [TestCase(SecurityGroupType.Anonymous, SecurityGroupType.Anonymous, true)]
    [TestCase(SecurityGroupType.Anonymous, SecurityGroupType.Authenticated, true)]
    [TestCase(SecurityGroupType.Authenticated, SecurityGroupType.Anonymous, false)]
    [TestCase(SecurityGroupType.Authenticated, SecurityGroupType.Authenticated, true)]
    [TestCase(SecurityGroupType.Authenticated, SecurityGroupType.Connected, true)]
    [TestCase(SecurityGroupType.Connected, SecurityGroupType.Authenticated, false)]
    [TestCase(SecurityGroupType.Connected, SecurityGroupType.Connected, true)]
    [TestCase(SecurityGroupType.AutoConnected, SecurityGroupType.Authenticated, false)]
    [TestCase(SecurityGroupType.AutoConnected, SecurityGroupType.Connected, true)]
    [TestCase(SecurityGroupType.Owner, SecurityGroupType.Connected, false)]
    public void TheSecurityGroupsAreALadder(SecurityGroupType required, SecurityGroupType level, bool allowed)
    {
        Assert.That(Allows(Acl(required), level), Is.EqualTo(allowed));
    }

    [Test]
    public void ACircleListNeedsOneOfTheCircles()
    {
        Assert.That(Allows(Acl(SecurityGroupType.Connected, Friends), SecurityGroupType.Connected, Friends), Is.True);
        Assert.That(Allows(Acl(SecurityGroupType.Connected, Friends), SecurityGroupType.Connected, Guid.NewGuid()), Is.False);
    }

    [Test]
    public void ACircleListDoesNotReplaceTheSecurityGroup()
    {
        // In Friends, but unreviewed (evaluated as Authenticated): a Connected ACL still refuses.
        Assert.That(Allows(Acl(SecurityGroupType.Connected, Friends), SecurityGroupType.Authenticated, Friends), Is.False);
    }

    [Test]
    public void ASystemAclAdmitsNoOne()
    {
        Assert.That(Allows(Acl(SecurityGroupType.System), SecurityGroupType.Connected), Is.False);
    }

    [Test]
    public void AnIdentityListAdmitsAnyNamedIdentity()
    {
        var acl = Named(SecurityGroupType.Connected, [], Sam, Pippin);
        Assert.That(Allows(acl, SecurityGroupType.Connected, Sam), Is.True);
        Assert.That(Allows(acl, SecurityGroupType.Connected, Pippin), Is.True);
        Assert.That(Allows(acl, SecurityGroupType.Connected, Merry), Is.False);
        Assert.That(Allows(acl, SecurityGroupType.Connected), Is.False, "an anonymous reader is no one named");
    }

    [Test]
    public void CirclesAndIdentitiesAdmitEitherOne()
    {
        var acl = Named(SecurityGroupType.Connected, [Friends], Sam);
        Assert.That(Allows(acl, SecurityGroupType.Connected, Sam), Is.True, "named, in no circle");
        Assert.That(Allows(acl, SecurityGroupType.Connected, Merry, Friends), Is.True, "in the circle, not named");
        Assert.That(Allows(acl, SecurityGroupType.Connected, Merry, Guid.NewGuid()), Is.False, "neither");
    }

    [Test]
    public void AnIdentityListDoesNotReplaceTheSecurityGroup()
    {
        // Named, but unreviewed or unconnected (evaluated as Authenticated): a Connected ACL still refuses.
        Assert.That(Allows(Named(SecurityGroupType.Connected, [], Sam), SecurityGroupType.Authenticated, Sam), Is.False);
        Assert.That(Allows(Named(SecurityGroupType.Authenticated, [], Sam), SecurityGroupType.Authenticated, Sam), Is.True);
    }

    [Test]
    public void ValidateStoresIdentitiesCanonicallyAndOnce()
    {
        var acl = Acl(SecurityGroupType.Connected);
        acl.OdinIdList = ["Sam.DotYou.Cloud", "sam.dotyou.cloud", "pippin.dotyou.cloud"];
        acl.Validate();
        Assert.That(acl.OdinIdList, Is.EqualTo(new[] { "sam.dotyou.cloud", "pippin.dotyou.cloud" }));
        Assert.That(Allows(acl, SecurityGroupType.Connected, Sam), Is.True);
    }

    [Test]
    public void ValidateRefusesAnInvalidIdentity()
    {
        var acl = Acl(SecurityGroupType.Connected);
        acl.OdinIdList = ["not a domain"];
        Assert.Throws<OdinClientException>(() => acl.Validate());
    }
}
