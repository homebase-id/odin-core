using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppNotificationsMigrationList : MigrationListBase
{
    public TableAppNotificationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAppNotificationsMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
