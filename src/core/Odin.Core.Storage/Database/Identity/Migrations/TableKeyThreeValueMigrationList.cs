using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyThreeValueMigrationList : MigrationListBase
{
    public TableKeyThreeValueMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableKeyThreeValueMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
