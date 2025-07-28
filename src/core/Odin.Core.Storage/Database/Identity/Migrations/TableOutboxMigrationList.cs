using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

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
