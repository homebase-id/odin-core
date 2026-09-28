using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity;

/// <summary>
/// <see cref="IdentityDatabase.PurgeIdentityAsync"/> removes one identity's rows and nothing else (#1792).
/// </summary>
public class IdentityPurgeTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItRemovesTheIdentitysRowsAndLeavesAnotherIdentitysAlone(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        await db.KeyValue.InsertAsync(new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = [1] });
        await db.KeyTwoValue.InsertAsync(new KeyTwoValueRecord
        {
            key1 = Guid.NewGuid().ToByteArray(), key2 = Guid.NewGuid().ToByteArray(), data = [1]
        });

        var otherIdentityId = Guid.NewGuid();
        await InsertKeyValueForAsync(db, otherIdentityId);

        Assert.That(await db.CountRowsForIdentityAsync(IdentityId), Is.GreaterThanOrEqualTo(2));

        await db.PurgeIdentityAsync(IdentityId);

        Assert.That(await db.CountRowsForIdentityAsync(IdentityId), Is.EqualTo(0));
        Assert.That(await db.CountRowsForIdentityAsync(otherIdentityId), Is.EqualTo(1), "another identity's rows must survive");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItRefusesTheEmptyId(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        Assert.ThrowsAsync<OdinSystemException>(() => db.PurgeIdentityAsync(Guid.Empty));
    }

    /// <summary>
    /// Every identity table with an identityId column is one the purge covers: a table added outside the generated
    /// list would otherwise keep a deleted identity's rows without anything noticing.
    /// </summary>
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task EveryTableWithAnIdentityIdIsPurged(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        var withIdentityId = (await db.TablesWithIdentityIdAsync()).Select(t => t.ToLowerInvariant()).ToList();
        var purged = (await db.GetPurgeTablesAsync()).Select(t => t.ToLowerInvariant()).ToHashSet();

        // In the tests the system tables can share the database; they, and migrations' copies of them, are not an
        // identity's to purge.
        var system = SystemDatabase.TableTypes.Select(t => t.Name["Table".Length..].ToLowerInvariant()).ToList();
        var infix = MigrationBase.BackupTableInfix.ToLowerInvariant();
        bool IsSystem(string table) => system.Any(s => table == s || table.StartsWith(s + infix));

        var missed = withIdentityId.Where(t => !IsSystem(t) && !purged.Contains(t)).ToList();
        Assert.That(missed, Is.Empty, "tables with an identityId column that PurgeIdentityAsync does not cover");
        Assert.That(purged.Where(IsSystem), Is.Empty, "the purge must not reach a system table");
        Assert.That(withIdentityId, Is.SupersetOf(purged), "every purged table must exist and carry identityId");
    }

    private static async Task InsertKeyValueForAsync(IdentityDatabase db, Guid identityId)
    {
        await using var cn = await db.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO KeyValue (identityId,key,data) VALUES (@identityId,@key,@data);";
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        cmd.AddParameter("@key", DbType.Binary, Guid.NewGuid().ToByteArray());
        cmd.AddParameter("@data", DbType.Binary, new byte[] { 1 });
        await cmd.ExecuteNonQueryAsync();
    }
}
