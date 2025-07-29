using System;
using System.Data;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.System.Migrations;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database;

public class MigratorTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldCreateTableVersionInfo(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var connectionFactory  = scope.Resolve<ScopedSystemConnectionFactory>();

        var migrator = new Migrator();
        await migrator.MigrateAsync(connectionFactory, new GlobalSystemMigrationList());

        //
        // Make sure the VersionInfo table exists but is empty (version is -1)
        //
        {
            var currentVersion = await migrator.GetCurrentVersionAsync(connectionFactory);
            Assert.That(currentVersion, Is.EqualTo(-1));
        }

        //
        // Make sure we can insert the version and read it back
        //
        {
            var testVersion1 = 20250729094412L;

            await using var cn = await connectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = Migrator.SqlUpsertVersion;

            var versionParam = cmd.CreateParameter();
            versionParam.DbType = DbType.Int64;
            versionParam.ParameterName = "@version";
            versionParam.Value = testVersion1;
            cmd.Parameters.Add(versionParam);

            await cmd.ExecuteNonQueryAsync();

            var currentVersion = await migrator.GetCurrentVersionAsync(connectionFactory);
            Assert.That(currentVersion, Is.EqualTo(testVersion1));
        }

        //
        // Make sure we can update the version and read it back
        //
        {
            var testVersion2 = 20250729094413L;

            await using var cn = await connectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = Migrator.SqlUpsertVersion;

            var versionParam = cmd.CreateParameter();
            versionParam.DbType = DbType.Int64;
            versionParam.ParameterName = "@version";
            versionParam.Value = testVersion2;
            cmd.Parameters.Add(versionParam);

            await cmd.ExecuteNonQueryAsync();

            var currentVersion = await migrator.GetCurrentVersionAsync(connectionFactory);
            Assert.That(currentVersion, Is.EqualTo(testVersion2));
        }
    }
}