#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// What a Selective Share drive refuses (docs/selective-share-pr3-plan.md, section 1): of a file's ACL, names and every
/// rung but Owner and Connected; of the drive, OwnerOnly and the collaborative channel.  Other drives answer as before.
/// </summary>
public class SelectiveShareDrivesTests
{
    private static readonly Dictionary<string, string> CollaborativeChannel = new()
    {
        [BuiltInDriveAttributes.IsCollaborativeChannel] = "true"
    };

    [TestCase(SecurityGroupType.Owner, true)]
    [TestCase(SecurityGroupType.Connected, true)]
    [TestCase(SecurityGroupType.AutoConnected, false)]
    [TestCase(SecurityGroupType.Authenticated, false)]
    [TestCase(SecurityGroupType.Anonymous, false)]
    public void AFileIsOwnerOrConnected(SecurityGroupType group, bool taken)
    {
        var acl = new AccessControlList { RequiredSecurityGroup = group };

        AssertTaken(() => SelectiveShareDrives.AssertAclFits(Drive(DriveSharingModel.SelectiveShare), acl), taken, $"{group}");
        AssertTaken(() => SelectiveShareDrives.AssertAclFits(Drive(DriveSharingModel.Private), acl), true, $"{group}, another drive");
    }

    [Test]
    public void AFileNamesNoCirclesOrIdentities()
    {
        var circles = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [Guid.NewGuid()] };
        var identities = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, OdinIdList = ["sam.dotyou.cloud"] };
        var empty = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected, CircleIdList = [], OdinIdList = [] };

        AssertTaken(() => SelectiveShareDrives.AssertAclFits(Drive(DriveSharingModel.SelectiveShare), circles), false, "circles");
        AssertTaken(() => SelectiveShareDrives.AssertAclFits(Drive(DriveSharingModel.SelectiveShare), identities), false, "identities");
        AssertTaken(() => SelectiveShareDrives.AssertAclFits(Drive(DriveSharingModel.SelectiveShare), empty), true, "empty lists");
        AssertTaken(() => SelectiveShareDrives.AssertAclFits(Drive(DriveSharingModel.Collaborative), circles), true, "circles, another drive");
    }

    [TestCase(DriveSharingModel.SelectiveShare, false)]
    [TestCase(DriveSharingModel.Private, true)]
    [TestCase(DriveSharingModel.Collaborative, true)]
    public void OnlyASelectiveShareDriveRefusesOwnerOnly(DriveSharingModel model, bool taken)
    {
        AssertTaken(() => Fit(model, ownerOnly: true), taken, $"{model}");
        AssertTaken(() => Fit(model), true, $"{model}, not owner only");
    }

    [TestCase(DriveSharingModel.SelectiveShare, false)]
    [TestCase(DriveSharingModel.Private, false)]
    [TestCase(DriveSharingModel.Collaborative, true)]
    public void OnlyAServerShareDriveMayBeMarkedAsACommunityChannel(DriveSharingModel model, bool taken)
    {
        var notSet = new Dictionary<string, string> { [BuiltInDriveAttributes.IsCollaborativeChannel] = "false" };

        AssertTaken(() => Fit(model, attributes: CollaborativeChannel), taken, $"{model}", model);
        AssertTaken(() => Fit(model, attributes: notSet), true, $"{model}, set to false");
    }

    [TestCase(DriveSharingModel.SelectiveShare, false)]
    [TestCase(DriveSharingModel.Private, false)]
    [TestCase(DriveSharingModel.Collaborative, true)]
    public void OnlyAServerShareDriveAllowsAnonymousReadsTheCdnAndSubscriptions(DriveSharingModel model, bool taken)
    {
        AssertTaken(() => Fit(model, anonymous: true), taken, $"{model}, anonymous reads", model);
        AssertTaken(() => Fit(model, cdn: true), taken, $"{model}, the CDN", model);
        AssertTaken(() => Fit(model, subscriptions: true), taken, $"{model}, subscriptions", model);
    }

    private static void Fit(DriveSharingModel model, bool ownerOnly = false, bool anonymous = false, bool cdn = false,
        bool subscriptions = false, Dictionary<string, string>? attributes = null) =>
        DriveSettings.AssertFit(model, ownerOnly, anonymous, cdn, subscriptions, attributes);

    private static StorageDrive Drive(DriveSharingModel model) => new(null, new StorageDriveData { SharingModel = model });

    /// <summary>Taken, or refused with a message naming <paramref name="model"/>.</summary>
    private static void AssertTaken(TestDelegate check, bool taken, string what,
        DriveSharingModel model = DriveSharingModel.SelectiveShare)
    {
        if (taken)
        {
            Assert.DoesNotThrow(check, what);
        }
        else
        {
            var e = Assert.Throws<OdinClientException>(check, what);
            Assert.That(e!.Message, Does.Contain(model.ToString()), what);
        }
    }
}
