using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Attestation.Database.Migrations;

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
