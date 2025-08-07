using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Notary.Migrations;

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
