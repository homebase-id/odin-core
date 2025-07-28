using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableKeyUniqueThreeValueMigrationList : MigrationListBase
{
    public TableKeyUniqueThreeValueMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableKeyUniqueThreeValueMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
