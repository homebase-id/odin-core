using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Attestation;

namespace Odin.Core.Storage.Database.Attestation;

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
