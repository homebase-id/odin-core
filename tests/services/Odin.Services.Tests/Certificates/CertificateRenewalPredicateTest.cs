using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Util;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Certificates;

#nullable enable

// The renewal predicate: expiry is the normal trigger; a tenant cert missing the mta-sts
// SAN renews early ONLY when tenant mail is on AND the record resolves - the DNS gate is
// what prevents an endless renew loop (and CA rate limiting) for manual-records tenants
// that never created the record.
public class CertificateRenewalPredicateTest
{
    private const string Domain = "frodo.example.com";
    private static readonly string[] TenantSans = [$"capi.{Domain}", $"file.{Domain}"];

    private readonly Mock<IDnsLookupService> _dnsLookupService = new();

    [SetUp]
    public void SetUp()
    {
        _dnsLookupService.Reset();
    }

    private CertificateService CreateService(bool tenantMailEnabled)
    {
        var configuration = new OdinConfiguration
        {
            Email = new OdinConfiguration.EmailSection
            {
                TenantMail = new OdinConfiguration.TenantMailSection { Enabled = tenantMailEnabled },
            }
        };
        return new CertificateService(
            new Mock<ILogger<CertificateService>>().Object,
            new Mock<INodeLock>().Object,
            new Mock<ICertificateStore>().Object,
            new Mock<ICertesAcme>().Object,
            _dnsLookupService.Object,
            new AcmeAccountConfig(),
            new Mock<IServiceProvider>().Object,
            configuration);
    }

    private static X509Certificate2 Cert(string[] sans, TimeSpan validFor)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName($"CN={Domain}"), ecdsa, HashAlgorithmName.SHA256);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Domain);
        foreach (var san in sans)
        {
            sanBuilder.AddDnsName(san);
        }
        request.CertificateExtensions.Add(sanBuilder.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow + validFor);
    }

    private void SetupMtaStsRecordStatus(DnsLookupRecordStatus status)
    {
        var dnsConfigs = new List<DnsConfig>
        {
            new() { Type = "A", Status = DnsLookupRecordStatus.Success },
            new()
            {
                Type = "CNAME",
                Name = DnsConfigurationSet.PrefixMtaSts,
                Domain = $"{DnsConfigurationSet.PrefixMtaSts}.{Domain}",
                Optional = true,
                Status = status,
            },
        };
        _dnsLookupService
            .Setup(x => x.GetAuthoritativeDomainDnsStatusAsync(It.IsAny<AsciiDomainName>(), It.IsAny<IReadOnlyCollection<DnsConfig>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, dnsConfigs));
    }

    [Test]
    public async Task ItShouldNotRenewAFreshCertWhileTenantMailIsOff()
    {
        var result = await CreateService(tenantMailEnabled: false)
            .NeedsRenewalAsync(Domain, Cert(TenantSans, TimeSpan.FromDays(60)), TenantSans, CancellationToken.None);

        Assert.That(result, Is.False);
        _dnsLookupService.VerifyNoOtherCalls(); // steady state must not touch DNS
    }

    [Test]
    public async Task ItShouldRenewWhenAboutToExpireRegardlessOfSans()
    {
        var result = await CreateService(tenantMailEnabled: true)
            .NeedsRenewalAsync(Domain, Cert(TenantSans, TimeSpan.FromDays(3)), TenantSans, CancellationToken.None);

        Assert.That(result, Is.True);
        _dnsLookupService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ItShouldNotRenewWhenTheCertAlreadyCarriesTheMtaStsSan()
    {
        var sansWithMtaSts = new[] { $"capi.{Domain}", $"file.{Domain}", $"mta-sts.{Domain}" };

        var result = await CreateService(tenantMailEnabled: true)
            .NeedsRenewalAsync(Domain, Cert(sansWithMtaSts, TimeSpan.FromDays(60)), TenantSans, CancellationToken.None);

        Assert.That(result, Is.False);
        _dnsLookupService.VerifyNoOtherCalls(); // steady state must not touch DNS
    }

    [Test]
    public async Task ItShouldRenewEarlyOnceTheMtaStsRecordResolves()
    {
        SetupMtaStsRecordStatus(DnsLookupRecordStatus.Success);

        var result = await CreateService(tenantMailEnabled: true)
            .NeedsRenewalAsync(Domain, Cert(TenantSans, TimeSpan.FromDays(60)), TenantSans, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ItShouldNotRenewWhileTheMtaStsRecordDoesNotResolve()
    {
        // Manual-records tenant without the record: renewing would just omit the SAN
        // again - an endless renew loop against the CA
        SetupMtaStsRecordStatus(DnsLookupRecordStatus.DomainOrRecordNotFound);

        var result = await CreateService(tenantMailEnabled: true)
            .NeedsRenewalAsync(Domain, Cert(TenantSans, TimeSpan.FromDays(60)), TenantSans, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ItShouldNeverAddOptionalSansToSystemDomainCerts()
    {
        // sans.Length == 0 = system domain (provisioning, admin)
        var result = await CreateService(tenantMailEnabled: true)
            .NeedsRenewalAsync("provisioning.example.com", Cert([], TimeSpan.FromDays(60)), [], CancellationToken.None);

        Assert.That(result, Is.False);
        _dnsLookupService.VerifyNoOtherCalls();
    }
}
