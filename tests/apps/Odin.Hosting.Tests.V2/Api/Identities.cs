using Odin.Hosting.Tests;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Canonical test-identity domain names. Derived from <see cref="TestIdentities"/> in the V1 test
/// project so both frameworks share a single source of truth: certs, preconfigured tenants, and
/// any production seeding for these domains stay aligned.
/// </summary>
public static class Identities
{
    public static readonly string Frodo = TestIdentities.Frodo.OdinId.DomainName;
    public static readonly string Sam = TestIdentities.Samwise.OdinId.DomainName;
    public static readonly string Merry = TestIdentities.Merry.OdinId.DomainName;
    public static readonly string Pippin = TestIdentities.Pippin.OdinId.DomainName;
    public static readonly string TomBombadil = TestIdentities.TomBombadil.OdinId.DomainName;
}
