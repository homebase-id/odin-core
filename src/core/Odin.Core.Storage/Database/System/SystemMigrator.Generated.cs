using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.System.Migrations;

public partial class GlobalSystemMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public List<MigrationBase> SortedMigrations { get; init; }

    public GlobalSystemMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableJobsMigrationList(),
            new TableCertificatesMigrationList(),
            new TableRegistrationsMigrationList(),
            new TableSettingsMigrationList(),
        };
        foreach (var migration in MigrationList)
            migration.Validate();

        SortedMigrations = MigrationList.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
    }
}
