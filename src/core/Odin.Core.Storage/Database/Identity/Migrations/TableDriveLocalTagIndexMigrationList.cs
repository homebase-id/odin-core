using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

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
