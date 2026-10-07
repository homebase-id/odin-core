#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Dns.Health;
using Odin.Services.Email.Dkim;
using Odin.Services.Email.Relay;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Dns.Health;

#nullable enable

// A fresh fixture (and so fresh mocks) per test: setups made by one test must not leak into
// the next, which is what used to make these tests pass or fail by run order
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class DnsHealthServiceTest
{
    private static readonly AsciiDomainName Domain = new("frodo.example.com");

    // RFC 4034 section 5.4 example key (key tag 60485, algorithm 5)
    private const string Rfc4034PublicKeyBase64 =
        "AQOeiiR0GOMYkDshWoSKz9XzfwJr1AYtsmx3TGkJaNXVbfi/2pHm822aJ5iI9BMzNXxeYCmZDRD99WYwYqUSdjMmmAphXdvx" +
        "egXd/M5+X7OrzKBaMbCVdFLUUh6DhweJBjEVv5f2wwjM9XzcnOf+EPbtG9DMBmADjFDc2w/rljwvFw==";

    private static DnsKeyRecord TestKey(string owner = "frodo.example.com.", int flags = 257)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.DNSKEY, QueryClass.IN, 3600, 0);
        return new DnsKeyRecord(info, flags, 3, 5, System.Convert.FromBase64String(Rfc4034PublicKeyBase64));
    }

    private readonly Mock<IAuthoritativeDnsLookup> _authoritativeDnsLookup = new();
    private readonly Mock<IDnssecLookup> _dnssecLookup = new();
    private readonly Mock<IDnsLookupService> _dnsLookupService = new();

    // Not configured: these tests cover the DNS/DNSSEC blocks, and an unconfigured store
    // is what every non-mail host looks like, so DKIM contributes no records here.
    private readonly Mock<IDkimStore> _dkimStore = new();
    private readonly Mock<ILookupClient> _dnsClient = new();
    private readonly Mock<IMailRelayFailureStore> _relayFailureStore = new();

    // Defaults every health call reaches: no authority (www -> NotSet) and an unsigned zone.
    // Tests set their own on top.
    [SetUp]
    public void SetUpDefaults()
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupDomainAuthorityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthoritativeDnsLookupResult());

        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private DnsHealthService CreateService(bool tenantMailEnabled = false, IMailRelayProvider? relayProvider = null)
    {
        var configuration = new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = new DnsConfigurationSet("131.164.170.62", "identity-host.example"),
                // Our managed apex: an Inherited domain inside it gets its enclosing zone graded
                ManagedDomainApexes = [new OdinConfiguration.RegistrySection.ManagedDomainApex { Apex = "example.com" }],
            },
            Email = new OdinConfiguration.EmailSection
            {
                TenantMail = new OdinConfiguration.TenantMailSection { Enabled = tenantMailEnabled },
            },
        };
        return new DnsHealthService(
            new Mock<ILogger<DnsHealthService>>().Object,
            configuration,
            _dnsClient.Object,
            _authoritativeDnsLookup.Object,
            _dnssecLookup.Object,
            _dnsLookupService.Object,
            _dkimStore.Object,
            // No relay unless a test asks for one: most tests are about the config-derived
            // records, and the relay's per-tenant rows are covered by the relay tests below.
            relayProvider ?? new NullMailRelayProvider(),
            _relayFailureStore.Object);
    }

    //
    // Verdict (pure)
    //

    [Test]
    public void ItShouldRankParentUnsignedAboveEverything()
    {
        Assert.That(DnsHealthService.ComputeVerdict(parentZoneSigned: false, parentDsCount: 0, anyDsMatches: false),
            Is.EqualTo(DnsHealthDnssecStatus.ParentUnsigned));
        // Even with a published (inert) DS
        Assert.That(DnsHealthService.ComputeVerdict(parentZoneSigned: false, parentDsCount: 1, anyDsMatches: true),
            Is.EqualTo(DnsHealthDnssecStatus.ParentUnsigned));
    }

    [Test]
    public void ItShouldDistinguishMissingMatchingAndMismatchingDs()
    {
        Assert.That(DnsHealthService.ComputeVerdict(true, 0, false), Is.EqualTo(DnsHealthDnssecStatus.DsMissing));
        Assert.That(DnsHealthService.ComputeVerdict(true, 1, true), Is.EqualTo(DnsHealthDnssecStatus.Secure));
        Assert.That(DnsHealthService.ComputeVerdict(true, 1, false), Is.EqualTo(DnsHealthDnssecStatus.DsMismatch));
    }

    //
    // DS matching against zone keys (pure)
    //

    [Test]
    public void ItShouldMatchAPublishedDsInWhateverDigestTypeTheParentChose()
    {
        var key = TestKey("dskey.example.com.");

        // Parent published SHA-1 (type 1) - matching must recompute in THAT type, not
        // insist on our SHA-256 default
        var publishedSha1 = DnssecLookup.ComputeDsFromDnsKey("dskey.example.com", key, 1);
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [publishedSha1]), Is.True);

        var publishedSha256 = DnssecLookup.ComputeDsFromDnsKey("dskey.example.com", key);
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [publishedSha256]), Is.True);
    }

    [Test]
    public void ItShouldNotMatchAForeignOrUncomputableDs()
    {
        var key = TestKey("dskey.example.com.");

        var foreign = new DsRecordData(11111, 13, 2, "deadbeef");
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [foreign]), Is.False);

        // Digest type we cannot compute (GOST=3) is skipped, not an exception
        var gost = new DsRecordData(60485, 5, 3, "deadbeef");
        Assert.That(DnsHealthService.AnyPublishedDsMatchesZoneKeys(
            "dskey.example.com", [key], [gost]), Is.False);
    }

    //
    // Record split: Optional-flagged rows (the email record set) must reach clients
    // as MailRecords, never as failed-looking required Records
    //

    /// <summary>
    /// DKIM is per-tenant key material, not configuration, so it cannot come from
    /// GetDnsConfiguration. It is handed to the lookup service as extra records and so
    /// gets verified — and reported — exactly like every other mail record.
    /// </summary>
    [Test]
    public async Task ItShouldCheckTheTenantsDkimRecords()
    {
        IReadOnlyCollection<DnsConfig>? passedExtras = null;
        SetupLookupCapturing(extras => passedExtras = extras);

        _dkimStore.Setup(x => x.IsConfigured).Returns(true);
        _dkimStore.Setup(x => x.GetKeysAsync(Domain.DomainName)).ReturnsAsync([
            new DkimKey
            {
                Selector = "s1",
                Algorithm = DkimAlgorithm.Ed25519,
                PublicKey = new byte[32],
                PrivateKeyPkcs8 = [],
            },
        ]);

        await CreateService(tenantMailEnabled: true).GetDnsHealthAsync(Domain, CancellationToken.None);

        Assert.That(passedExtras, Is.Not.Null);
        Assert.That(passedExtras!.Count, Is.EqualTo(1));
        var record = passedExtras!.Single();
        Assert.That(record.Name, Does.Contain("_domainkey"));
        Assert.That(record.Type, Is.EqualTo("TXT"));
        // Load-bearing: IsDomainDnsReady drops Optional records before deciding, and
        // that verdict feeds the certificate DNS gate. A non-optional DKIM record would let
        // a missing DKIM TXT block certificate issuance.
        Assert.That(record.Optional, Is.True);
    }

    [Test]
    public async Task ItShouldCheckNoDkimWhenThereIsNothingToCheck()
    {
        // Tenant mail off — the overwhelmingly common case, and the store is never touched
        IReadOnlyCollection<DnsConfig>? passedExtras = null;
        SetupLookupCapturing(extras => passedExtras = extras);

        await CreateService().GetDnsHealthAsync(Domain, CancellationToken.None);

        Assert.That(passedExtras, Is.Empty);
        _dkimStore.Verify(x => x.GetKeysAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// A DKIM read failure must cost the DKIM block, not the whole panel: the owner still
    /// needs to see their A record, their CNAMEs and their DNSSEC state.
    /// </summary>
    [Test]
    public async Task ItShouldStillReportTheRestWhenDkimCannotBeRead()
    {
        SetupLookupCapturing(_ => { });
        _dkimStore.Setup(x => x.IsConfigured).Returns(true);
        _dkimStore.Setup(x => x.GetKeysAsync(Domain.DomainName)).ThrowsAsync(new Exception("store down"));

        var result = await CreateService(tenantMailEnabled: true).GetDnsHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Records, Is.Not.Empty);
    }

    private void SetupLookupCapturing(Action<IReadOnlyCollection<DnsConfig>> capture)
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("example.com");
        _dnsLookupService
            .Setup(x => x.GetAuthoritativeDomainDnsStatusAsync(
                Domain, It.IsAny<IReadOnlyCollection<DnsConfig>>(), It.IsAny<CancellationToken>()))
            .Callback<AsciiDomainName, IReadOnlyCollection<DnsConfig>, CancellationToken>(
                (_, extras, _) => capture(extras ?? []))
            // The extras come back graded IN PLACE, like the real lookup does: live, unless a
            // test says the relay's rows are not published yet
            .ReturnsAsync((AsciiDomainName _, IReadOnlyCollection<DnsConfig> extras, CancellationToken _) =>
            {
                foreach (var x in extras ?? [])
                {
                    x.Status = x.Name.Contains("934313") ? _relayRowGrade : DnsLookupRecordStatus.Success;
                }
                return (true, new List<DnsConfig> { new() { Type = "A", Name = "", Value = "127.0.0.1" } }
                    .Concat(extras ?? [])
                    .ToList());
            });
    }

    private DnsLookupRecordStatus _relayRowGrade = DnsLookupRecordStatus.Success;

    /// <summary>
    /// The monthly report's trigger. A broken mail record is otherwise silent: mail is
    /// refused or spam-foldered and the owner hears it from whoever stopped receiving it.
    /// </summary>
    [Test]
    public async Task ItShouldReportBrokenMailRecordsAsNeedingAttention()
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("example.com");
        _dnsLookupService
            .Setup(x => x.GetAuthoritativeDomainDnsStatusAsync(
                Domain, It.IsAny<IReadOnlyCollection<DnsConfig>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, new List<DnsConfig>
            {
                new() { Type = "A", Name = "", Value = "127.0.0.1", Status = DnsLookupRecordStatus.Success },
                new()
                {
                    Type = "TXT", Name = "s1._domainkey", Domain = "s1._domainkey.frodo.example.com",
                    Description = "DKIM key (ed25519)", Optional = true,
                    Status = DnsLookupRecordStatus.DomainOrRecordNotFound,
                },
                new()
                {
                    Type = "TXT", Name = "", Domain = "frodo.example.com",
                    Description = "SPF (authorized senders)", Optional = true,
                    Status = DnsLookupRecordStatus.Success,
                },
            }));

        var attention = await CreateService(tenantMailEnabled: true)
            .GetMailRecordAttentionAsync(Domain, CancellationToken.None);

        Assert.That(attention.Count, Is.EqualTo(1));
        Assert.That(attention[0], Does.Contain("DKIM key (ed25519)"));
        Assert.That(attention[0], Does.Contain("is missing"));
    }

    [Test]
    public async Task ItShouldNotReportMailAttentionWhenTenantMailIsOff()
    {
        var attention = await CreateService().GetMailRecordAttentionAsync(Domain, CancellationToken.None);
        Assert.That(attention, Is.Empty);
    }

    [Test]
    public async Task ItShouldSplitOptionalRecordsIntoMailRecords()
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("example.com");
        _dnsLookupService
            .Setup(x => x.GetAuthoritativeDomainDnsStatusAsync(Domain, It.IsAny<IReadOnlyCollection<DnsConfig>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, new List<DnsConfig>
            {
                new() { Type = "A", Name = "", Value = "127.0.0.1" },
                new() { Type = "CNAME", Name = "capi", Value = "alias.example" },
                new() { Type = "TXT", Name = "_dmarc", Value = "v=DMARC1; p=reject", Optional = true },
                new() { Type = "CNAME", Name = "mta-sts", Value = "alias.example", Optional = true },
            }));

        var result = await CreateService().GetDnsHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.RecordsAreValid, Is.True);
        Assert.That(result.Records.Select(r => r.Name), Is.EqualTo(new[] { "", "capi" }));
        Assert.That(result.Records.All(r => !r.Optional), Is.True);
        Assert.That(result.MailRecords.Select(r => r.Name), Is.EqualTo(new[] { "_dmarc", "mta-sts" }));
        Assert.That(result.MailRecords.All(r => r.Optional), Is.True);
    }

    //
    // Outbound relay verdict
    //

    private const string PlanLimitRefusal =
        "Relay: POST /domain/add returned 400: Free plans can add a maximum of 5 verified senders";

    /// <summary>
    /// One health call plus the monthly report's view of it, against a configured relay set up by
    /// <paramref name="setupRelay"/>. DKIM keys exist (= email activated) unless told otherwise:
    /// that is the only case in which the relay is supposed to know the domain at all.
    /// </summary>
    private async Task<(DnsHealthResult health, List<string> attention)> RelayHealthAsync(
        Action<Mock<IMailRelayProvider>> setupRelay,
        bool mailActivated = true,
        Action<IReadOnlyCollection<DnsConfig>>? captureExtras = null)
    {
        SetupLookupCapturing(captureExtras ?? (_ => { }));
        _dkimStore.Setup(x => x.IsConfigured).Returns(true);
        _dkimStore.Setup(x => x.GetKeysAsync(Domain.DomainName)).ReturnsAsync(mailActivated
            ? [new DkimKey { Selector = "s1", Algorithm = DkimAlgorithm.Ed25519, PublicKey = new byte[32], PrivateKeyPkcs8 = [] }]
            : []);

        _relay.Setup(x => x.IsConfigured).Returns(true);
        setupRelay(_relay);

        var service = CreateService(tenantMailEnabled: true, relayProvider: _relay.Object);
        return (await service.GetDnsHealthAsync(Domain, CancellationToken.None),
            await service.GetMailRecordAttentionAsync(Domain, CancellationToken.None));
    }

    private readonly Mock<IMailRelayProvider> _relay = new();

    /// <summary>
    /// The 2026-10-07 failure: the relay refused the domain, so it has no record of it and
    /// there were no relay rows to grade - every surface said healthy. The verdict must say
    /// NotRegistered, carry the relay's own refusal, and reach the monthly report.
    /// </summary>
    [Test]
    public async Task ItShouldReportADomainTheRelayRefusedAsNotRegisteredWithTheReason()
    {
        _relayFailureStore.Setup(x => x.GetAsync(Domain.DomainName)).ReturnsAsync(PlanLimitRefusal);

        var (health, attention) = await RelayHealthAsync(relay => relay
            .Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MailRelayDomainState?)null));

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.NotRegistered), $"relay was {health.Relay.Status}");
        Assert.That(health.Relay.LastError, Is.EqualTo(PlanLimitRefusal));
        Assert.That(health.Relay.NeedsAttention, Is.True);
        Assert.That(attention, Has.Some.Contains("Outbound sending is not set up").And.Some.Contains("maximum of 5"),
            $"attention was: [{string.Join(" | ", attention)}]");
    }

    /// <summary>
    /// An unreadable DKIM store proves nothing about activation. Assuming "never activated"
    /// would hide a refused domain behind "nothing to check" - the failure this exists to fix.
    /// </summary>
    [Test]
    public async Task ItShouldStillAskTheRelayWhenTheDkimStoreCannotBeRead()
    {
        var (health, _) = await RelayHealthAsync(relay => relay
            .Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MailRelayDomainState?)null));
        // RelayHealthAsync configured readable keys; break the store for this call
        _dkimStore.Setup(x => x.GetKeysAsync(Domain.DomainName)).ThrowsAsync(new InvalidOperationException("store down"));

        var again = await CreateService(tenantMailEnabled: true, relayProvider: _relay.Object)
            .GetDnsHealthAsync(Domain, CancellationToken.None);

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.NotRegistered));
        Assert.That(again.Relay.Status, Is.EqualTo(MailRelayHealthStatus.NotRegistered), $"relay was {again.Relay.Status}");
    }

    [Test]
    public async Task ItShouldNotAskTheRelayAboutATenantThatNeverActivatedEmail()
    {
        var (health, _) = await RelayHealthAsync(_ => { }, mailActivated: false);

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.NotApplicable), $"relay was {health.Relay.Status}");
        _relay.Verify(x => x.GetDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>"We could not ask" is not "it is broken": no attention, no owner to-do.</summary>
    [Test]
    public async Task ItShouldReportAnUnreachableRelayWithoutBlamingTheOwner()
    {
        var (health, attention) = await RelayHealthAsync(relay => relay
            .Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused")));

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.Unreachable), $"relay was {health.Relay.Status}");
        Assert.That(attention, Is.Empty, $"attention was: [{string.Join(" | ", attention)}]");
    }

    [Test]
    public async Task ItShouldCheckTheRelayRecordsOfARegisteredDomain()
    {
        IReadOnlyCollection<DnsConfig>? passedExtras = null;

        var (health, _) = await RelayHealthAsync(relay => relay
                .Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MailRelayDomainState
                {
                    Domain = Domain.DomainName,
                    Verified = true,
                    Records = [new DnsConfig { Type = "CNAME", Name = "em934313", Value = "return.smtp2go.net", Optional = true }],
                }),
            captureExtras: extras => passedExtras = extras);

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.Registered), $"relay was {health.Relay.Status}");
        Assert.That(passedExtras?.Select(x => x.Name), Has.Member("em934313"),
            $"extras were: [{string.Join(", ", passedExtras?.Select(x => x.Name) ?? [])}]");
    }

    private static readonly DnsConfig RelayRow = new()
    {
        Type = "CNAME", Name = "em934313", Domain = "em934313.frodo.example.com",
        Value = "return.smtp2go.net", Description = "Relay Return-Path CNAME (SPF)", Optional = true,
    };

    // A fresh row per state: the lookup grades rows in place, so tests must not share one
    private static MailRelayDomainState RelayState(bool verified, params string[] problems) => new()
    {
        Domain = Domain.DomainName,
        Verified = verified,
        Records =
        [
            new DnsConfig
            {
                Type = RelayRow.Type, Name = RelayRow.Name, Domain = RelayRow.Domain, Value = RelayRow.Value,
                Description = RelayRow.Description, Optional = true,
            },
        ],
        Problems = [..problems],
    };

    /// <summary>
    /// The relay still says what it said last time it looked, even though our live lookup
    /// finds its rows published. It is asked again, and the fresh answer is the verdict -
    /// so an owner who published the records by hand does not have to press anything.
    /// </summary>
    [Test]
    public async Task ItShouldAskTheRelayAgainWhenItsRowsAreLiveButItHasNotVerified()
    {
        // Like the real relay: its stored verdict changes once it has been asked to look again
        var verified = false;
        var (health, attention) = await RelayHealthAsync(relay =>
        {
            relay.Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => verified
                    ? RelayState(true)
                    : RelayState(false, "Lookup CNAME(em934313.frodo.example.com) failed: NXDOMAIN"));
            relay.Setup(x => x.VerifyDomainAsync(Domain, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    verified = true;
                    return RelayState(true);
                });
        });

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.Registered), $"relay was {health.Relay.Status}");
        Assert.That(attention, Is.Empty, $"attention was: [{string.Join(" | ", attention)}]");
    }

    /// <summary>Asking is rate-limited: a tab left open must not hammer the relay.</summary>
    [Test]
    public async Task ItShouldAskTheRelayAgainAtMostOncePerInterval()
    {
        var (health, attention) = await RelayHealthAsync(relay =>
        {
            relay.Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RelayState(false, "Lookup CNAME(em934313.frodo.example.com) failed: NXDOMAIN"));
            relay.Setup(x => x.VerifyDomainAsync(Domain, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RelayState(false, "still NXDOMAIN on their side"));
        });

        // RelayHealthAsync made two health calls (panel + monthly attention) on one service
        _relay.Verify(x => x.VerifyDomainAsync(Domain, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.Unverified), $"relay was {health.Relay.Status}");
        Assert.That(attention, Has.Some.Contains("NXDOMAIN"), $"attention was: [{string.Join(" | ", attention)}]");
    }

    [Test]
    public async Task ItShouldKeepTheStoredVerdictWhenAskingAgainFails()
    {
        var (health, _) = await RelayHealthAsync(relay =>
        {
            relay.Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RelayState(false, "Lookup CNAME(em934313.frodo.example.com) failed: NXDOMAIN"));
            relay.Setup(x => x.VerifyDomainAsync(Domain, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("relay down"));
        });

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.Unverified), $"relay was {health.Relay.Status}");
        Assert.That(health.Relay.Problem, Does.Contain("NXDOMAIN"), $"problem was '{health.Relay.Problem}'");
    }

    /// <summary>
    /// While the relay's rows are not published yet, those rows already say what is wrong. The
    /// relay's "not verified" would say it a second time, so it is not a problem of its own -
    /// and there is nothing for the relay to re-check yet.
    /// </summary>
    [Test]
    public async Task ItShouldNotRepeatWhatTheBrokenRelayRowsAlreadySay()
    {
        _relayRowGrade = DnsLookupRecordStatus.DomainOrRecordNotFound;

        var (health, attention) = await RelayHealthAsync(relay => relay
            .Setup(x => x.GetDomainAsync(Domain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RelayState(false, "Lookup CNAME(em934313.frodo.example.com) failed: NXDOMAIN")));

        Assert.That(health.Relay.Status, Is.EqualTo(MailRelayHealthStatus.Unverified), $"relay was {health.Relay.Status}");
        Assert.That(health.Relay.RecordsNotLiveYet, Is.True);
        Assert.That(health.Relay.Problem, Is.Null, $"problem was '{health.Relay.Problem}'");
        Assert.That(attention, Has.Count.EqualTo(1).And.Some.Contains("Relay Return-Path CNAME"),
            $"attention was: [{string.Join(" | ", attention)}]");
        _relay.Verify(x => x.VerifyDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    //
    // DNSSEC orchestration (mocked seams)
    //

    /// <summary>A zone nobody answered for is "could not tell", not a finding for the monthly report.</summary>
    [Test]
    public async Task ItShouldNotFlagAZoneWhoseKeysCouldNotBeLookedUp()
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<DnsKeyRecord>?)null);

        var service = CreateService();
        var health = await service.GetDnssecHealthAsync(Domain, CancellationToken.None);
        var attention = await service.GetDnssecAttentionAsync(Domain, CancellationToken.None);

        Assert.That(health.LookupFailed, Is.True, $"status {health.Status}, lookupFailed {health.LookupFailed}");
        Assert.That(attention, Is.Null, $"attention was {attention?.Status}");
    }

    /// <summary>
    /// An identity can sit inside the owner's own zone (home.example.org in example.org). That
    /// zone is neither ours to fix nor ours to log about, so it is not graded.
    /// </summary>
    [Test]
    public async Task ItShouldNotGradeAnEnclosingZoneThatIsNotOurs()
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("example.org");

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.Inherited), $"status was {result.Status}");
        Assert.That(result.EnclosingZoneStatus, Is.Null, $"enclosing graded {result.EnclosingZoneStatus}");
        _dnssecLookup.Verify(x => x.TryGetZoneDnsKeysAsync("example.org", It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Managed-domain case: frodo.example.com lives inside the example.com zone. "Inherited"
    /// used to be taken on trust; the enclosing zone is now graded with the same rules, so an
    /// unsigned or unanchored apex shows instead of hiding behind the word.
    /// </summary>
    [TestCase(false, false, DnsHealthDnssecStatus.ZoneUnsigned)]
    [TestCase(true, false, DnsHealthDnssecStatus.DsMissing)]
    [TestCase(true, true, DnsHealthDnssecStatus.Secure)]
    public async Task ItShouldReportInheritedAndGradeTheEnclosingZone(
        bool enclosingSigned, bool enclosingAnchored, DnsHealthDnssecStatus expectedEnclosing)
    {
        const string enclosing = "example.com";
        var key = TestKey(owner: "example.com.");
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enclosing);
        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(enclosing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enclosingSigned ? [key] : []);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(enclosing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(enclosing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enclosingAnchored ? [DnssecLookup.ComputeDsFromDnsKey(enclosing, key)] : []);

        var service = CreateService();
        var result = await service.GetDnssecHealthAsync(Domain, CancellationToken.None);
        var attention = await service.GetDnssecAttentionAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.Inherited),
            $"status was {result.Status}");
        Assert.That(result.EnclosingZone, Is.EqualTo(enclosing));
        Assert.That(result.EnclosingZoneStatus, Is.EqualTo(expectedEnclosing),
            $"enclosing zone graded {result.EnclosingZoneStatus}");
        // The enclosing zone is ours to fix, never the owner's monthly to-do
        Assert.That(attention, Is.Null, $"attention was {attention?.Status}");
    }

    [Test]
    public async Task ItShouldReportZoneUnsignedWhenNoDnsKeysAreServed()
    {
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.ZoneUnsigned));
    }

    [Test]
    public async Task ItShouldOfferComputedDsRecordsWhenOneIsMissing()
    {
        var key = TestKey();
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.DsMissing));
        // No CDS -> computed from the zone's public key, SHA-256
        var expected = DnssecLookup.ComputeDsFromDnsKey(Domain.DomainName, key);
        Assert.That(result.DsToPublish.Single(), Is.EqualTo(expected));
        Assert.That(result.ParentZoneSigned, Is.True);
    }

    [Test]
    public async Task ItShouldPreferPublishedCdsOverComputation()
    {
        var key = TestKey();
        var cds = new DsRecordData(60485, 5, 2, "aabbccdd");
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([cds]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.DsToPublish.Single(), Is.EqualTo(cds));
    }

    [Test]
    public async Task ItShouldReportDsMismatchForAStaleParentDs()
    {
        var key = TestKey();
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DsRecordData(9999, 13, 2, "deadbeef")]);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateService().GetDnssecHealthAsync(Domain, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(DnsHealthDnssecStatus.DsMismatch));
        Assert.That(result.ParentDsRecords.Single().KeyTag, Is.EqualTo(9999));
    }

    //
    // Optional www (mocked lookups)
    //

    private void SetupWwwLookups(CNameRecord[]? cnames = null, ARecord[]? aRecords = null)
    {
        var authority = new AuthoritativeDnsLookupResult
        {
            AuthoritativeDomain = Domain.DomainName,
            AuthoritativeNameServer = "ns1.example",
            NameServers = ["127.0.0.9"],
        };
        _authoritativeDnsLookup
            .Setup(x => x.LookupDomainAuthorityAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authority);

        _dnsClient
            .Setup(c => c.QueryServerAsync(
                It.IsAny<IReadOnlyCollection<NameServer>>(),
                It.Is<DnsQuestion>(q => q.QuestionType == QueryType.CNAME),
                It.IsAny<DnsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(cnames ?? []));
        _dnsClient
            .Setup(c => c.QueryServerAsync(
                It.IsAny<IReadOnlyCollection<NameServer>>(),
                It.Is<DnsQuestion>(q => q.QuestionType == QueryType.A),
                It.IsAny<DnsQueryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(aRecords ?? []));
    }

    private static IDnsQueryResponse Response(DnsResourceRecord[] answers)
    {
        var mock = new Mock<IDnsQueryResponse>();
        mock.SetupGet(x => x.HasError).Returns(false);
        mock.SetupGet(x => x.Answers).Returns(answers);
        mock.SetupGet(x => x.Authorities).Returns([]);
        mock.SetupGet(x => x.NameServer).Returns(new NameServer(System.Net.IPAddress.Loopback));
        return mock.Object;
    }

    private static CNameRecord Cname(string owner, string target)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.CNAME, QueryClass.IN, 3600, 0);
        return new CNameRecord(info, DnsString.Parse(target));
    }

    private static ARecord A(string owner, string ip)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.A, QueryClass.IN, 3600, 0);
        return new ARecord(info, System.Net.IPAddress.Parse(ip));
    }

    [Test]
    public async Task ItShouldReportWwwNotSetAsAnUnremarkableState()
    {
        SetupWwwLookups();

        var result = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);

        Assert.That(result.Single().Status, Is.EqualTo(OptionalRecordStatus.NotSet));
        Assert.That(result.Single().Domain, Is.EqualTo("www.frodo.example.com"));
    }

    [Test]
    public async Task ItShouldAcceptWwwPointingAtTheIdentityByCnameOrARecord()
    {
        // CNAME to the domain itself
        SetupWwwLookups(cnames: [Cname("www.frodo.example.com.", "frodo.example.com.")]);
        var byDomainCname = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);
        Assert.That(byDomainCname.Single().Status, Is.EqualTo(OptionalRecordStatus.Success));

        // CNAME to the same alias the apex uses
        SetupWwwLookups(cnames: [Cname("www.frodo.example.com.", "identity-host.example.")]);
        var byAliasCname = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);
        Assert.That(byAliasCname.Single().Status, Is.EqualTo(OptionalRecordStatus.Success));

        // A record matching the apex A
        SetupWwwLookups(aRecords: [A("www.frodo.example.com.", "131.164.170.62")]);
        var byARecord = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);
        Assert.That(byARecord.Single().Status, Is.EqualTo(OptionalRecordStatus.Success));
    }

    [Test]
    public async Task ItShouldReportADeliberateSeparateWwwSiteAsPointsElsewhere()
    {
        SetupWwwLookups(cnames: [Cname("www.frodo.example.com.", "some-other-site.example.")]);

        var result = await CreateService().CheckOptionalWwwAsync(Domain, CancellationToken.None);

        Assert.That(result.Single().Status, Is.EqualTo(OptionalRecordStatus.PointsElsewhere));
        Assert.That(result.Single().Found, Is.EquivalentTo(new[] { "some-other-site.example" }));
    }

    //
    // Security-email attention rule (docs/owner-console-dnssec-panel-plan.md section 3b)
    //

    /// <summary>
    /// Since 2026-10-07 every unanchored state needs the owner - including ParentUnsigned and
    /// ZoneUnsigned, which used to be "that is fine". Only an anchored chain (Secure) and a
    /// domain inside our own zone (Inherited) stay quiet.
    /// </summary>
    [TestCase(DnsHealthDnssecStatus.DsMismatch, true)]
    [TestCase(DnsHealthDnssecStatus.DsMissing, true)]
    [TestCase(DnsHealthDnssecStatus.ParentUnsigned, true)]
    [TestCase(DnsHealthDnssecStatus.ZoneUnsigned, true)]
    [TestCase(DnsHealthDnssecStatus.Secure, false)]
    [TestCase(DnsHealthDnssecStatus.Inherited, false)]
    public void ItShouldFlagEveryUnanchoredDnssecState(DnsHealthDnssecStatus status, bool expected)
    {
        var needsAttention = new DnssecHealthResult { Status = status }.NeedsAttention;
        Assert.That(needsAttention, Is.EqualTo(expected), $"{status} -> {needsAttention}");
    }

    [Test]
    public async Task ItShouldReturnNullAttentionOnLookupFailure()
    {
        // A DNS hiccup must neither block the health report nor count as attention
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("dns exploded"));

        var result = await CreateService().GetDnssecAttentionAsync(Domain, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ItShouldReturnAttentionForAStaleDsAndStayQuietWhenSecure()
    {
        var key = TestKey();
        _authoritativeDnsLookup
            .Setup(x => x.LookupZoneApexAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Domain.DomainName);
        _dnssecLookup
            .Setup(x => x.TryGetZoneDnsKeysAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([key]);
        _dnssecLookup
            .Setup(x => x.IsParentZoneSignedAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dnssecLookup
            .Setup(x => x.GetCdsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Stale DS -> attention with the expected DS values on board
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DsRecordData(9999, 13, 2, "deadbeef")]);
        var mismatch = await CreateService().GetDnssecAttentionAsync(Domain, CancellationToken.None);
        Assert.That(mismatch, Is.Not.Null);
        Assert.That(mismatch!.Status, Is.EqualTo(DnsHealthDnssecStatus.DsMismatch));
        Assert.That(mismatch.DsToPublish, Is.Not.Empty);

        // Matching DS -> quiet
        _dnssecLookup
            .Setup(x => x.GetParentDsRecordsAsync(Domain.DomainName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([DnssecLookup.ComputeDsFromDnsKey(Domain.DomainName, key)]);
        var secure = await CreateService().GetDnssecAttentionAsync(Domain, CancellationToken.None);
        Assert.That(secure, Is.Null);
    }
}
