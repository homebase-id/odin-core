using System.Linq;
using Odin.Services.Dns;

namespace Odin.Services.Registry.Registration;

#nullable enable

/// <summary>One of an identity's rrsets: as the zone has it (null if it has none) and as this host would write it.</summary>
public sealed record IdentityDnsChange(DnsRrset? Current, DnsRrset Desired)
{
    public bool Changes => Current == null || Current.Ttl != Desired.Ttl || !Current.Contents.ToHashSet().SetEquals(Desired.Contents);
}
