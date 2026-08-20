using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Database.System.Migrations;

public class TableDkimKeysMigrationList : MigrationListBase
{
    public TableDkimKeysMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDkimKeysMigrationV202608201100(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
