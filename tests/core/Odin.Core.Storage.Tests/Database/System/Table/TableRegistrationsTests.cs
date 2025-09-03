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
            identityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            email = "frodo@baggins.com",
            primaryDomainName = "frodos.joint",
        });
        await tableRegistrations.InsertAsync(new RegistrationsRecord
        {
            identityId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            email = "same@baggins.com",
            primaryDomainName = "sams.joint",
        });

        var all = await tableRegistrations.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));

        var now = UnixTimeUtc.Now();
        var lastSeen = new Dictionary<Guid, LastSeenEntry>
        {
            { all[0].identityId, new LastSeenEntry(all[0].identityId, all[0].primaryDomainName, now) },
            { all[1].identityId, new LastSeenEntry(all[1].identityId, all[1].primaryDomainName, now) },
            { Guid.NewGuid(), new LastSeenEntry(Guid.NewGuid(), "foo", now) },
        };

        await tableRegistrations.UpdateLastSeenAsync(lastSeen);

        all = await tableRegistrations.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all[0].lastSeen, Is.EqualTo(now));
        Assert.That(all[1].lastSeen, Is.EqualTo(now));

        var older = now.AddMilliseconds(-10000);
        lastSeen = new Dictionary<Guid, LastSeenEntry>
        {
            { all[0].identityId, new LastSeenEntry(all[0].identityId, all[0].primaryDomainName, older) },
            { all[1].identityId, new LastSeenEntry(all[1].identityId, all[1].primaryDomainName, older) },
            { Guid.NewGuid(), new LastSeenEntry(Guid.NewGuid(), "foo", older) },
        };

        await tableRegistrations.UpdateLastSeenAsync(lastSeen);
        all = await tableRegistrations.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all[0].lastSeen, Is.EqualTo(now));
        Assert.That(all[1].lastSeen, Is.EqualTo(now));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldGetLastSeen(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var tableRegistrations = scope.Resolve<TableRegistrations>();

        var now = UnixTimeUtc.Now();

        await tableRegistrations.InsertAsync(new RegistrationsRecord
        {
            identityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            email = "frodo@baggins.com",
            primaryDomainName = "frodos.joint",
            lastSeen = now
        });
        await tableRegistrations.InsertAsync(new RegistrationsRecord
        {
            identityId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            email = "same@baggins.com",
            primaryDomainName = "sams.joint",
            lastSeen = now
        });

        var lastSeenEntry = await tableRegistrations.GetLastSeenAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.That(lastSeenEntry, Is.Not.Null);
        Assert.That(lastSeenEntry.IdentityId, Is.EqualTo(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        Assert.That(lastSeenEntry.Domain, Is.EqualTo("frodos.joint"));
        Assert.That(lastSeenEntry.LastSeen, Is.EqualTo(now));

        lastSeenEntry = await tableRegistrations.GetLastSeenAsync("sams.joint");
        Assert.That(lastSeenEntry, Is.Not.Null);
        Assert.That(lastSeenEntry.IdentityId, Is.EqualTo(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        Assert.That(lastSeenEntry.Domain, Is.EqualTo("sams.joint"));
        Assert.That(lastSeenEntry.LastSeen, Is.EqualTo(now));

    }

}