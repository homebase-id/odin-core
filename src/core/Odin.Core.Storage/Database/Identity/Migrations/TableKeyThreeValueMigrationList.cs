using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

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
