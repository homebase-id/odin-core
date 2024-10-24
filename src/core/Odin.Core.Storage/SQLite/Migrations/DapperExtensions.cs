#if DAPPER
using Dapper;
using System;
using System.Data;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.Migrations;

public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override void SetValue(IDbDataParameter parameter, Guid value)
    {
        parameter.Value = value.ToByteArray();
    }

    public override Guid Parse(object value)
    {
        return new Guid((byte[])value);
    }
}

//

public class UnixTimeUtcUniqueHandler : SqlMapper.TypeHandler<UnixTimeUtcUnique>
{
    public override void SetValue(IDbDataParameter parameter, UnixTimeUtcUnique value)
    {
        parameter.Value = value.uniqueTime;
    }

    public override UnixTimeUtcUnique Parse(object value)
    {
        return new UnixTimeUtcUnique((long)value);
    }
}

//

public class StringHandler : SqlMapper.TypeHandler<string>
{
    public override void SetValue(IDbDataParameter parameter, string value)
    {
        parameter.Value = value;
    }

    public override string Parse(object value)
    {
        if (value is string stringValue)
        {
            if (stringValue == "System.Byte[]")
            {
                return null;
            }

            return stringValue;
        }
        if (value is byte[] bytesValue)
        {
            return System.Text.Encoding.UTF8.GetString(bytesValue);
        }

        throw new DataException($"Cannot convert {value.GetType()} to string");
    }
}


public static class DapperExtensions
{
    public static void ConfigureTypeHandlers()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new UnixTimeUtcUniqueHandler());
        SqlMapper.AddTypeHandler(new StringHandler());
    }
}
#endif