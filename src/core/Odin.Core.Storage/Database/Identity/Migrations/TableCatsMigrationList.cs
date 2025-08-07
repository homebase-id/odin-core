using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableCatsMigrationList : MigrationListBase
{
    public TableCatsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCatsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
