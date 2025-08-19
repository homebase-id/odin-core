using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Attestation.Migrations;

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
