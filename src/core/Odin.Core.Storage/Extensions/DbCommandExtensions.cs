using System;
using System.Data;
using System.Data.Common;
using System.Text;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Extensions;

public static class DbCommandExtensions
{
    /// <summary>
    /// Very simple method to render the SQL for debugging purposes. It will NOT get everything right. In particular,
    /// you will need to set the correct DbType on the individual parameters.
    /// </summary>
    public static string RenderSqlForDebugging(this IDbCommand command)
    {
        var sql = new StringBuilder(command.CommandText);

        foreach (DbParameter parameter in command.Parameters)
        {
            var parameterName = parameter.ParameterName;
            var parameterValue = parameter.Value == DBNull.Value
                ? "NULL"
                : FormatParameterValue(parameter.Value, parameter.DbType);

            sql = sql.Replace(parameterName, parameterValue);
        }

        return sql.ToString();
    }

    //

    /// <summary>
    /// Very simple method to render the SQL for debugging purposes. It will NOT get everything right. In particular,
    /// you will need to set the correct DbType on the individual parameters.
    /// </summary>
    public static string RenderSqlForDebugging(this ICommandWrapper command)
    {
        return RenderSqlForDebugging(command.DangerousInstance);
    }

    //

    private static string FormatParameterValue(object value, DbType dbType)
    {
        if (value == null)
        {
            return "NULL";
        }

        switch (dbType)
        {
            case DbType.AnsiString:
            case DbType.AnsiStringFixedLength:
            case DbType.String:
            case DbType.StringFixedLength:
            case DbType.Date:
            case DbType.DateTime:
            case DbType.DateTime2:
            case DbType.DateTimeOffset:
                return $"'{value}'";
            default:
                return value.ToString();
        }
    }
}
