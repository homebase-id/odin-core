using FluentMigrator;

namespace Odin.Core.Storage.Migrations;

[Migration(20240828053709)]
public class CreateDriveAclIndex : Migration
{
    public override void Up()
    {
        Create.Table("DriveAclIndex")
            .WithColumn("identityId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("driveId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("fileId").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("aclMemberId").AsGuid().NotNullable().PrimaryKey();

        Create.Index("Idx0TableDriveAclIndexCRUD")
            .OnTable("DriveAclIndex")
            .OnColumn("identityId").Ascending()
            .OnColumn("driveId").Ascending()
            .OnColumn("aclMemberId").Ascending();
    }

    public override void Down()
    {
        Delete.Table("DriveAclIndex");
    }
}