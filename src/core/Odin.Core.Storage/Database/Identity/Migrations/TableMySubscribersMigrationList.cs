using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableMySubscribersMigrationList : MigrationListBase
{
    public TableMySubscribersMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableMySubscribersMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
