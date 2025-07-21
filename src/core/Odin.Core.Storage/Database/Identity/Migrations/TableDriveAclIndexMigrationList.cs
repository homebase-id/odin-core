using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveAclIndexMigrationList : MigrationListBase
{
    public TableDriveAclIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveAclIndexMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
