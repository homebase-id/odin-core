using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableMySubscriptionsMigrationList : MigrationListBase
{
    public TableMySubscriptionsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableMySubscriptionsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
