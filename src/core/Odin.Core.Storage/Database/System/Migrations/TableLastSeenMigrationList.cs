using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.System.Migrations;

public class TableLastSeenMigrationList : MigrationListBase
{
    public TableLastSeenMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableLastSeenMigrationV202509090509(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
