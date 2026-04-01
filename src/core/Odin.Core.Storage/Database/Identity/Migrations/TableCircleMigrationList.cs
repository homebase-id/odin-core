using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableCircleMigrationList : MigrationListBase
{
    public TableCircleMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCircleMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
