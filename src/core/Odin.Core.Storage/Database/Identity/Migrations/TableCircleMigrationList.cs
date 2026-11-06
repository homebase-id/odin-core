using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableCircleMigrationList : MigrationListBase
{
    public TableCircleMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCircleMigrationV0(-1),
            new TableCircleMigrationV202608040942(0),
            new TableCircleMigrationV202608261644(202608040942),
            new TableCircleMigrationV202610140701(202608261644),
            new TableCircleMigrationV202610301200(202610140701),
            // AUTO-INSERT-MARKER
        };
    }

}
