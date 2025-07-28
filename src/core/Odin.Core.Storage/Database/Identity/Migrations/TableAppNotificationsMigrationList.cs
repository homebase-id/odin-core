using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableAppNotificationsMigrationList : MigrationListBase
{
    public TableAppNotificationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAppNotificationsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
