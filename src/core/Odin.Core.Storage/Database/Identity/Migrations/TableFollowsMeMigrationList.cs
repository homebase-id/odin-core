using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableFollowsMeMigrationList : MigrationListBase
{
    public TableFollowsMeMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableFollowsMeMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
