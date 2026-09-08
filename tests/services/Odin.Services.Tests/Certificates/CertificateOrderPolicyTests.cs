using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Util;
using Odin.Services.Background;
using Odin.Services.Background.BackgroundServices.System;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Certificates;

#nullable enable

/// <summary>
/// The policy decisions that stand between a tenant and a certificate: which names a refusal
/// actually implicates, and how long to wait before asking the CA again.
/// </summary>
public class CertificateOrderPolicyTests
{
    private const string Domain = "delete.n1.id.pub";
    private const string MtaSts = "mta-sts.delete.n1.id.pub";
    private static readonly string[] OptionalSans = [MtaSts];
    private static readonly string[] AllSans = ["capi.delete.n1.id.pub", "file.delete.n1.id.pub", MtaSts];

    // The real thing, from the 2026-09-08 incident. Note it names the optional SAN only.
    private const string RateLimitedDetail =
        "urn:ietf:params:acme:error:rateLimited: too many failed authorizations (5) for " +
        "\"mta-sts.delete.n1.id.pub\" in the last 1h0m0s, retry after 2026-09-08 19:16:26 UTC";

    private ICertesAcme _certesAcme = null!;
    private ICertificateStore _certificateStore = null!;
    private IDnsLookupService _dnsLookupService = null!;
    private IBackgroundServiceNotifier<UpdateCertificatesBackgroundService> _notifier = null!;
    private CertificateService _certificateService = null!;

    [SetUp]
    public void Setup()
    {
        _certesAcme = Substitute.For<ICertesAcme>();
        _certificateStore = Substitute.For<ICertificateStore>();
        _dnsLookupService = Substitute.For<IDnsLookupService>();
        _notifier = Substitute.For<IBackgroundServiceNotifier<UpdateCertificatesBackgroundService>>();

        _certificateService = new CertificateService(
            NullLogger<CertificateService>.Instance,
            new NodeLock(),
            _certificateStore,
            _certesAcme,
            _dnsLookupService,
            new AcmeAccountConfig(),
            Substitute.For<IServiceProvider>(),
            new OdinConfiguration(),
            _notifier);
    }

    //
    // Name matching. A plain Contains is wrong here in the direction that matters: every SAN
    // has the apex as a suffix, so a complaint about mta-sts.<apex> would read as a complaint
    // about <apex> too and switch off the fallback in exactly the case it exists for.
    //

    [Test]
    public void MentionsName_DoesNotMatchTheApexInsideItsOwnSubdomain()
    {
        Assert.That(CertificateService.MentionsName(RateLimitedDetail, MtaSts), Is.True,
            "The optional SAN is the name the CA actually refused");
        Assert.That(CertificateService.MentionsName(RateLimitedDetail, Domain), Is.False,
            "The apex appears only as a suffix of the optional SAN, and must not count as refused");
    }

    [Test]
    [TestCase("failed for delete.n1.id.pub", true)]
    [TestCase("failed for delete.n1.id.pub.", true)]
    [TestCase("failed for \"delete.n1.id.pub\"", true)]
    [TestCase("failed for xdelete.n1.id.pub", false)]
    [TestCase("failed for delete.n1.id.public", false)]
    [TestCase("failed for capi.delete.n1.id.pub", false)]
    [TestCase("", false)]
    public void MentionsName_RespectsLabelBoundaries(string message, bool expected)
    {
        Assert.That(CertificateService.MentionsName(message, Domain), Is.EqualTo(expected));
    }

    //
    // Which names a refusal implicates
    //

    [Test]
    public void RefusedOnlyOptionalNames_TrueWhenTheCaNamesOnlyAnOptionalSan()
    {
        var e = new AcmeOrderException("Failed validating", [MtaSts]);
        Assert.That(_certificateService.RefusedOnlyOptionalNames(e, OptionalSans, Domain, AllSans), Is.True);
    }

    [Test]
    public void RefusedOnlyOptionalNames_FalseWhenARequiredNameIsAlsoRefused()
    {
        var e = new AcmeOrderException("Failed validating", [MtaSts, "capi.delete.n1.id.pub"]);
        Assert.That(_certificateService.RefusedOnlyOptionalNames(e, OptionalSans, Domain, AllSans), Is.False,
            "Dropping the optional names would not help - the order fails either way");
    }

    [Test]
    public void RefusedOnlyOptionalNames_FallsBackToTheDetailTextWhenThereAreNoSubproblems()
    {
        // A top-level rateLimited problem carries no sub-problems, so the only signal is the
        // hostname Let's Encrypt writes into the detail. This is the live incident's shape.
        var e = new AcmeRateLimitedException(RateLimitedDetail, TimeSpan.FromHours(1));

        Assert.That(e.FailedIdentifiers, Is.Empty);
        Assert.That(_certificateService.RefusedOnlyOptionalNames(e, OptionalSans, Domain, AllSans), Is.True);
    }

    [Test]
    public void RefusedOnlyOptionalNames_FalseWhenNothingIsImplicated()
    {
        var e = new AcmeOrderException("the server experienced an internal error");
        Assert.That(_certificateService.RefusedOnlyOptionalNames(e, OptionalSans, Domain, AllSans), Is.False);
    }

    [Test]
    public void RefusedOnlyOptionalNames_FalseWhenThereAreNoOptionalSans()
    {
        var e = new AcmeOrderException("Failed validating", [Domain]);
        Assert.That(_certificateService.RefusedOnlyOptionalNames(e, [], Domain, AllSans), Is.False);
    }

    //
    // Backoff schedule. Let's Encrypt allows five failed authorizations per hostname per hour,
    // so a flat five-minute wait (twelve attempts an hour) would breach the limit it protects.
    //

    [Test]
    public void ExponentialBackoff_StaysWithinFiveAttemptsPerHour()
    {
        Assert.That(CertificateService.ExponentialBackoff(1), Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(CertificateService.ExponentialBackoff(2), Is.EqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(CertificateService.ExponentialBackoff(3), Is.EqualTo(TimeSpan.FromMinutes(20)));
        Assert.That(CertificateService.ExponentialBackoff(4), Is.EqualTo(TimeSpan.FromMinutes(40)));
        Assert.That(CertificateService.ExponentialBackoff(5), Is.EqualTo(TimeSpan.FromHours(1)));

        // A long-failing domain must not overflow its way back to a short wait
        Assert.That(CertificateService.ExponentialBackoff(1000), Is.EqualTo(TimeSpan.FromHours(1)));

        var total = TimeSpan.Zero;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            total += CertificateService.ExponentialBackoff(attempt);
        }
        Assert.That(total, Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(60)),
            "Four consecutive failures must span at least an hour");
    }

    //
    // Suppression: having dropped an optional name, we must not renew straight back into the
    // same refusal. Let's Encrypt allows five duplicate certificates per week; a 12h sweep
    // that re-added the SAN, failed, dropped it and reissued would mint fourteen.
    //

    [Test]
    public void OptionalSans_AreNotSuppressedForAnUntouchedDomain()
    {
        Assert.That(_certificateService.AreOptionalSansSuppressed(Domain), Is.False);
    }

    //
    // The request path
    //

    [Test]
    public async Task RequestIssuanceAsync_PulsesTheBackgroundIssuerAndOrdersNothingItself()
    {
        await _certificateService.RequestIssuanceAsync(Domain);

        await _notifier.Received(1).NotifyWorkAvailableAsync(Arg.Any<string?>());
        await _certesAcme.DidNotReceiveWithAnyArgs().CreateCertificateAsync(default!, default!, default);
    }

    [Test]
    public async Task RequestIssuanceAsync_StaysQuietWhileTheDomainIsInBackoff()
    {
        // Drive the service into backoff through the DNS gate
        _dnsLookupService
            .GetAuthoritativeDomainDnsStatusAsync(
                Arg.Any<AsciiDomainName>(), Arg.Any<IReadOnlyCollection<DnsConfig>?>(), Arg.Any<CancellationToken>())
            .Returns((false, new List<DnsConfig>()));

        await _certificateService.CreateCertificateAsync(Domain, ["capi." + Domain]);
        _notifier.ClearReceivedCalls();

        for (var i = 0; i < 5; i++)
        {
            await _certificateService.RequestIssuanceAsync(Domain);
        }

        await _notifier.DidNotReceiveWithAnyArgs().NotifyWorkAvailableAsync(default);
    }
}
