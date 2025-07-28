using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableConnectionsMigrationList : MigrationListBase
{
    public TableConnectionsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableConnectionsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
