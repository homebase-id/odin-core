using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDrivesMigrationList : MigrationListBase
{
    public TableDrivesMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDrivesMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
