using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableDriveLocalTagIndexMigrationList : MigrationListBase
{
    public TableDriveLocalTagIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveLocalTagIndexMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
