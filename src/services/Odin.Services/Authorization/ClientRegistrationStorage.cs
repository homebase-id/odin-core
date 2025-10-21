#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
public class ClientRegistrationStorage(TableClientRegistrations clientRegistrationsTable)
{
    public async Task SaveAsync(IClientRegistration clientRegistration)
    {
        OdinValidationUtils.AssertNotEmptyGuid(clientRegistration.Id, "Client registration must have an id");

        var record = new ClientRegistrationsRecord
        {
            catId = clientRegistration.Id,
            issuedToId = clientRegistration.IssuedTo,
            expiresAt = UnixTimeUtc.Now().AddSeconds(clientRegistration.TimeToLiveSeconds),
            ttl = clientRegistration.TimeToLiveSeconds,
            catType = clientRegistration.Type,
            categoryId = clientRegistration.CategoryId,
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
        
        if (record.expiresAt < UnixTimeUtc.Now())
        {
            await DeleteAsync(id);
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(record.value);
    }

    public async Task<List<T>> GetByTypeAndCategoryIdAsync<T>(int typeId, Guid categoryId) where T : class
    {
        var records = await clientRegistrationsTable.GetByTypeAndCategoryIdAsync(typeId, categoryId);
        return records.Select(record => OdinSystemSerializer.DeserializeOrThrow<T>(record.value)).ToList();
    }


    public async Task DeleteAsync(Guid tokenId)
    {
        await clientRegistrationsTable.DeleteAsync(tokenId);
    }
}