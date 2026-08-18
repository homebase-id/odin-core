using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Registry.Registration;

#nullable enable

// Data-level tests of the pure DNSSEC verdict (docs/byod-dnssec-plan.md). The zone-side
// gates (NotConfigured / ZoneNotHosted) are decided before this function runs.
public class DnssecStatusTest
{
    private static DsRecordData Ds(int keyTag, byte algorithm = 13, byte digestType = 2, string digest = "aabbcc")
    {
        return new DsRecordData(keyTag, algorithm, digestType, digest);
    }

    //

    [Test]
    public void ItShouldReportZoneNotSignedWhenWeHaveNoKeys()
    {
        // No active keys -> the parent state is irrelevant
        var verdict = DnssecStatusResult.ComputeVerdict(
            ourDsRecords: [],
            parentDsRecords: [Ds(1)],
            parentZoneSigned: true);
        Assert.That(verdict, Is.EqualTo(DnssecStatus.ZoneNotSigned));
    }

    [Test]
    public void ItShouldReportParentUnsignedBeforeAnyDsConsideration()
    {
        // A DS published under an unsigned parent is inert - ParentUnsigned wins over
        // both "missing" and "mismatch" interpretations
        var unsignedNoDs = DnssecStatusResult.ComputeVerdict([Ds(1)], [], parentZoneSigned: false);
        Assert.That(unsignedNoDs, Is.EqualTo(DnssecStatus.ParentUnsigned));

        var unsignedStaleDs = DnssecStatusResult.ComputeVerdict([Ds(1)], [Ds(999)], parentZoneSigned: false);
        Assert.That(unsignedStaleDs, Is.EqualTo(DnssecStatus.ParentUnsigned));
    }

    [Test]
    public void ItShouldReportDsMissingWhenParentIsSignedButPublishesNoDs()
    {
        var verdict = DnssecStatusResult.ComputeVerdict([Ds(1)], [], parentZoneSigned: true);
        Assert.That(verdict, Is.EqualTo(DnssecStatus.DsMissing));
    }

    [Test]
    public void ItShouldReportSecureWhenAnyPublishedDsMatchesOurs()
    {
        var verdict = DnssecStatusResult.ComputeVerdict([Ds(1)], [Ds(1)], parentZoneSigned: true);
        Assert.That(verdict, Is.EqualTo(DnssecStatus.Secure));
    }

    [Test]
    public void ItShouldReportSecureDespiteStaleExtraDsRecords()
    {
        // Validation needs only ONE matching DS; a leftover DS from a previous provider
        // next to a matching one does not break the chain
        var verdict = DnssecStatusResult.ComputeVerdict(
            ourDsRecords: [Ds(1)],
            parentDsRecords: [Ds(999, digest: "deadbeef"), Ds(1)],
            parentZoneSigned: true);
        Assert.That(verdict, Is.EqualTo(DnssecStatus.Secure));
    }

    [Test]
    public void ItShouldReportDsMismatchWhenNoPublishedDsMatches()
    {
        // The SERVFAIL scenario: the parent anchors a key we do not hold
        var verdict = DnssecStatusResult.ComputeVerdict(
            ourDsRecords: [Ds(1)],
            parentDsRecords: [Ds(999, digest: "deadbeef")],
            parentZoneSigned: true);
        Assert.That(verdict, Is.EqualTo(DnssecStatus.DsMismatch));
    }

    [Test]
    public void ItShouldMatchDsRecordsCaseInsensitivelyOnTheDigest()
    {
        var verdict = DnssecStatusResult.ComputeVerdict(
            ourDsRecords: [Ds(1, digest: "aabbcc")],
            parentDsRecords: [Ds(1, digest: "AABBCC")],
            parentZoneSigned: true);
        Assert.That(verdict, Is.EqualTo(DnssecStatus.Secure));
    }

    [Test]
    public void ItShouldNotMatchWhenOnlyTheKeyTagAgrees()
    {
        // Key tags are not unique; the whole tuple must agree
        var verdict = DnssecStatusResult.ComputeVerdict(
            ourDsRecords: [Ds(1, digest: "aabbcc")],
            parentDsRecords: [Ds(1, digest: "deadbeef")],
            parentZoneSigned: true);
        Assert.That(verdict, Is.EqualTo(DnssecStatus.DsMismatch));
    }

    //
    // DS presentation parsing (the PowerDNS cryptokey "ds" strings)
    //

    [Test]
    public void ItShouldParseDsPresentationStrings()
    {
        var ds = DsRecordData.TryParse("46082 13 2 C8F816A7A575BDB2F997F682AAB2653BA2CB5EDDB69B036A30742A33BEFAF141");
        Assert.That(ds, Is.Not.Null);
        Assert.That(ds!.KeyTag, Is.EqualTo(46082));
        Assert.That(ds.Algorithm, Is.EqualTo(13));
        Assert.That(ds.DigestType, Is.EqualTo(2));
        // Digest is normalized to lowercase
        Assert.That(ds.Digest, Is.EqualTo("c8f816a7a575bdb2f997f682aab2653ba2cb5eddb69b036a30742a33befaf141"));
    }

    [Test]
    public void ItShouldParseDigestsContainingSpaces()
    {
        // dig and some tools present long digests with embedded spaces
        var ds = DsRecordData.TryParse("46082 13 2 C8F816A7A575BDB2F997F682AAB2653BA2CB5EDD B69B036A30742A33BEFAF141");
        Assert.That(ds, Is.Not.Null);
        Assert.That(ds!.Digest, Is.EqualTo("c8f816a7a575bdb2f997f682aab2653ba2cb5eddb69b036a30742a33befaf141"));
    }

    [Test]
    public void ItShouldRejectMalformedDsStrings()
    {
        Assert.That(DsRecordData.TryParse(""), Is.Null);
        Assert.That(DsRecordData.TryParse("46082 13 2"), Is.Null);
        Assert.That(DsRecordData.TryParse("not a ds record at-all"), Is.Null);
    }

    [Test]
    public void ItShouldCompareParsedAndConstructedRecords()
    {
        var parsed = DsRecordData.TryParse("1 13 2 AABBCC");
        Assert.That(parsed!.Matches(new DsRecordData(1, 13, 2, "aabbcc")), Is.True);
        Assert.That(parsed.Matches(new DsRecordData(1, 13, 4, "aabbcc")), Is.False);
    }
}
