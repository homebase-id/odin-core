using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Attestation;

namespace Odin.Core.Storage.Database.Attestation;

public class TableAttestationStatusMigrationList : MigrationListBase
{
    public TableAttestationStatusMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAttestationStatusMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
