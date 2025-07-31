using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Attestation.Migrations;

public class TableAttestationRequestMigrationList : MigrationListBase
{
    public TableAttestationRequestMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAttestationRequestMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
