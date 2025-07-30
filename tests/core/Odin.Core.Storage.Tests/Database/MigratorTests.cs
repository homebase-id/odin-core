using System;
using System.Data;
using System.Threading.Tasks;
using Autofac;
using Moq;
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
    public async Task ItShouldCreateAndUpdateTableVersionInfo(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, false);
        await using var scope = Services.BeginLifetimeScope();
        var migrator  = scope.Resolve<SystemMigrator>();

        await migrator.MigrateAsync();

        //
        // Make sure the VersionInfo table exists but is empty (version is -1)
        //
        {
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(-1));
        }

        //
        // Make sure we can insert the version and read it back
        //
        {
            var testVersion1 = 20250729094412L;

            await migrator.SetCurrentVersionAsync(testVersion1);

            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(testVersion1));
        }

        //
        // Make sure we can update the version and read it back
        //
        {
            var testVersion2 = 20250729094413L;

            await migrator.SetCurrentVersionAsync(testVersion2);

            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(testVersion2));
        }
    }
}