using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.Attestation.Migrations;

public partial class GlobalAttestationMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public List<MigrationBase> SortedMigrations { get; init; }

    public GlobalAttestationMigrationList()
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
