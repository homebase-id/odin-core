using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveMainIndexMigrationList : MigrationListBase
{
    public TableDriveMainIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveMainIndexMigrationV202507191211(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
