using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableDriveReactionsMigrationList : MigrationListBase
{
    public TableDriveReactionsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveReactionsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
