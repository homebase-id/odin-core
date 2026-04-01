using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableKeyTwoValueMigrationList : MigrationListBase
{
    public TableKeyTwoValueMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableKeyTwoValueMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
