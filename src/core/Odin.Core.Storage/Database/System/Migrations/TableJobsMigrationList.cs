using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.System.Table;

public class TableJobsMigrationList : MigrationListBase
{
    public TableJobsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableJobsMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
