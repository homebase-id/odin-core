using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.Notary.Migrations;

public class GlobalNotaryMigrationList : IGlobalMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public List<MigrationBase> SortedMigrations { get; init; }

    public GlobalNotaryMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableNotaryChainMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();

        SortedMigrations = MigrationList.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
    }
}
