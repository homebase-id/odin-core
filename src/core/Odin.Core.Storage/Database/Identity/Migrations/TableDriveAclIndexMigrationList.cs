using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveAclIndexMigrationList : MigrationListBase
{
    public TableDriveAclIndexMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveAclIndexMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
