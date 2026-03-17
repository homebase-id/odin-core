using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableFollowsMeMigrationList : MigrationListBase
{
    public TableFollowsMeMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableFollowsMeMigrationV0(-1),
            new TableFollowsMeMigrationV202603141230(0),
            // AUTO-INSERT-MARKER
        };
    }

}
