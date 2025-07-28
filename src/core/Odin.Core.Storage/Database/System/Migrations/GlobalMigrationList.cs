using System.Collections.Generic;

namespace Odin.Core.Storage.Database.System;

public class GlobalMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public GlobalMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableJobsMigrationList(),
            new TableCertificatesMigrationList(),
            new TableRegistrationsMigrationList(),
            new TableSettingsMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();
    }
}
