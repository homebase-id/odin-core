using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.KeyChain.Migrations;

public class GlobalKeyChainMigrationList : IGlobalMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public List<MigrationBase> SortedMigrations { get; init; }

    public GlobalKeyChainMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableKeyChainMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();

        SortedMigrations = MigrationList.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
    }
}
