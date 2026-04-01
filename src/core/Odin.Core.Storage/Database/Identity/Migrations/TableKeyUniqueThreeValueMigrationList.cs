using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

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
