using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// AllowCdn replaced a rule that read a "blockcdn" string out of the drive's free-form attribute
/// bag: CDN-eligible when AllowAnonymousReads was set OR blockcdn was not exactly "true".
///
/// Drives written before the flag existed have no AllowCdn in their stored detailsJson, so it
/// deserializes to null and the old rule has to decide. These tests pin that down - the point of
/// the flag was to make the switch explicit, not to change who the CDN can read.
/// </summary>
[TestFixture]
public class AllowCdnResolutionTests
{
    private static StorageDriveDetails Legacy(bool allowAnonymousReads, string blockCdnValue = null)
    {
        var attributes = new Dictionary<string, string>();
        if (blockCdnValue != null)
        {
            attributes["blockcdn"] = blockCdnValue;
        }

        return new StorageDriveDetails
        {
            AllowAnonymousReads = allowAnonymousReads,
            Attributes = attributes,
            AllowCdn = null // the whole point: stored before the flag existed
        };
    }

    [Test]
    public void StoredDefinitionWithoutTheFlagDeserializesToNull()
    {
        // A detailsJson written before AllowCdn existed. If this ever came back as `false`
        // instead of null, every pre-existing drive would silently drop out of the CDN.
        const string legacyJson = """
            {"Metadata":"","OwnerOnly":false,"IsReadonly":false,
             "AllowAnonymousReads":true,"AllowSubscriptions":false,"IsArchived":false}
            """;

        var details = OdinSystemSerializer.Deserialize<StorageDriveDetails>(legacyJson);

        Assert.That(details.AllowCdn, Is.Null);
    }

    [Test]
    public void ExplicitValueAlwaysWins()
    {
        var enabled = Legacy(allowAnonymousReads: false, blockCdnValue: "true");
        enabled.AllowCdn = true;
        Assert.That(DriveManager.ResolveAllowCdn(enabled), Is.True);

        var disabled = Legacy(allowAnonymousReads: true);
        disabled.AllowCdn = false;
        Assert.That(DriveManager.ResolveAllowCdn(disabled), Is.False,
            "an explicit false must not be overridden by the legacy rule");
    }

    [Test]
    public void LegacyDriveWithNoBlockCdnAttributeStaysCdnEligible()
    {
        Assert.That(DriveManager.ResolveAllowCdn(Legacy(allowAnonymousReads: false)), Is.True);
        Assert.That(DriveManager.ResolveAllowCdn(Legacy(allowAnonymousReads: true)), Is.True);
    }

    [Test]
    public void LegacyDriveWithBlockCdnTrueIsNotCdnEligible()
    {
        Assert.That(DriveManager.ResolveAllowCdn(Legacy(allowAnonymousReads: false, blockCdnValue: "true")), Is.False);
        Assert.That(DriveManager.ResolveAllowCdn(Legacy(allowAnonymousReads: false, blockCdnValue: "True")), Is.False,
            "bool.TryParse is case-insensitive, as it was before");
    }

    [Test]
    public void LegacyBlockCdnDidNotOverrideAnonymousReads()
    {
        // Faithful to the old expression, quirk included: it OR-ed AllowAnonymousReads against
        // the attribute, so blockcdn could never actually block an anonymous drive. Preserved
        // deliberately - "keep the old behaviour" includes the parts of it that were surprising.
        Assert.That(DriveManager.ResolveAllowCdn(Legacy(allowAnonymousReads: true, blockCdnValue: "true")), Is.True);
    }

    [Test]
    public void LegacyUnparseableBlockCdnValueDoesNotBlock()
    {
        // The old AttributeHasFalseValue treated anything non-boolean as "not blocking".
        Assert.That(DriveManager.ResolveAllowCdn(Legacy(allowAnonymousReads: false, blockCdnValue: "yes")), Is.True);
        Assert.That(DriveManager.ResolveAllowCdn(Legacy(allowAnonymousReads: false, blockCdnValue: "")), Is.True);
    }

    [Test]
    public void LegacyDriveWithNoAttributeBagAtAllStaysCdnEligible()
    {
        var details = new StorageDriveDetails { AllowAnonymousReads = false, Attributes = null, AllowCdn = null };
        Assert.That(DriveManager.ResolveAllowCdn(details), Is.True);
    }

    //
    // IsCdnEnabled / SetCdnEnabled - the accessor pair every call site goes through, so the
    // rule can be changed in one place. TenantPathManager is not touched by either.
    //

    private static StorageDrive DriveWith(bool allowCdn) =>
        new(null, new StorageDriveData { AllowCdn = allowCdn });

    [Test]
    public void IsCdnEnabledReportsTheResolvedFlag()
    {
        Assert.That(DriveWith(true).IsCdnEnabled(), Is.True);
        Assert.That(DriveWith(false).IsCdnEnabled(), Is.False);
    }

    [Test]
    public void SetCdnEnabledRoundTripsThroughIsCdnEnabled()
    {
        var drive = DriveWith(false);

        drive.SetCdnEnabled(true);
        Assert.That(drive.IsCdnEnabled(), Is.True);

        drive.SetCdnEnabled(false);
        Assert.That(drive.IsCdnEnabled(), Is.False);
    }

    [Test]
    public void CdnEligibilityIsIndependentOfAnonymousReadsAndOwnerOnly()
    {
        // Once resolved, CDN eligibility is its own switch. Notably an owner-only, non-anonymous
        // drive can still be CDN-eligible - the rule this replaced listed those too, and the
        // Contacts system drive is exactly that shape.
        var ownerOnly = new StorageDrive(null, new StorageDriveData
        {
            AllowCdn = true,
            AllowAnonymousReads = false,
            OwnerOnly = true
        });

        Assert.That(ownerOnly.IsCdnEnabled(), Is.True);
        Assert.That(ownerOnly.AllowAnonymousReads, Is.False);
        Assert.That(ownerOnly.OwnerOnly, Is.True);
    }
}
