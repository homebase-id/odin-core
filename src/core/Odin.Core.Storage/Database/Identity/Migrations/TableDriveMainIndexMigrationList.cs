using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveMainIndexMigrationList : MigrationListBase
{
    public TableDriveMainIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveMainIndexMigrationV0(-1),
            new TableDriveMainIndexMigrationV202507191211(0),
            new TableDriveMainIndexMigrationV202611171200(202507191211),
            // AUTO-INSERT-MARKER
        };
    }

}
