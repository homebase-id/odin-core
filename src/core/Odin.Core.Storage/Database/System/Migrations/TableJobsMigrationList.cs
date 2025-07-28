using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.System.Table;

namespace Odin.Core.Storage.Database.System;

public class TableJobsMigrationList : MigrationListBase
{
    public TableJobsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableJobsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
