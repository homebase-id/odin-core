#if RUN_POSTGRES_TESTS
using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests._Universal.Registry;

/// <summary>
/// Deleting an identity removes its rows from the identity database (#1792). On PostgreSQL every identity shares
/// one database, so deleting the tenant folder -- all that happens for SQLite, where the database is a file in
/// it -- leaves them behind. PostgreSQL only: on SQLite the rows go with the file and there is nothing to count.
/// </summary>
public class DeleteRegistrationPurgeTests
{
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task DeletingARegistrationRemovesItsRowsFromTheIdentityDatabase()
    {
        var registry = _scaffold.Services.GetRequiredService<IIdentityRegistry>();

        // A dev domain with a dev certificate that no test identity uses: registered with that certificate,
        // so none is requested for it.
        const string domain = "dev.dotyou.cloud";
        var certificates = Path.Combine(_scaffold.Services.GetRequiredService<OdinConfiguration>().Development.SslSourcePath, domain);
        var id = Guid.NewGuid();
        await registry.AddRegistration(new IdentityRegistrationRequest
        {
            Id = id,
            OdinId = new OdinId(domain),
            Email = $"purge@{domain}",
            PlanId = "",
            OptionalCertificatePemContent = new CertificatePemContent
            {
                Certificate = await File.ReadAllTextAsync(Path.Combine(certificates, "certificate.crt")),
                PrivateKey = await File.ReadAllTextAsync(Path.Combine(certificates, "private.key"))
            }
        });

        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using (var scope = container.GetTenantScope(domain).BeginLifetimeScope("DeleteRegistrationPurgeTests:write"))
        {
            await scope.Resolve<IdentityDatabase>().KeyValueCached.InsertAsync(new KeyValueRecord
            {
                key = Guid.NewGuid().ToByteArray(),
                data = Guid.NewGuid().ToByteArray()
            });
        }

        Assert.That(await CountRowsAsync(id), Is.GreaterThan(0), "the identity must have rows before it is deleted");

        await registry.DeleteRegistration(domain);

        Assert.That(await CountRowsAsync(id), Is.EqualTo(0), "the deleted identity's rows must be gone");
    }

    // Through a surviving identity's connection: on PostgreSQL they all read the same database.
    private async Task<long> CountRowsAsync(Guid identityId)
    {
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(TestIdentities.Frodo.OdinId.DomainName)
            .BeginLifetimeScope("DeleteRegistrationPurgeTests:count");
        return await scope.Resolve<IdentityDatabase>().CountRowsForIdentityAsync(identityId);
    }
}
#endif
