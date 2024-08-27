using Dapper;
using FluentMigrator;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.Migrations;

[Migration(20240826113527)]
public class CreateDriveMainIndexTable : Migration
{
    public override void Up()
    {
        Create.Table("DriveMainIndex")
            .WithColumn("identityId").AsGuid().PrimaryKey()
            .WithColumn("Text").AsString();


    }

    public override void Down()
    {
        Delete.Table("DriveMainIndex");
    }
}
