using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Attestation.Table;

public class TableAttestationStatusMigrationList : MigrationListBase
{
    public TableAttestationStatusMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAttestationStatusMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
