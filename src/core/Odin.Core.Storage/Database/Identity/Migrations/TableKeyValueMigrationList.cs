using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableKeyValueMigrationList : MigrationListBase
{
    public TableKeyValueMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableKeyValueMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
