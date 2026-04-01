using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.System.Table;

public class TableRegistrationsTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldLoadAllRegistrations(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var tableRegistrations = scope.Resolve<TableRegistrations>();

        await tableRegistrations.InsertAsync(new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            email = "frodo@baggins.com",
            primaryDomainName = "frodos.joint",
        });
        await tableRegistrations.InsertAsync(new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            email = "same@baggins.com",
            primaryDomainName = "sams.joint",
        });

        var all = await tableRegistrations.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
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
        var tableRegistrations = scope.Resolve<TableRegistrations>();

        for (int i = 0; i < 3; i++)
        {
            await tableRegistrations.InsertAsync(new RegistrationsRecord
            {
                identityId = Guid.NewGuid(),
                email = $"user{i}@test.com",
                primaryDomainName = $"domain{i}.test",
            });
        }

        var (page1, cursor1) = await tableRegistrations.PagingByRowIdAsync(2, null);
        Assert.That(page1.Count, Is.EqualTo(2));
        Assert.That(cursor1, Is.Not.Null);

        var (page2, cursor2) = await tableRegistrations.PagingByRowIdAsync(2, cursor1);
        Assert.That(page2.Count, Is.EqualTo(1));
        Assert.That(cursor2, Is.Null);

        var (all, allCursor) = await tableRegistrations.PagingByRowIdAsync(100, null);
        Assert.That(all.Count, Is.EqualTo(3));
        Assert.That(allCursor, Is.Null);
    }
}