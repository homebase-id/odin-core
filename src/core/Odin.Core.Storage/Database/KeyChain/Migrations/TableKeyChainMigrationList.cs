using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.KeyChain;

namespace Odin.Core.Storage.Database.KeyChain;

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
