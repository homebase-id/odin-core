using System;
using System.Collections.Generic;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// Unit coverage for the Conditional-Temporal-Read window resolution: the drive ceiling / circle window
/// precedence (the smaller wins; each defaults to one week) and the permission-context plumbing that feeds it.
/// </summary>
[TestFixture]
public class TemporalReadPolicyTests
{
    private const long Day = 60 * 60 * 24;
    private const long Week = Day * 7;

    // ---- Effective window = min(drive ceiling, circle window), each defaulting to one week ----

    [Test]
    public void EffectiveWindow_NoDriveMaxAge_NoCircle_UsesOneWeekDefault()
    {
        Assert.That(TemporalReadPolicy.ComputeEffectiveWindowSeconds(null, null), Is.EqualTo(Week));
        Assert.That(TemporalRead.DefaultWindowSeconds, Is.EqualTo(Week));
    }

    [Test]
    public void EffectiveWindow_DriveMaxAgeBelowDefault_NoCircle_UsesDriveMaxAge()
    {
        // drive = 3 days, circle unset (defaults to 7) -> 3 days
        Assert.That(TemporalReadPolicy.ComputeEffectiveWindowSeconds(3 * Day, null), Is.EqualTo(3 * Day));
    }

    [Test]
    public void EffectiveWindow_DriveMaxAgeAboveDefault_NoCircle_CappedByCircleDefault()
    {
        // drive = 30 days, circle unset (defaults to 7) -> 7 days
        Assert.That(TemporalReadPolicy.ComputeEffectiveWindowSeconds(30 * Day, null), Is.EqualTo(Week));
    }

    [Test]
    public void EffectiveWindow_Drive30_Circle7_Yields7()
    {
        // The user's canonical example: restrict a drive to 30 days, a circle to 7 days -> members get 7.
        Assert.That(TemporalReadPolicy.ComputeEffectiveWindowSeconds(30 * Day, 7 * Day), Is.EqualTo(7 * Day));
    }

    [Test]
    public void EffectiveWindow_Drive3_Circle30_Yields3()
    {
        Assert.That(TemporalReadPolicy.ComputeEffectiveWindowSeconds(3 * Day, 30 * Day), Is.EqualTo(3 * Day));
    }

    [Test]
    public void EffectiveWindow_NoDrive_Circle3_Yields3()
    {
        Assert.That(TemporalReadPolicy.ComputeEffectiveWindowSeconds(null, 3 * Day), Is.EqualTo(3 * Day));
    }

    [Test]
    public void EffectiveWindow_BothEqual_YieldsThatValue()
    {
        Assert.That(TemporalReadPolicy.ComputeEffectiveWindowSeconds(30 * Day, 30 * Day), Is.EqualTo(30 * Day));
    }

    // ---- Drive attribute parsing ----

    [Test]
    public void ParseCeiling_NullAttributes_ReturnsNull()
    {
        Assert.That(TemporalReadPolicy.ParseCeilingSeconds(null), Is.Null);
    }

    [Test]
    public void ParseCeiling_MissingKey_ReturnsNull()
    {
        Assert.That(TemporalReadPolicy.ParseCeilingSeconds(new Dictionary<string, string>()), Is.Null);
    }

    [Test]
    public void ParseCeiling_ValidValue_Parsed()
    {
        var attrs = new Dictionary<string, string> { [TemporalRead.MaxAgeAttributeKey] = (3 * Day).ToString() };
        Assert.That(TemporalReadPolicy.ParseCeilingSeconds(attrs), Is.EqualTo(3 * Day));
    }

    [Test]
    [TestCase("0")]
    [TestCase("-5")]
    [TestCase("abc")]
    [TestCase("")]
    public void ParseCeiling_InvalidOrNonPositive_ReturnsNull(string raw)
    {
        var attrs = new Dictionary<string, string> { [TemporalRead.MaxAgeAttributeKey] = raw };
        Assert.That(TemporalReadPolicy.ParseCeilingSeconds(attrs), Is.Null);
    }

    // ---- Permission group / context window resolution ----

    [Test]
    public void Group_TemporalGrantWithWindow_ReturnsWindow()
    {
        var driveId = Guid.NewGuid();
        var group = GroupWith(Grant(driveId, DrivePermission.ConditionalTemporalRead, 3 * Day));
        Assert.That(group.GetTemporalReadWindowSeconds(driveId), Is.EqualTo(3 * Day));
    }

    [Test]
    public void Group_TemporalGrantWithoutWindow_ReturnsDefault()
    {
        var driveId = Guid.NewGuid();
        var group = GroupWith(Grant(driveId, DrivePermission.ConditionalTemporalRead, null));
        Assert.That(group.GetTemporalReadWindowSeconds(driveId), Is.EqualTo(Week));
    }

    [Test]
    public void Group_NoTemporalGrant_ReturnsNull()
    {
        var driveId = Guid.NewGuid();
        var group = GroupWith(Grant(driveId, DrivePermission.Read, null));
        Assert.That(group.GetTemporalReadWindowSeconds(driveId), Is.Null);
    }

    [Test]
    public void Context_MostPermissiveWindowAcrossGroups_Wins()
    {
        var driveId = Guid.NewGuid();
        var ctx = ContextWith(
            GroupWith(Grant(driveId, DrivePermission.ConditionalTemporalRead, 3 * Day)),
            GroupWith(Grant(driveId, DrivePermission.ConditionalTemporalRead, 5 * Day)));

        // Across circles a caller gets the union (the larger window); the drive ceiling caps it elsewhere.
        Assert.That(ctx.GetTemporalCircleWindowSeconds(driveId), Is.EqualTo(5 * Day));
    }

    [Test]
    public void Context_TemporalFlagDoesNotSatisfyNormalRead()
    {
        var driveId = Guid.NewGuid();
        var ctx = ContextWith(GroupWith(Grant(driveId, DrivePermission.ConditionalTemporalRead, 3 * Day)));

        ClassicAssert.IsFalse(ctx.HasDrivePermission(driveId, DrivePermission.Read));
        ClassicAssert.IsTrue(ctx.HasDrivePermission(driveId, DrivePermission.ConditionalTemporalRead));
    }

    [Test]
    public void Context_AssertTemporalRead_PassesForTemporalFlag()
    {
        var driveId = Guid.NewGuid();
        var ctx = ContextWith(GroupWith(Grant(driveId, DrivePermission.ConditionalTemporalRead, null)));
        Assert.DoesNotThrow(() => ctx.AssertCanConditionalTemporalReadDrive(driveId));
    }

    [Test]
    public void Context_AssertTemporalRead_PassesForFullRead()
    {
        var driveId = Guid.NewGuid();
        var ctx = ContextWith(GroupWith(Grant(driveId, DrivePermission.Read, null)));
        Assert.DoesNotThrow(() => ctx.AssertCanConditionalTemporalReadDrive(driveId));
    }

    [Test]
    public void Context_AssertTemporalRead_ThrowsWhenNoGrant()
    {
        var driveId = Guid.NewGuid();
        var ctx = ContextWith(GroupWith(Grant(Guid.NewGuid(), DrivePermission.ConditionalTemporalRead, null)));
        Assert.Throws<OdinSecurityException>(() => ctx.AssertCanConditionalTemporalReadDrive(driveId));
    }

    // ---- helpers ----

    private static DriveGrant Grant(Guid driveId, DrivePermission permission, long? windowSeconds)
    {
        return new DriveGrant
        {
            DriveId = driveId,
            PermissionedDrive = new PermissionedDrive
            {
                Drive = new TargetDrive { Alias = driveId, Type = Guid.NewGuid() },
                Permission = permission,
                TemporalReadWindowSeconds = windowSeconds
            }
        };
    }

    private static PermissionGroup GroupWith(params DriveGrant[] grants)
    {
        return new PermissionGroup(new PermissionSet(), grants, null, null);
    }

    private static PermissionContext ContextWith(params PermissionGroup[] groups)
    {
        var dict = new Dictionary<string, PermissionGroup>();
        for (var i = 0; i < groups.Length; i++)
        {
            dict["g" + i] = groups[i];
        }

        return new PermissionContext(dict, null);
    }
}
