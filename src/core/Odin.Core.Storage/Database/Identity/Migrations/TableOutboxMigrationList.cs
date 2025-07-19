using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableOutboxMigrationList : MigrationListBase
{
    public TableOutboxMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableOutboxMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
