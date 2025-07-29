using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.System.Migrations;

public partial class SystemMigrator
{
    public List<MigrationBase> SortedMigrations 
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
