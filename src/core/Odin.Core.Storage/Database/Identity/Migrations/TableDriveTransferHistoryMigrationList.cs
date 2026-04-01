using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveTransferHistoryMigrationList : MigrationListBase
{
    public TableDriveTransferHistoryMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveTransferHistoryMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
