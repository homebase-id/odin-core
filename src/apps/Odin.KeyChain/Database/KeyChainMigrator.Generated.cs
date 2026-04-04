// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System.Collections.Generic;
using System.Linq;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.KeyChain.Database.Connection;
using Odin.KeyChain.Database.Migrations;

#nullable disable

namespace Odin.KeyChain.Database;

public partial class KeyChainMigrator
{
    public override List<MigrationBase> SortedMigrations
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableKeyChainMigrationList(),
            };

            foreach (var migration in list)
            {
                migration.Validate();
            }

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
