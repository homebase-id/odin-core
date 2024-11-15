using System;

namespace Odin.Core.Storage.Database.Identity.Abstractions;

public class IdentityKey(Guid id)
{
    public Guid Id { get; } = id;
}
