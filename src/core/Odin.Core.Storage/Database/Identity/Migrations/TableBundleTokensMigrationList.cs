using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableBundleTokensMigrationList : MigrationListBase
{
    public TableBundleTokensMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableBundleTokensMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
