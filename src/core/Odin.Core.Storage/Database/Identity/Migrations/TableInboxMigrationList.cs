using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableInboxMigrationList : MigrationListBase
{
    public TableInboxMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableInboxMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
