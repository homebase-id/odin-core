using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Notary.Table;

public class TableNotaryChainMigrationList : MigrationListBase
{
    public TableNotaryChainMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableNotaryChainMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
