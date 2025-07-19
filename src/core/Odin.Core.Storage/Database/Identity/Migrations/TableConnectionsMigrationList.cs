using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableConnectionsMigrationList : MigrationListBase
{
    public TableConnectionsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableConnectionsMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
