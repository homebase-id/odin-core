using System.Collections.Generic;
using MediatR;
using Odin.Core.Cryptography.Data;
using Odin.Services.EncryptionKeyService;

namespace Odin.Services.Mediator.Owner;

/// <summary>
/// Raised when the owner makes a request to the system, thus indicating we have access to
/// the master key to perform various system operations (rotate keys, etc.)
/// </summary>
public class OwnerIsOnlineNotification : INotification
{
}

public class RsaKeyRotatedNotification : INotification
{
    public RsaKeyType KeyType { get; }
    public IList<RsaFullKeyData> ExpiredKeys { get; }
    public RsaFullKeyListData NewKeySet { get; }

    public RsaKeyRotatedNotification(RsaKeyType keyType, IList<RsaFullKeyData> expiredKeys, RsaFullKeyListData newKeySet)
    {
        this.KeyType = keyType;
        ExpiredKeys = expiredKeys ?? new List<RsaFullKeyData>();
        NewKeySet = newKeySet;
    }
}