using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.System.Migrations;

public class TableCertificatesMigrationList : MigrationListBase
{
    public TableCertificatesMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCertificatesMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
