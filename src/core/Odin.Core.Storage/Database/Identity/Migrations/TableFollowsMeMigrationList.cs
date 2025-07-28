using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableFollowsMeMigrationList : MigrationListBase
{
    public TableFollowsMeMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableFollowsMeMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
