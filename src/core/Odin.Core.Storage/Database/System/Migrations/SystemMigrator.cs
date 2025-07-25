using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System.Migrations;

//
// Migrator example for the system database.
//
// SQL generator creates ONE of these files per database (not per table).
// This means that this class contains ALL migrations for the entire database.
// It will be instantiated and executed by SystemDatabase everytime the program starts.
//

public class SystemMigrator : MigrationListBase
{
    private enum Direction
    {
        Nowhere,
        Up,
        Down
    }

    public SystemMigrator()
    {
        Migrations =
        [
            new TableJobsMigrationV0(this), // MS:TODO remove 'this' from the ctor
            new TableCertificatesMigrationV0(this),
            new TableRegistrationsMigrationV0(this),
            new TableSettingsMigrationV0(this)
        ];
    }

    // REQUIREMENT: all databases must hav a VersionInfo table
    // Alternatively, we could control this with parameters to the ctor.
    public async Task Migrate(IConnectionWrapper connection, long requestedVersion = long.MaxValue)
    {
        // SEB:TODO distributed lock here to prevent multiple migrations running at the same time

        var dbVersion = await Task.FromResult(123L); // SEB:TODO get the current database version from the VersionInfo table

        var direction = requestedVersion.CompareTo(dbVersion) switch
        {
            > 0 => Direction.Up,
            < 0 => Direction.Down,
            0 => Direction.Nowhere
        };

        if (direction == Direction.Nowhere)
        {
            return;
        }

        //
        // Collect all migrations that need to be run
        //

        List<MigrationBase> migrationsToRun;
        if (direction == Direction.Up)
        {
            // Collect all migrations with
            //  version greater than the current database version and less than or equal to the requested version
            //  sort OLDEST to NEWEST
            migrationsToRun = Migrations
                .Where(m => m.MigrationVersion > dbVersion && m.MigrationVersion <= requestedVersion)
                .OrderBy(m => m.MigrationVersion)
                .ToList();
        }
        else
        {
            // Collect all migrations with
            //   version greater than the requested version and less than or equal to the current database version
            //   sort NEWEST to OLDEST
            migrationsToRun = Migrations
                .Where(m => m.MigrationVersion > requestedVersion && m.MigrationVersion <= dbVersion)
                .OrderBy(m => m.MigrationVersion)
                .Reverse()
                .ToList();
        }

        //
        // Run collected migrations
        //

        foreach (var migration in migrationsToRun)
        {
            if (direction == Direction.Up)
            {
                // MS:TODO do additional table version checks here, if you like
                await migration.UpAsync(connection);
            }
            else
            {
                // MS:TODO do additional table version checks here, if you like
                await migration.DownAsync(connection);
            }
            // SEB:TODO update the VersionInfo table with the new version
        }

    }
}
