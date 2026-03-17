using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableImFollowingMigrationList : MigrationListBase
{
    public TableImFollowingMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableImFollowingMigrationV0(-1),
            new TableImFollowingMigrationV202603141230(0),
            // AUTO-INSERT-MARKER
        };
    }

}
