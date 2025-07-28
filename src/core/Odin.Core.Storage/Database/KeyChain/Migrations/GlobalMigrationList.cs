using System.Collections.Generic;

namespace Odin.Core.Storage.Database.KeyChain.Migrations;

public class GlobalMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public GlobalMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableKeyChainMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();
    }
}
