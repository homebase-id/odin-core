using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircleMigrationList : MigrationListBase
{
    public TableCircleMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCircleMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
