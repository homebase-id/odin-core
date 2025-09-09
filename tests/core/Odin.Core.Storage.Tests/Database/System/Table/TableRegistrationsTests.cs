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


}