using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveMainIndexMigrationList : MigrationListBase
{
    public TableDriveMainIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveMainIndexMigrationV0(this),
            new TableDriveMainIndexMigrationV20250719(this),
            // AUTO-INSERT-MARKER
        };
    }

}
