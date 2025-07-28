using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

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
