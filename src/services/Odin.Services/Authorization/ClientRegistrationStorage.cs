#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;
using Odin.Services.Util;

namespace Odin.Services.Authorization;

/// <summary>
/// Stores the server-side aspect of a <see cref="ClientAccessToken"/>
/// </summary>
public class ClientRegistrationStorage(TableClientRegistrations clientRegistrationsTable, ILogger<ClientRegistrationStorage> logger, OdinConfiguration configuration)
{
    
    private static readonly TimeSpan Window = TimeSpan.FromDays(1);

    public async Task SaveAsync(IClientRegistration clientRegistration)
    {
        OdinValidationUtils.AssertNotEmptyGuid(clientRegistration.Id, "Client registration must have an id");

        var threshold = configuration.Host.ClientRegistrationThreshold;
        var windowThreshold = configuration.Host.ClientRegistrationWindowThreshold;
        
        var now = DateTime.UtcNow;
        var previousRegistrations = await clientRegistrationsTable.GetByTypeAndIssuedToAsync(
            clientRegistration.Type, clientRegistration.IssuedTo);
        var byCategory = previousRegistrations.Where(r => r.categoryId == clientRegistration.CategoryId);
        var recentCount = byCategory.Count(r => (now - r.created.ToDateTime()) <= Window);

        if (previousRegistrations.Count > threshold)
        {
            logger.LogError(
                "Threshold of {threshold} has been broken. ({count}) client registrations of " +
                "type [{type}] and category [{category}] created by {issuedTo}",
                threshold,
                previousRegistrations.Count,
                clientRegistration.Type,
                clientRegistration.CategoryId,
                clientRegistration.IssuedTo);
        }

        if (recentCount > windowThreshold)
        {
            logger.LogError(
                "Threshold of {threshold} has been broken. ({count}) client registrations of " +
                "type [{type}] and category [{category}] created by {issuedTo} within {window}",
                windowThreshold,
                recentCount,
                clientRegistration.Type,
                clientRegistration.CategoryId,
                clientRegistration.IssuedTo,
                FormatTimespan());
        }

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

    public async Task ExtendLife(Guid id)
    {
        var record = await clientRegistrationsTable.GetAsync(id);
        if (null != record)
        {
            record.expiresAt = UnixTimeUtc.Now().AddSeconds(record.ttl);
        }

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


    private static string FormatTimespan()
    {
        var span = Window;
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}d";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes}m";
        return $"{(int)span.TotalSeconds}s";
    }
}