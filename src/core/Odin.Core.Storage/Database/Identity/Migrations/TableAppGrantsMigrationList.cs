using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableAppGrantsMigrationList : MigrationListBase
{
    public TableAppGrantsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAppGrantsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
