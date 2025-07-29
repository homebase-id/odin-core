using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.KeyChain;

public partial class KeyChainMigrator
{
    protected override List<MigrationBase> SortedMigrations
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableKeyChainMigrationList(),
            };

            foreach (var migration in list)
                migration.Validate();

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
