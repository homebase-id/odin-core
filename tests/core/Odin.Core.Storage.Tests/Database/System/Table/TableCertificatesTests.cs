using System;
using System.Threading.Tasks;
using Autofac;
using DnsClient;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.System.Table;

public class TableCertificatesTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldCreateCertificate(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var certificates = scope.Resolve<TableCertificates>();

        var odinId = new OdinId("frodos.joint");
        var record = new CertificatesRecord
        {
            domain = odinId,
            privateKey = "adsadasdsadsadasdsadasdasdasdsad",
            certificate = "asdsadsadsadasdasdasdasdasdasdasdasd",
            expiration = UnixTimeUtc.Now(),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = "correlationContext.Id",
            lastError = null
        };

        await certificates.UpsertAsync(record);

        var copy = await certificates.GetAsync(odinId);
        Assert.That(copy, Is.Not.Null);
        Assert.That(copy.domain, Is.EqualTo(odinId));
    }
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldFailCertificateNonExistingCertificate(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var certificates = scope.Resolve<TableCertificates>();

        var odinId = new OdinId("frodos.joint");
        var lastAttempt = UnixTimeUtc.Now();
        await certificates.FailCertificateUpdate(odinId, lastAttempt, "correlation-context", "some error");

        var copy = await certificates.GetAsync(odinId);
        Assert.That(copy, Is.Not.Null);
        Assert.That(copy.domain, Is.EqualTo(odinId));
        Assert.That(copy.privateKey, Is.EqualTo(""));
        Assert.That(copy.certificate, Is.EqualTo(""));
        Assert.That(copy.lastAttempt, Is.EqualTo(lastAttempt));
        Assert.That(copy.correlationId, Is.EqualTo("correlation-context"));
        Assert.That(copy.lastError, Is.EqualTo("some error"));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldFailCertificateExistingCertificate(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var certificates = scope.Resolve<TableCertificates>();

        var odinId = new OdinId("frodos.joint");
        var record = new CertificatesRecord
        {
            domain = odinId,
            privateKey = "adsadasdsadsadasdsadasdasdasdsad",
            certificate = "asdsadsadsadasdasdasdasdasdasdasdasd",
            expiration = UnixTimeUtc.Now(),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = "correlationContext.Id",
            lastError = null
        };

        await certificates.UpsertAsync(record);

        var lastAttempt = UnixTimeUtc.Now();
        await certificates.FailCertificateUpdate(odinId, lastAttempt, "correlation-context", "some error");

        var copy = await certificates.GetAsync(odinId);
        Assert.That(copy, Is.Not.Null);
        Assert.That(copy.domain, Is.EqualTo(odinId));
        Assert.That(copy.privateKey, Is.EqualTo(record.privateKey));
        Assert.That(copy.certificate, Is.EqualTo(record.certificate));
        Assert.That(copy.expiration, Is.EqualTo(record.expiration));
        Assert.That(copy.lastAttempt, Is.EqualTo(lastAttempt));
        Assert.That(copy.correlationId, Is.EqualTo("correlation-context"));
        Assert.That(copy.lastError, Is.EqualTo("some error"));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldPageByRowId(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var certificates = scope.Resolve<TableCertificates>();

        for (int i = 0; i < 3; i++)
        {
            var record = new CertificatesRecord
            {
                domain = new OdinId($"domain{i}.test"),
                privateKey = $"pk{i}",
                certificate = $"cert{i}",
                expiration = UnixTimeUtc.Now(),
                lastAttempt = UnixTimeUtc.Now(),
                correlationId = $"corr{i}",
                lastError = null
            };
            await certificates.UpsertAsync(record);
        }

        var (page1, cursor1) = await certificates.PagingByRowIdAsync(2, null);
        Assert.That(page1.Count, Is.EqualTo(2));
        Assert.That(cursor1, Is.Not.Null);

        var (page2, cursor2) = await certificates.PagingByRowIdAsync(2, cursor1);
        Assert.That(page2.Count, Is.EqualTo(1));
        Assert.That(cursor2, Is.Null);

        var (all, allCursor) = await certificates.PagingByRowIdAsync(100, null);
        Assert.That(all.Count, Is.EqualTo(3));
        Assert.That(allCursor, Is.Null);
    }
}