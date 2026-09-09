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
/// The policy decisions that stand between a tenant and a certificate: how long to wait before
/// asking the CA again, and what the request path is allowed to do.
/// </summary>
public class CertificateOrderPolicyTests
{
    private const string Domain = "delete.n1.id.pub";

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
    // Backoff schedules. Let's Encrypt allows five failed authorizations per hostname per hour,
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

    [Test]
    public void TransientBackoff_IsShortButStillEscalates()
    {
        // A transient failure never spent CA allowance, so it waits seconds - but a CA that is
        // down for an hour must not be asked sixty times.
        Assert.That(CertificateService.TransientBackoff(1), Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(CertificateService.TransientBackoff(2), Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(CertificateService.TransientBackoff(3), Is.EqualTo(TimeSpan.FromMinutes(2)));
        Assert.That(CertificateService.TransientBackoff(4), Is.EqualTo(TimeSpan.FromMinutes(4)));
        Assert.That(CertificateService.TransientBackoff(5), Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(CertificateService.TransientBackoff(1000), Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void AcmeTransientException_IsNotAnOrderFailure()
    {
        Assert.That(new AcmeTransientException("badNonce"), Is.Not.InstanceOf<AcmeOrderException>(),
            "A transient hiccup is not a verdict on the order and must not be treated as one");
    }

    //
    // The request path
    //

    [Test]
    public async Task RequestIssuanceAsync_PulsesTheBackgroundIssuerAndOrdersNothingItself()
    {
        Assert.That(await _certificateService.RequestIssuanceAsync(Domain), Is.True);

        await _certesAcme.DidNotReceiveWithAnyArgs().CreateCertificateAsync(default!, default!, default);
    }

    [Test]
    public async Task RequestIssuanceAsync_DoesNotBlockWhenTheIssuerIsUnreachable()
    {
        // NotifyWorkAvailableAsync waits up to 30s for the background service and then throws
        // when it never appears (SystemBackgroundServicesEnabled = false). The handshake path
        // must not wear that, and must not fault on it either.
        _notifier.NotifyWorkAvailableAsync(Arg.Any<string?>())
            .Returns(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                throw new InvalidOperationException("Background service not found");
            });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.That(await _certificateService.RequestIssuanceAsync(Domain), Is.True);
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000),
            "Requesting issuance must never wait on the background issuer");
    }

    [Test]
    public async Task RequestIssuanceAsync_IsRateLimited()
    {
        // The pulse wakes a whole-registry sweep, so back-to-back requests must coalesce
        Assert.That(await _certificateService.RequestIssuanceAsync(Domain), Is.True);

        for (var i = 0; i < 20; i++)
        {
            Assert.That(await _certificateService.RequestIssuanceAsync(Domain), Is.False);
        }
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
