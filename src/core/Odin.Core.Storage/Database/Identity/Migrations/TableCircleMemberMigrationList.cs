using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableCircleMemberMigrationList : MigrationListBase
{
    public TableCircleMemberMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCircleMemberMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
