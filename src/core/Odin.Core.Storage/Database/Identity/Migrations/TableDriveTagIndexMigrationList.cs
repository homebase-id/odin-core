using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveTagIndexMigrationList : MigrationListBase
{
    public TableDriveTagIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveTagIndexMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
