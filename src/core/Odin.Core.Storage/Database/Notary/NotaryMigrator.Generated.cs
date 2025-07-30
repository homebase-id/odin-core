using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.Notary;

public partial class NotaryMigrator
{
    protected override List<MigrationBase> SortedMigrations
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableNotaryChainMigrationList(),
            };

            foreach (var migration in list)
                migration.Validate();

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
