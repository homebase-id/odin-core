using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Services.Configuration;
using Odin.Services.Dns.Health;
using Odin.Services.Email;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Dns.Health;

#nullable enable

public class DnsInfraVerifierTest
{
    private const string Alias = "identity-host.hosting.example";
    private const string Zone = "hosting.example";
    private const string ApexIp = "131.164.170.62";

    // RFC 4034 section 5.4 example key (pattern from DnsHealthServiceTest)
    private const string Rfc4034PublicKeyBase64 =
        "AQOeiiR0GOMYkDshWoSKz9XzfwJr1AYtsmx3TGkJaNXVbfi/2pHm822aJ5iI9BMzNXxeYCmZDRD99WYwYqUSdjMmmAphXdvx" +
        "egXd/M5+X7OrzKBaMbCVdFLUUh6DhweJBjEVv5f2wwjM9XzcnOf+EPbtG9DMBmADjFDc2w/rljwvFw==";

    private static DnsKeyRecord TestKey(string owner = Zone + ".", int flags = 257)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.DNSKEY, QueryClass.IN, 3600, 0);
        return new DnsKeyRecord(info, flags, 3, 5, Convert.FromBase64String(Rfc4034PublicKeyBase64));
    }

    private readonly Mock<ILookupClient> _dnsClient = new();
    private readonly Mock<IAuthoritativeDnsLookup> _authoritativeDnsLookup = new();
    private readonly Mock<IDnssecLookup> _dnssecLookup = new();

    [SetUp]
    public void SetUp()
    {
        _dnsClient.Reset();
        _authoritativeDnsLookup.Reset();
        _dnssecLookup.Reset();
    }

    /// <summary>
    /// The check is skippable, so a host whose configured hostnames are not the real ones does not
    /// spend three retry rounds logging lookups that were never going to succeed. It is a switch
    /// rather than a domain allowlist so it covers any local-testing domain, and so no test domain
    /// has to be named in production code.
    /// </summary>
    [Test]
    public void HasChecksIsFalseWhenVerificationIsDisabled()
    {
        var verifier = CreateVerifier(verificationEnabled: false, email: null, managedApexes: "example.com");
        Assert.That(verifier.HasChecks, Is.False);
    }

    [Test]
    public void HasChecksIsTrueByDefaultWhenSomethingIsConfigured()
    {
        var verifier = CreateVerifier(email: null, managedApexes: "example.com");
        Assert.That(verifier.HasChecks, Is.True);
    }

    private DnsInfraVerifier CreateVerifier(
        OdinConfiguration.EmailSection? email = null,
        params string[] managedApexes)
    {
        return CreateVerifier(true, email, managedApexes);
    }

    private DnsInfraVerifier CreateVerifier(
        bool verificationEnabled,
        OdinConfiguration.EmailSection? email = null,
        params string[] managedApexes)
    {
        var configuration = new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = new DnsConfigurationSet(ApexIp, Alias),
                DnsInfraVerificationEnabled = verificationEnabled,
                ManagedDomainApexes = managedApexes
                    .Select(x => new OdinConfiguration.RegistrySection.ManagedDomainApex { Apex = x })
                    .ToList(),
            },
            Email = email ?? new OdinConfiguration.EmailSection(),
        };
        return new DnsInfraVerifier(
            new Mock<ILogger<DnsInfraVerifier>>().Object,
            configuration,
            _dnsClient.Object,
            _authoritativeDnsLookup.Object,
            _dnssecLookup.Object);
    }

    //
    // Setup helpers
    //

    private void SetupAliasQuery(params DnsResourceRecord[] answers)
    {
        _dnsClient
            .Setup(x => x.QueryAsync(Alias, QueryType.A, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(answers));
    }

    private void SetupZoneApex(string host, string zone)
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(host, It.IsAny<CancellationToken>()))
            .ReturnsAsync(zone);
    }

    private void SetupAnchoredZone(string zone)
    {
        var key = TestKey(zone + ".");
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(zone, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(zone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(zone, It.IsAny<CancellationToken>()))
            .ReturnsAsync([DnssecLookup.ComputeDsFromDnsKey(zone, key)]);
    }

    private void SetupHealthyServerHostname()
    {
        SetupAliasQuery(A(Alias + ".", ApexIp));
        SetupZoneApex(Alias, Zone);
        SetupAnchoredZone(Zone);
    }

    //
    // Server hostname resolution
    //

    [Test]
    public async Task ItShouldPassWhenHostnameResolvesAndZoneIsAnchored()
    {
        SetupHealthyServerHostname();

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.IsClean, Is.True);
    }

    [Test]
    public async Task ItShouldWarnWhenHostnameResolvesViaCname()
    {
        // The A query follows the chain, so addresses arrive anyway - the CNAME in the
        // answer chain is what marks the extra zones the DNSSEC chain now depends on
        SetupAliasQuery(Cname(Alias + ".", "other-host.elsewhere.example."), A("other-host.elsewhere.example.", ApexIp));
        SetupZoneApex(Alias, Zone);
        SetupAnchoredZone(Zone);

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Single(), Does.Contain("CNAME"));
    }

    [Test]
    public async Task ItShouldErrorWhenHostnameDoesNotResolve()
    {
        SetupAliasQuery( /* no answers */);
        SetupZoneApex(Alias, Zone);
        SetupAnchoredZone(Zone);

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors.Single(), Does.Contain("does not resolve"));
    }

    [Test]
    public async Task ItShouldWarnWhenHostnameDisagreesWithTheApexARecord()
    {
        SetupAliasQuery(A(Alias + ".", "10.9.9.9"));
        SetupZoneApex(Alias, Zone);
        SetupAnchoredZone(Zone);

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Single(), Does.Contain(ApexIp));
    }

    [Test]
    public async Task ItShouldReportAFailedHostnameLookupAsAnErrorNotACrash()
    {
        _dnsClient
            .Setup(x => x.QueryAsync(Alias, QueryType.A, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DnsResponseException("timeout"));
        SetupZoneApex(Alias, Zone);
        SetupAnchoredZone(Zone);

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors.Single(), Does.Contain("lookup failed"));
    }

    //
    // DNSSEC anchoring of the enclosing zone
    //

    [Test]
    public async Task ItShouldWarnWhenTheZoneIsUnsigned()
    {
        SetupAliasQuery(A(Alias + ".", ApexIp));
        SetupZoneApex(Alias, Zone);
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(Zone, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Single(), Does.Contain("not DNSSEC-signed").And.Contain(Zone).And.Contain(Alias));
    }

    [Test]
    public async Task ItShouldWarnWhenTheZoneIsSignedButNotAnchored()
    {
        SetupAliasQuery(A(Alias + ".", ApexIp));
        SetupZoneApex(Alias, Zone);
        SetupAnchoredZone(Zone);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Zone, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Single(), Does.Contain("no DS record is published"));
    }

    [Test]
    public async Task ItShouldErrorOnADsMismatchBecauseResolversWillServfail()
    {
        SetupAliasQuery(A(Alias + ".", ApexIp));
        SetupZoneApex(Alias, Zone);
        SetupAnchoredZone(Zone);
        var realDs = DnssecLookup.ComputeDsFromDnsKey(Zone, TestKey());
        var mismatchingDs = realDs with { Digest = new string('0', realDs.Digest.Length) };
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Zone, It.IsAny<CancellationToken>()))
            .ReturnsAsync([mismatchingDs]);

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors.Single(), Does.Contain("SERVFAIL"));
    }

    [Test]
    public async Task ItShouldWarnWhenTheZoneCannotBeDetermined()
    {
        SetupAliasQuery(A(Alias + ".", ApexIp));
        SetupZoneApex(Alias, "");

        var result = await CreateVerifier().VerifyAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Single(), Does.Contain("enclosing zone"));
        _dnssecLookup.VerifyNoOtherCalls();
    }

    //
    // Managed domain apexes
    //

    [Test]
    public async Task ItShouldWarnWhenAManagedDomainApexIsNotAnchored()
    {
        // Tenants under the apex inherit its DNSSEC state wholesale, so an unanchored
        // apex silently voids DNSSEC for every one of them
        SetupHealthyServerHostname();
        SetupZoneApex("demo.rocks", "demo.rocks");
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync("demo.rocks", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateVerifier(managedApexes: "demo.rocks").VerifyAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings.Single(), Does.Contain("managed domain apex 'demo.rocks'"));
    }

    [Test]
    public async Task ItShouldPassAnchoredManagedDomainApexes()
    {
        SetupHealthyServerHostname();
        SetupZoneApex("demo.rocks", "demo.rocks");
        SetupAnchoredZone("demo.rocks");

        var result = await CreateVerifier(managedApexes: "demo.rocks").VerifyAsync();

        Assert.That(result.IsClean, Is.True);
    }

    //
    // MX nodes
    //

    private static OdinConfiguration.EmailSection TenantMailEnabled(params string[] mxNodes)
    {
        return new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.SendGrid,
            TenantMail = new OdinConfiguration.TenantMailSection
            {
                Enabled = true,
                MxNodes = mxNodes.ToList(),
            },
        };
    }

    [Test]
    public async Task ItShouldEvaluateASharedZoneOnceForHostnameAndMxNodes()
    {
        SetupHealthyServerHostname();
        SetupZoneApex("mx1." + Zone, Zone);
        SetupZoneApex("mx2." + Zone, Zone);

        var result = await CreateVerifier(TenantMailEnabled("mx1." + Zone, "mx2." + Zone)).VerifyAsync();

        Assert.That(result.IsClean, Is.True);
        _dnssecLookup.Verify(x => x.GetZoneDnsKeysAsync(Zone, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Test]
    public async Task ItShouldNameEveryAffectedHostWhenASharedZoneIsUnsigned()
    {
        SetupAliasQuery(A(Alias + ".", ApexIp));
        SetupZoneApex(Alias, Zone);
        SetupZoneApex("mx1." + Zone, Zone);
        _dnssecLookup
            .Setup(x => x.GetZoneDnsKeysAsync(Zone, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateVerifier(TenantMailEnabled("mx1." + Zone)).VerifyAsync();

        Assert.That(result.Warnings.Single(), Does.Contain(Alias).And.Contain("mx1." + Zone));
    }

    [Test]
    public async Task ItShouldIgnoreMxNodesWhenTenantMailIsDisabled()
    {
        SetupHealthyServerHostname();

        var email = new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.SendGrid,
            TenantMail = new OdinConfiguration.TenantMailSection
            {
                Enabled = false,
                MxNodes = ["mx1." + Zone],
            },
        };
        var result = await CreateVerifier(email).VerifyAsync();

        Assert.That(result.IsClean, Is.True);
        _authoritativeDnsLookup.Verify(
            x => x.LookupZoneApexAsync("mx1." + Zone, It.IsAny<CancellationToken>()), Times.Never());
    }

    //
    // Record construction helpers (pattern from DnsHealthServiceTest)
    //

    private static IDnsQueryResponse Response(params DnsResourceRecord[] answers)
    {
        var mock = new Mock<IDnsQueryResponse>();
        mock.SetupGet(x => x.HasError).Returns(false);
        mock.SetupGet(x => x.Answers).Returns(answers);
        mock.SetupGet(x => x.Authorities).Returns([]);
        return mock.Object;
    }

    private static ARecord A(string owner, string ip)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.A, QueryClass.IN, 3600, 0);
        return new ARecord(info, IPAddress.Parse(ip));
    }

    private static CNameRecord Cname(string owner, string target)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.CNAME, QueryClass.IN, 3600, 0);
        return new CNameRecord(info, DnsString.Parse(target));
    }
}
