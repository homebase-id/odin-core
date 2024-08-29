using System;
using Dapper;
using FluentMigrator;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.Migrations;

[Migration(20240826113528)]
public class CreateDriveMainIndexTable2 : Migration
{
    public override void Up()
    {
        IfDatabase("sqlite")
            .Execute.Sql(
                """
                ALTER TABLE DriveMainIndex
                ADD COLUMN created INTEGER NOT NULL DEFAULT (strftime('%s','now') * 1000);
                """
            );

        IfDatabase("postgres")
            .Execute.Sql(
                """
                ALTER TABLE DriveMainIndex
                ADD COLUMN created BIGINT NOT NULL DEFAULT EXTRACT(EPOCH FROM now()) * 1000;
                """
            );
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE DriveMainIndex DROP COLUMN created;");
    }
}
