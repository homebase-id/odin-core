using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage;

#nullable enable

public abstract class AbstractTable(IScopedConnectionFactory connectionFactory)
{
    // SEB:NOTE this is temporary until we have a proper migration system
    public abstract Task EnsureTableExistsAsync(bool dropExisting = false);

    public object Cast(Guid? guid)
    {
        return guid.Cast(connectionFactory.DatabaseType);
    }
}

