using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableImFollowingMigrationList : MigrationListBase
{
    public TableImFollowingMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableImFollowingMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
