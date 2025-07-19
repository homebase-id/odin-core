using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveTransferHistoryMigrationList : MigrationListBase
{
    public TableDriveTransferHistoryMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveTransferHistoryMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
