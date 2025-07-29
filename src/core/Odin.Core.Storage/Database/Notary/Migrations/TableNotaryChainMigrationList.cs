using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Notary;

namespace Odin.Core.Storage.Database.Notary;

public class TableNotaryChainMigrationList : MigrationListBase
{
    public TableNotaryChainMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableNotaryChainMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
