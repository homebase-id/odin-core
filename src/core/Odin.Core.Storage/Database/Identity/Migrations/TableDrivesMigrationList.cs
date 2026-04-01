using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDrivesMigrationList : MigrationListBase
{
    public TableDrivesMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDrivesMigrationV202510311515(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
