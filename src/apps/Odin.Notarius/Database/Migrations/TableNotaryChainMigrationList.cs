using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Notarius.Database.Migrations;

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
