using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;

namespace Odin.Services.Tests.Authorization.Acl;

/// <summary>
/// The 777 slot was renamed <c>Connected</c> -> <c>Reviewed</c>, and enums persist as camelCase strings
/// (<c>OdinSystemSerializer</c>), so every stored ACL on disk already says <c>"connected"</c>.
/// </summary>
/// <remarks>
/// These assert against hardcoded strings on purpose.  A round-trip test cannot catch a broken
/// <c>[JsonStringEnumMemberName]</c>: the value would serialize as <c>"reviewed"</c> and deserialize back
/// to <see cref="SecurityGroupType.Reviewed"/>, perfectly self-consistent, while orphaning every file
/// header written before the rename.
/// </remarks>
[TestFixture]
public class SecurityGroupTypeSerializationTests
{
    [Test]
    public void ReviewedStillSerializesAsConnected()
    {
        var acl = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Reviewed };

        var json = OdinSystemSerializer.Serialize(acl);

        Assert.That(json, Does.Contain("\"connected\""),
            $"the 777 slot must stay on the wire as 'connected'; got {json}");
        Assert.That(json, Does.Not.Contain("reviewed"),
            "emitting 'reviewed' would orphan every ACL written before the rename");
    }

    [Test]
    public void ConnectedOnTheWireDeserializesToReviewed()
    {
        const string json = """{"requiredSecurityGroup":"connected"}""";

        var acl = OdinSystemSerializer.Deserialize<AccessControlList>(json);

        Assert.That(acl.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Reviewed));
    }

    [Test]
    public void AutoConnectedOnTheWireStillDeserializes()
    {
        // Clients set this on files -- the chat app ACLs messages with it -- so stored headers name it.
        // Dropping the member would throw on every one of them.
        const string json = """{"requiredSecurityGroup":"autoconnected"}""";

        AccessControlList acl = null;

        Assert.DoesNotThrow(() => acl = OdinSystemSerializer.Deserialize<AccessControlList>(json),
            "a stored header naming 'autoconnected' must still parse");

        Assert.That(acl!.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.AutoConnected));
        Assert.That((int)acl.RequiredSecurityGroup, Is.EqualTo(555),
            "the enum must agree with the indexed requiredSecurityGroup column, which holds 555");
    }

    [Test]
    [TestCase(SecurityGroupType.Anonymous, "anonymous")]
    [TestCase(SecurityGroupType.Authenticated, "authenticated")]
    [TestCase(SecurityGroupType.Reviewed, "connected")]
    [TestCase(SecurityGroupType.Owner, "owner")]
    public void EveryTierRoundTripsThroughItsWireValue(SecurityGroupType tier, string wire)
    {
        var json = $$"""{"requiredSecurityGroup":"{{wire}}"}""";

        var acl = OdinSystemSerializer.Deserialize<AccessControlList>(json);
        Assert.That(acl.RequiredSecurityGroup, Is.EqualTo(tier));

        var reserialized = OdinSystemSerializer.Serialize(acl);
        Assert.That(reserialized, Does.Contain($"\"{wire}\""));
    }

    [Test]
    public void TheNumericSlotsAreUnchanged()
    {
        // The DB filters on requiredSecurityGroup BETWEEN 0 AND callerLevel, so the ordering is
        // load-bearing and the values are effectively part of the schema.
        Assert.That((int)SecurityGroupType.Anonymous, Is.EqualTo(111));
        Assert.That((int)SecurityGroupType.Authenticated, Is.EqualTo(444));
        Assert.That((int)SecurityGroupType.AutoConnected, Is.EqualTo(555));
        Assert.That((int)SecurityGroupType.Reviewed, Is.EqualTo(777));
        Assert.That((int)SecurityGroupType.Owner, Is.EqualTo(999));
    }
}
