using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableNonceMigrationList : MigrationListBase
{
    public TableNonceMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableNonceMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
