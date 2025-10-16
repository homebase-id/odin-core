#nullable enable
using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Util;

namespace Odin.Services.Authorization;

/// <summary>
/// Stores the server-side aspect of a <see cref="ClientAccessToken"/>
/// </summary>
public class ClientRegistrationStorage(TableClientRegistrationsCached clientRegistrationsTable)
{
    public async Task SaveAsync(IClientRegistration clientRegistration)
    {
        OdinValidationUtils.AssertNotEmptyGuid(clientRegistration.Id, "Client registration must have an id");
        
        var record = new ClientRegistrationsRecord
        {
            catId = clientRegistration.Id,
            issuedToId = clientRegistration.IssuedTo,
            expiresAt = UnixTimeUtc.Now().AddSeconds(clientRegistration.TimeToLiveSeconds),
            catType = clientRegistration.Type,
            value = clientRegistration.GetValue()
        };

        await clientRegistrationsTable.UpsertAsync(record);
    }

    public async Task<T?> GetAsync<T>(Guid id) where T : class
    {
        var record = await clientRegistrationsTable.GetAsync(id);

        if (record == null)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(record.value);
    }

    public async Task DeleteAsync(Guid tokenId)
    {
        await clientRegistrationsTable.DeleteAsync(tokenId);
    }
}