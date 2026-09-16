using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableBundleTokenAppsMigrationList : MigrationListBase
{
    public TableBundleTokenAppsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableBundleTokenAppsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
