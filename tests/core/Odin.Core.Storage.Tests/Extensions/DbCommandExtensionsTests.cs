using System;
using System.Data;
using System.Data.Common;
using NUnit.Framework;
using Odin.Core.Storage.Extensions;

using Microsoft.Data.Sqlite;


namespace Odin.Core.Storage.Tests.Extensions;

public class DbCommandExtensionsTests
{
    [Test]
    public void RenderSqlForDebugging_Int_And_String()
    {
        var command = SqliteFactory.Instance.CreateCommand();
        Assert.That(command, Is.Not.Null);

        command.CommandText = "SELECT * FROM Users WHERE UserId = @UserId AND Status = @Status";
        command.CommandType = CommandType.Text;

        // Add parameters
        DbParameter param1 = command.CreateParameter();
        param1.ParameterName = "@UserId";
        param1.DbType = DbType.Int32;
        param1.Value = 123;
        command.Parameters.Add(param1);

        DbParameter param2 = command.CreateParameter();
        param2.ParameterName = "@Status";
        param2.DbType = DbType.String;
        param2.Value = "Active";
        command.Parameters.Add(param2);

        var sql = command.RenderSqlForDebugging();
        Assert.That(sql, Is.EqualTo("SELECT * FROM Users WHERE UserId = 123 AND Status = 'Active'"));
    }

    [Test]
    public void RenderSqlForDebugging_Int_And_Null()
    {
        var command = SqliteFactory.Instance.CreateCommand();
        Assert.That(command, Is.Not.Null);

        command.CommandText = "SELECT * FROM Users WHERE UserId = @UserId OR Status IS @Status";
        command.CommandType = CommandType.Text;

        // Add parameters
        DbParameter param1 = command.CreateParameter();
        param1.ParameterName = "@UserId";
        param1.DbType = DbType.Int32;
        param1.Value = 123;
        command.Parameters.Add(param1);

        DbParameter param2 = command.CreateParameter();
        param2.ParameterName = "@Status";
        param2.DbType = DbType.String;
        param2.Value = DBNull.Value;
        command.Parameters.Add(param2);

        var sql = command.RenderSqlForDebugging();
        Assert.That(sql, Is.EqualTo("SELECT * FROM Users WHERE UserId = 123 OR Status IS NULL"));
    }

}

