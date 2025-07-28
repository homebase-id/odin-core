using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

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
