using Dapper;
using System;
using System.Data;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Core.Util;

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

public class OdinIdHandler : SqlMapper.TypeHandler<OdinId?>
{
    public override void SetValue(IDbDataParameter parameter, OdinId? value)
    {
        parameter.Value = value?.DomainName;
    }

    public override OdinId? Parse(object value)
    {
        var domain = "";
        if (value is string stringValue)
        {
            domain = stringValue;
        }
        else if (value is byte[] bytesValue)
        {
            domain = System.Text.Encoding.UTF8.GetString(bytesValue);
        }

        if (AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return new OdinId(domain);
        }
        return null;
    }
}

//

public static class DapperExtensions
{
    public static void ConfigureTypeHandlers()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new UnixTimeUtcUniqueHandler());
        SqlMapper.AddTypeHandler(new OdinIdHandler());
    }
}
