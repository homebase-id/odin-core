using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Notary;

public class GlobalMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public GlobalMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableNotaryChainMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();
    }
}
