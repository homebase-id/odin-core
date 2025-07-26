using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

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
