using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Attestation.Table;

public class TableAttestationRequestMigrationList : MigrationListBase
{
    public TableAttestationRequestMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAttestationRequestMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
