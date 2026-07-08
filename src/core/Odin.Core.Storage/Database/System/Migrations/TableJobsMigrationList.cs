using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.System.Migrations;

public class TableJobsMigrationList : MigrationListBase
{
    public TableJobsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableJobsMigrationV0(-1),
            new TableJobsMigrationV202607080912(0),
            // AUTO-INSERT-MARKER
        };
    }

}
