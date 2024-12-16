using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity;


public class IdentityDatabaseTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldConnectAndQuery(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        var r1 = new KeyValueRecord
        {
            key = Guid.NewGuid().ToByteArray(),
            data = Guid.NewGuid().ToByteArray()
        };

        await db.KeyValue.InsertAsync(r1);

        var r2 = await db.KeyValue.GetAsync(r1.key);

        Assert.AreEqual(r1.key, r2.key);
        Assert.AreEqual(r1.data, r2.data);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldCommitTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        var r1 = new KeyValueRecord
        {
            key = Guid.NewGuid().ToByteArray(),
            data = Guid.NewGuid().ToByteArray()
        };

        {
            await using var tx = await db.BeginStackedTransactionAsync();
            await db.KeyValue.InsertAsync(r1);
            tx.Commit();
        }

        {
            await using var cn = await db.CreateScopedConnectionAsync();
            var r2 = await db.KeyValue.GetAsync(r1.key);
            Assert.AreEqual(r1.key, r2.key);
            Assert.AreEqual(r1.data, r2.data);
        }
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldRollbackTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        var r1 = new KeyValueRecord
        {
            key = Guid.NewGuid().ToByteArray(),
            data = Guid.NewGuid().ToByteArray()
        };

        {
            await using var tx = await db.BeginStackedTransactionAsync();
            await db.KeyValue.InsertAsync(r1);
        }

        {
            await using var cn = await db.CreateScopedConnectionAsync();
            var r2 = await db.KeyValue.GetAsync(r1.key);
            Assert.IsNull(r2);
        }
    }
}