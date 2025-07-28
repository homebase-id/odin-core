using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableKeyThreeValueMigrationList : MigrationListBase
{
    public TableKeyThreeValueMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableKeyThreeValueMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
