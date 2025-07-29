using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.System;

public partial class SystemMigrator
{
    protected override List<MigrationBase> SortedMigrations
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableJobsMigrationList(),
                new TableCertificatesMigrationList(),
                new TableRegistrationsMigrationList(),
                new TableSettingsMigrationList(),
            };

            foreach (var migration in list)
                migration.Validate();

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
