using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.KeyChain.Table;

public class TableKeyChainMigrationList : MigrationListBase
{
    public TableKeyChainMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableKeyChainMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
