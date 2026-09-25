using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Util;
using Odin.Core.X509;
using Odin.Services.Background;
using Odin.Services.Background.BackgroundServices.System;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Certificates;

#nullable enable

/// <summary>
/// These cover the behaviour that keeps <see cref="CertificateService.CreateCertificateAsync"/>
/// safe to call from the TLS handshake path: it must never wait, and a domain that cannot get a
/// certificate must not start a fresh ACME order on every inbound connection.
/// </summary>
public class CertificateServiceContentionTests
{
    private INodeLock _nodeLock = null!;
    private ICertificateStore _certificateStore = null!;
    private ICertesAcme _certesAcme = null!;
    private IDnsLookupService _dnsLookupService = null!;
    private CertificateService _certificateService = null!;

    [SetUp]
    public void Setup()
    {
        _nodeLock = new NodeLock();
        _certificateStore = Substitute.For<ICertificateStore>();
        _certesAcme = Substitute.For<ICertesAcme>();
        _dnsLookupService = Substitute.For<IDnsLookupService>();

        _certificateService = new CertificateService(
            NullLogger<CertificateService>.Instance,
            _nodeLock,
            _certificateStore,
            _certesAcme,
            _dnsLookupService,
            new AcmeAccountConfig(),
            Substitute.For<IServiceProvider>(),
            new OdinConfiguration(),
            Substitute.For<IBackgroundServiceNotifier<UpdateCertificatesBackgroundService>>());
    }

    //

    [Test]
    public async Task CreateCertificateAsync_WaitsForAnOrderAlreadyInProgress()
    {
        const string domain = "contended.example.com";

        // Take the same lock the certificate service uses, as another thread or node would.
        // NodeLock has no timeout, so the contended call parks until we release it - which is
        // acceptable now that both callers are background work and nothing is on a request path.
        await using var held = await _nodeLock.LockAsync(NodeLockKey.Create("CertificateServiceLock:" + domain));

        var contended = _certificateService.CreateCertificateAsync(domain, [$"capi.{domain}"]);
        var completed = await Task.WhenAny(contended, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.That(completed, Is.Not.SameAs(contended), "The second caller must wait for the order in progress");

        // No order was attempted, and no failure was recorded: this is contention, not a fault
        await _certesAcme.DidNotReceiveWithAnyArgs()
            .CreateCertificateAsync(default!, default!, default);
        await _certificateStore.DidNotReceiveWithAnyArgs()
            .StoreFailedCertificateUpdateAsync(default!, default!);
    }

    //

    [Test]
    public async Task CreateCertificateAsync_DoesNotReorder_AfterAFailedAttempt()
    {
        const string domain = "bad-dns.example.com";
        string[] sans = [$"capi.{domain}"];

        // The incident shape: DNS is not (yet) correct, so the order can only fail
        _dnsLookupService
            .GetAuthoritativeDomainDnsStatusAsync(
                Arg.Any<AsciiDomainName>(), Arg.Any<IReadOnlyCollection<DnsConfig>?>(), Arg.Any<CancellationToken>())
            .Returns((false, new List<DnsConfig>()));

        var first = await _certificateService.CreateCertificateAsync(domain, sans);
        Assert.That(first, Is.Null);
        await _certificateStore.Received(1).StoreFailedCertificateUpdateAsync(domain, Arg.Any<string>());

        // Every subsequent connection during the backoff window must be a no-op: no lock, no DNS
        // lookup, no ACME order. This is what stops one un-issuable domain from burning the CA's
        // per-hostname failed-authorization allowance.
        _dnsLookupService.ClearReceivedCalls();

        for (var i = 0; i < 5; i++)
        {
            Assert.That(await _certificateService.CreateCertificateAsync(domain, sans), Is.Null);
        }

        await _dnsLookupService.DidNotReceiveWithAnyArgs()
            .GetAuthoritativeDomainDnsStatusAsync(default!, default, default);
        await _certificateStore.Received(1).StoreFailedCertificateUpdateAsync(domain, Arg.Any<string>());
        await _certesAcme.DidNotReceiveWithAnyArgs()
            .CreateCertificateAsync(default!, default!, default);
    }

    //

    [Test]
    public async Task CreateCertificateAsync_LeavesTheLockFree_AfterAFailedAttempt()
    {
        const string domain = "bad-dns.example.com";

        _dnsLookupService
            .GetAuthoritativeDomainDnsStatusAsync(
                Arg.Any<AsciiDomainName>(), Arg.Any<IReadOnlyCollection<DnsConfig>?>(), Arg.Any<CancellationToken>())
            .Returns((false, new List<DnsConfig>()));

        await _certificateService.CreateCertificateAsync(domain, [$"capi.{domain}"]);

        // A failing order must not leave the domain locked behind it. NodeLock.LockAsync has no
        // timeout, so a leaked lock shows up as this call never returning.
        var reacquire = _nodeLock.LockAsync(NodeLockKey.Create("CertificateServiceLock:" + domain));
        var completed = await Task.WhenAny(reacquire, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(completed, Is.SameAs(reacquire), "The order lock was still held after a failed order");
        await (await reacquire).DisposeAsync();
    }

    //

    // #1747: the re-check after the lock must read the database, not this node's cache
    [Test]
    public async Task RenewIfAboutToExpireAsync_DoesNotReorder_WhenAnotherNodeAlreadyRenewed()
    {
        const string domain = "renewed-elsewhere.example.com";

        using var aboutToExpire = AboutToExpire(domain);
        using var renewedByOtherNode = X509Extensions.CreateSelfSignedEcDsaCertificate(
            domain, DateTimeOffset.Now - TimeSpan.FromMinutes(1), DateTimeOffset.Now + TimeSpan.FromDays(90));

        // This node's cache is stale; the database has what the other node wrote.
        _certificateStore.GetCertificateAsync(domain).Returns(aboutToExpire);
        _certificateStore.ReloadCertificateAsync(domain).Returns(renewedByOtherNode);

        var renewed = await _certificateService.RenewIfAboutToExpireAsync(domain);

        Assert.That(renewed, Is.False, "Nothing should have been renewed here: the other node already did it");

        // No order, and no attempt at one - see the control test below for what an attempt looks like
        await _certesAcme.DidNotReceiveWithAnyArgs()
            .CreateCertificateAsync(default!, default!, default);
        await _certificateStore.DidNotReceiveWithAnyArgs()
            .StoreFailedCertificateUpdateAsync(default!, default!);
        await _certificateStore.Received(1).ReloadCertificateAsync(domain);
    }

    //

    // The control for the test above: when the database agrees the certificate is about to
    // expire, the renewal must go ahead. The substituted IServiceProvider cannot load an ACME
    // account, so "went ahead" shows up here as a recorded failure rather than an order.
    [Test]
    public async Task RenewIfAboutToExpireAsync_GoesAhead_WhenTheDatabaseAlsoSaysAboutToExpire()
    {
        const string domain = "still-expiring.example.com";

        using var aboutToExpire = AboutToExpire(domain);

        _certificateStore.GetCertificateAsync(domain).Returns(aboutToExpire);
        _certificateStore.ReloadCertificateAsync(domain).Returns(aboutToExpire);

        var renewed = await _certificateService.RenewIfAboutToExpireAsync(domain);

        Assert.That(renewed, Is.False);
        await _certificateStore.Received(1).StoreFailedCertificateUpdateAsync(domain, Arg.Any<string>());
        await _certificateStore.Received(1).ReloadCertificateAsync(domain);
    }

    //

    private static X509Certificate2 AboutToExpire(string domain)
    {
        return X509Extensions.CreateSelfSignedEcDsaCertificate(
            domain, DateTimeOffset.Now - TimeSpan.FromDays(89), DateTimeOffset.Now + TimeSpan.FromDays(1));
    }
}
