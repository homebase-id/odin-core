using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.System.Migrations;

public class TableJobsMigrationList : MigrationListBase
{
    public TableJobsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableJobsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
