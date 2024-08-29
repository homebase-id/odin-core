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
            .WithColumn("identityId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("driveId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("fileId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("globalTransitId").AsGuid()
            .WithColumn("fileState").AsInt32().NotNullable()
            .WithColumn("requiredSecurityGroup").AsInt32().NotNullable()
            .WithColumn("fileSystemType").AsInt32().NotNullable()
            .WithColumn("userDate").AsInt32().NotNullable()
            .WithColumn("fileType").AsInt32().NotNullable()
            .WithColumn("dataType").AsInt32().NotNullable()
            .WithColumn("archivalStatus").AsInt32().NotNullable()
            .WithColumn("historyStatus").AsInt32().NotNullable()
            .WithColumn("senderId").AsString()
            .WithColumn("groupId").AsGuid()
            .WithColumn("uniqueId").AsGuid()
            .WithColumn("byteCount").AsInt64().NotNullable()
            // .WithColumn("created").AsInt64().NotNullable()
            // .WithColumn("modified").AsInt64()
            ;

        Create.UniqueConstraint()
            .OnTable("DriveMainIndex")
            .Columns("identityId", "driveId", "uniqueId");

        Create.UniqueConstraint()
            .OnTable("DriveMainIndex")
            .Columns("identityId", "driveId", "globalTransitId");

        // Create.Index("Idx0TableDriveMainIndexCRUD")
        //     .OnTable("DriveMainIndex")
        //     .OnColumn("identityId").Ascending()
        //     .OnColumn("driveId").Ascending()
        //     .OnColumn("modified").Ascending();
    }

    public override void Down()
    {
        Delete.Table("DriveMainIndex");
    }
}
