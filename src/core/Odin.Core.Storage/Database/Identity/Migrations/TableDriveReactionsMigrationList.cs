using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveReactionsMigrationList : MigrationListBase
{
    public TableDriveReactionsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableDriveReactionsMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
