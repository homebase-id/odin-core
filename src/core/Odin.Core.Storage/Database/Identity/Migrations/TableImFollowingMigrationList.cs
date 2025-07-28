using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableImFollowingMigrationList : MigrationListBase
{
    public TableImFollowingMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableImFollowingMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
