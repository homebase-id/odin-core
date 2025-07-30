using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.System.Table;

namespace Odin.Core.Storage.Database.System.Migrations;

public class TableSettingsMigrationList : MigrationListBase
{
    public TableSettingsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableSettingsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
