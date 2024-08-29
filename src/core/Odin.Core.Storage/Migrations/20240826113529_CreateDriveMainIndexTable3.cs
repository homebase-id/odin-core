using Dapper;
using FluentMigrator;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.Migrations;

[Migration(20240826113529)]
public class CreateDriveMainIndexTable3 : Migration
{
    public override void Up()
    {
        Alter.Table("DriveMainIndex")
            .AddColumn("modified").AsInt64();

        Create.Index("Idx0TableDriveMainIndexCRUD")
            .OnTable("DriveMainIndex")
            .OnColumn("identityId").Ascending()
            .OnColumn("driveId").Ascending()
            .OnColumn("modified").Ascending();
    }

    public override void Down()
    {
        Delete.Index("Idx0TableDriveMainIndexCRUD").OnTable("DriveMainIndex");
        Delete.Column("modified").FromTable("DriveMainIndex");
    }
}
