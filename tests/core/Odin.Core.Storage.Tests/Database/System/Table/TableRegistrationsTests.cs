using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

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
    public async Task ItShouldUpdateLastSeen(DatabaseType databaseType)
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

        var now = UnixTimeUtc.Now();
        var lastSeen = new Dictionary<Guid, UnixTimeUtc>()
        {
            { all[0].identityId, now },
            { all[1].identityId, now },
            { Guid.NewGuid(), now },
        };

        await tableRegistrations.UpdateLastSeen(lastSeen);
        all = await tableRegistrations.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all[0].lastSeen, Is.EqualTo(now));
        Assert.That(all[1].lastSeen, Is.EqualTo(now));

        var older = now.AddMilliseconds(-10000);
        lastSeen[all[0].identityId] = older;
        lastSeen[all[1].identityId] = older;

        await tableRegistrations.UpdateLastSeen(lastSeen);
        all = await tableRegistrations.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all[0].lastSeen, Is.EqualTo(now));
        Assert.That(all[1].lastSeen, Is.EqualTo(now));
    }

    //
}