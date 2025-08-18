using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.Attestation.Migrations;

public class GlobalMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public List<MigrationBase> SortedMigrations { get; init; }

    public GlobalMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableAttestationRequestMigrationList(),
            new TableAttestationStatusMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();

        SortedMigrations = MigrationList.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
    }
}
