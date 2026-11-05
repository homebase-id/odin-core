using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableCircleMemberMigrationList : MigrationListBase
{
    public TableCircleMemberMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCircleMemberMigrationV0(-1),
            new TableCircleMemberMigrationV202610301200(0),
            // AUTO-INSERT-MARKER
        };
    }

}
