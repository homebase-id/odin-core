using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Attestation.Migrations;

public class GlobalMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public GlobalMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableAttestationRequestMigrationList(),
            new TableAttestationStatusMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();
    }
}
