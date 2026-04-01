using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

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
