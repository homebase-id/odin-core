using System.Collections.Generic;
using System.Linq;
using Odin.Core.Dns;

namespace Odin.Services.Registry.Registration;

#nullable enable

/// <summary>
/// DNSSEC state of an own-domain whose zone we host. See docs/byod-dnssec-plan.md.
/// </summary>
public enum DnssecStatus
{
    /// <summary>Zone hosting is not configured on this deployment</summary>
    NotConfigured,

    /// <summary>No zone for the domain in our PowerDNS (not provisioned yet, or a managed domain)</summary>
    ZoneNotHosted,

    /// <summary>Zone exists but has no active published signing key - should not happen (dnssec is enabled at creation); ops should investigate</summary>
    ZoneNotSigned,

    /// <summary>Our zone is signed but the parent zone is not - a DS cannot extend the chain of trust; informational, resolution works insecurely</summary>
    ParentUnsigned,

    /// <summary>Parent is signed and publishes no DS for the domain - the user can add the DS we provide (registrar for an apex, DNS host for a subdomain)</summary>
    DsMissing,

    /// <summary>DS records exist at the parent but NONE matches our keys - validating resolvers will SERVFAIL; the user must replace/remove the DS</summary>
    DsMismatch,

    /// <summary>At least one DS at the parent matches our keys - the chain of trust is anchored</summary>
    Secure,
}

public sealed class DnssecStatusResult
{
    public DnssecStatus Status { get; init; }

    /// <summary>The DS records of our zone's signing keys - what the user publishes at the parent</summary>
    public List<DsRecordData> OurDsRecords { get; init; } = [];

    /// <summary>The DS records actually published at the parent (empty when none)</summary>
    public List<DsRecordData> ParentDsRecords { get; init; } = [];

    /// <summary>Whether the parent zone itself is DNSSEC-signed (a prerequisite for any DS to matter)</summary>
    public bool ParentZoneSigned { get; init; }

    //

    /// <summary>
    /// The pure DS-vs-parent verdict, applied once the zone-side questions (configured,
    /// hosted, signed) are answered. Kept side-effect free for data-level testing.
    /// Precedence: an unsigned parent terminates everything (published DS records are
    /// inert without a signed parent); then missing vs. matching vs. mismatching DS -
    /// where a single match anchors the chain regardless of stale extras.
    /// </summary>
    internal static DnssecStatus ComputeVerdict(
        IReadOnlyCollection<DsRecordData> ourDsRecords,
        IReadOnlyCollection<DsRecordData> parentDsRecords,
        bool parentZoneSigned)
    {
        if (ourDsRecords.Count == 0)
        {
            return DnssecStatus.ZoneNotSigned;
        }
        if (!parentZoneSigned)
        {
            return DnssecStatus.ParentUnsigned;
        }
        if (parentDsRecords.Count == 0)
        {
            return DnssecStatus.DsMissing;
        }
        return parentDsRecords.Any(parent => ourDsRecords.Any(ours => ours.Matches(parent)))
            ? DnssecStatus.Secure
            : DnssecStatus.DsMismatch;
    }
}
