using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableDriveAclIndexMigrationList : MigrationListBase
{
    public TableDriveAclIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveAclIndexMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
