using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveTagIndexMigrationList : MigrationListBase
{
    public TableDriveTagIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveTagIndexMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
