using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircleMemberMigrationList : MigrationListBase
{
    public TableCircleMemberMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCircleMemberMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
