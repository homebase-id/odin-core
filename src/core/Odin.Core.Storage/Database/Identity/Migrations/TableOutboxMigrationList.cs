using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableOutboxMigrationList : MigrationListBase
{
    public TableOutboxMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableOutboxMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
