using System;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Abstractions;

public class IdentityKey(Guid id)
{
    public Guid Id { get; } = id;

    public static implicit operator Guid(IdentityKey key)
    {
        return key.Id;
    }

    public byte[] ToByteArray()
    {
        return Id.ToByteArray();
    }

    public string BytesToSql(DatabaseType databaseType)
    {
        return ToByteArray().ToSql(databaseType);
    }
}
