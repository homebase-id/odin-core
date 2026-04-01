// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System.Collections.Generic;
using System.Linq;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Notarius.Database.Connection;
using Odin.Notarius.Database.Migrations;

#nullable disable

namespace Odin.Notarius.Database;

public partial class NotaryMigrator
{
    public override List<MigrationBase> SortedMigrations
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableNotaryChainMigrationList(),
            };

            foreach (var migration in list)
            {
                migration.Validate();
            }

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
