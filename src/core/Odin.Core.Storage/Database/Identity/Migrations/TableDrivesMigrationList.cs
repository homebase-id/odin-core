using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDrivesMigrationList : MigrationListBase
{
    public TableDrivesMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDrivesMigrationV0(0),
            new TableDrivesMigrationV20250723(this),
            // AUTO-INSERT-MARKER
        };
    }

}
