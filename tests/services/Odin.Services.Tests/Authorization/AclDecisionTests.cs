using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Services.Authorization.Acl;

namespace Odin.Services.Tests.Authorization;

/// <summary>
/// The one ACL decision both the caller path and the identity (feed distribution) path use: the security
/// groups are a ladder, and a circle list narrows the ACL rather than replacing its security group.
/// </summary>
public class AclDecisionTests
{
    private static readonly Guid Friends = Guid.NewGuid();

    private static AccessControlList Acl(SecurityGroupType group, params Guid[] circles) => new()
    {
        RequiredSecurityGroup = group,
        CircleIdList = circles.Length == 0 ? null : [..circles]
    };

    private static bool Allows(AccessControlList acl, SecurityGroupType level, params Guid[] circles) =>
        DriveAclAuthorizationService.Allows(acl, level, new List<Guid>(circles));

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
    public void NamedIdentitiesAdmitNoOne()
    {
        var acl = Acl(SecurityGroupType.Connected);
        acl.OdinIdList = ["frodo.dotyou.cloud"];
        Assert.That(Allows(acl, SecurityGroupType.Connected), Is.False);
    }
}
