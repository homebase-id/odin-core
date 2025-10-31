using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDrivesMigrationList : MigrationListBase
{
    public TableDrivesMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDrivesMigrationV0(-1),
            new TableDrivesMigrationV202509220609(0),
            new TableDrivesMigrationV202510311515(202509220609),
            // AUTO-INSERT-MARKER
        };
    }

}
