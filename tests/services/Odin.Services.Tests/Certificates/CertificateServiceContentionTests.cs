using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public async Task CreateCertificateAsync_ReturnsImmediately_WhenAnOrderIsAlreadyInProgress()
    {
        const string domain = "contended.example.com";

        // Take the same lock the certificate service uses, as another thread or node would
        await using var _ = await _nodeLock.LockAsync(NodeLockKey.Create("CertificateServiceLock:" + domain));

        var sw = Stopwatch.StartNew();
        var certificate = await _certificateService.CreateCertificateAsync(domain, [$"capi.{domain}"]);
        sw.Stop();

        Assert.That(certificate, Is.Null);
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000),
            "A contended certificate lock must not stall the TLS handshake path");

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

        // A failing order must not leave the domain locked behind it
        await using var handle = await _nodeLock.TryLockAsync(NodeLockKey.Create("CertificateServiceLock:" + domain));
        Assert.That(handle, Is.Not.Null);
    }
}
