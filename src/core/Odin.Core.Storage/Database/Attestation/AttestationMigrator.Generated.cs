using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.Attestation.Migrations;

public partial class AttestationMigrator
{
    public List<MigrationBase> SortedMigrations 
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableAttestationRequestMigrationList(),
                new TableAttestationStatusMigrationList(),
            };

            foreach (var migration in list)
                migration.Validate();

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
