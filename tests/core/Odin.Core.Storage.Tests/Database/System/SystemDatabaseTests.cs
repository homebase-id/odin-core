using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.System;

public class SystemDatabaseTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldConnectAndQuery(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<SystemDatabase>();

        var r1 = new JobsRecord
        {
            id = Guid.NewGuid(),
            name = "adafdff",
            correlationId = Guid.NewGuid().ToString(),
            jobType = "JobType",
        };

        await db.Jobs.InsertAsync(r1);

        var r2 = await db.Jobs.GetAsync(r1.id);
        Assert.AreEqual(r1.id, r2.id);
        Assert.AreEqual(r1.name, r2.name);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldCommitTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<SystemDatabase>();

        var r1 = new JobsRecord
        {
            id = Guid.NewGuid(),
            name = "adafdff",
            correlationId = Guid.NewGuid().ToString(),
            jobType = "JobType",
        };

        {
            await using var tx = await db.BeginStackedTransactionAsync();
            await db.Jobs.InsertAsync(r1);
            tx.Commit();
        }

        {
            await using var cn = await db.CreateScopedConnectionAsync();
            var r2 = await db.Jobs.GetAsync(r1.id);
            Assert.AreEqual(r1.id, r2.id);
            Assert.AreEqual(r1.name, r2.name);
        }
    }


    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRollbackTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<SystemDatabase>();

        var r1 = new JobsRecord
        {
            id = Guid.NewGuid(),
            name = "adafdff",
            correlationId = Guid.NewGuid().ToString(),
            jobType = "JobType",
        };

        {
            await using var tx = await db.BeginStackedTransactionAsync();
            await db.Jobs.InsertAsync(r1);
        }

        {
            await using var cn = await db.CreateScopedConnectionAsync();
            var r2 = await db.Jobs.GetAsync(r1.id);
            Assert.IsNull(r2);
        }
    }
}