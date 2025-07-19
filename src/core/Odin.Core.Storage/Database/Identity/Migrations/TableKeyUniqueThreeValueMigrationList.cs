using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyUniqueThreeValueMigrationList : MigrationListBase
{
    public TableKeyUniqueThreeValueMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableKeyUniqueThreeValueMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
