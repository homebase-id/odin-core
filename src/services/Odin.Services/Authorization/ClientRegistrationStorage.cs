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
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Util;

namespace Odin.Services.Authorization;

/// <summary>
/// Stores the server-side aspect of a <see cref="ClientAccessToken"/>
/// </summary>
public class ClientRegistrationStorage(
    TableClientRegistrations clientRegistrationsTable,
    ILogger<ClientRegistrationStorage> logger,
    OdinConfiguration configuration,
    OdinContextCache odinContextCache)
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(1);

    /// <summary>
    /// How stale a sliding expiry may get before <see cref="ExtendLife"/> writes it again.
    /// </summary>
    public static readonly TimeSpan ExtendLifeGranularity = TimeSpan.FromDays(1);

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
            logger.LogDebug(
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
            logger.LogDebug(
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

    /// <summary>
    /// Restarts the registration's lifetime from now, so a client still in use is never cut off.
    /// </summary>
    /// <remarks>
    /// Throttled: a lifetime restarted within the last <see cref="ExtendLifeGranularity"/> is left
    /// as it is. Callers reach this on every request that validates a token, and moving a six-month
    /// date by a few minutes is not worth a write per request.
    /// </remarks>
    public async Task ExtendLife(Guid id)
    {
        var record = await clientRegistrationsTable.GetAsync(id);
        if (record != null)
        {
            await ExtendLife(record);
        }
    }

    /// <summary>
    /// As above, for a caller that already holds the row (from <see cref="GetWithRowAsync{T}"/>),
    /// so restarting costs no second read.
    /// </summary>
    public async Task ExtendLife(ClientRegistrationsRecord record)
    {
        var restarted = UnixTimeUtc.Now().AddSeconds(record.ttl);
        if (restarted - record.expiresAt < ExtendLifeGranularity)
        {
            return;
        }

        record.expiresAt = restarted;
        await clientRegistrationsTable.UpsertAsync(record);
    }

    public async Task<T?> GetAsync<T>(Guid id) where T : class
    {
        var (registration, _) = await GetWithRowAsync<T>(id);
        return registration;
    }

    /// <summary>
    /// The registration and the row it came from, so a caller can act on the row's expiry -- cap a
    /// cache entry at it, or <see cref="ExtendLife(ClientRegistrationsRecord)"/> -- without reading
    /// it again. An expired row is dropped on the way past and comes back as null.
    /// </summary>
    public async Task<(T? Registration, ClientRegistrationsRecord? Row)> GetWithRowAsync<T>(Guid id) where T : class
    {
        var record = await clientRegistrationsTable.GetAsync(id);

        if (record == null)
        {
            return (null, null);
        }

        if (!IsLive(record, UnixTimeUtc.Now()))
        {
            await DeleteAsync(id);
            return (null, null);
        }

        return (OdinSystemSerializer.Deserialize<T>(record.value), record);
    }

    /// <summary>
    /// Live registrations only: an expired row is not a client any more, whether or not a read has
    /// dropped it yet.
    /// </summary>
    public async Task<List<T>> GetByTypeAndCategoryIdAsync<T>(int typeId, Guid categoryId) where T : class
    {
        var records = await clientRegistrationsTable.GetByTypeAndCategoryIdAsync(typeId, categoryId);
        return LiveOnly<T>(records);
    }

    /// <summary>
    /// Live registrations issued to one party, for a registration type whose category does not
    /// vary per party (a YouAuth domain client, say).
    /// </summary>
    public async Task<List<T>> GetByTypeAndIssuedToAsync<T>(int typeId, string issuedTo) where T : class
    {
        var records = await clientRegistrationsTable.GetByTypeAndIssuedToAsync(typeId, issuedTo);
        return LiveOnly<T>(records);
    }

    private static List<T> LiveOnly<T>(IEnumerable<ClientRegistrationsRecord> records) where T : class
    {
        var now = UnixTimeUtc.Now();
        return records
            .Where(record => IsLive(record, now))
            .Select(record => OdinSystemSerializer.DeserializeOrThrow<T>(record.value))
            .ToList();
    }

    private static bool IsLive(ClientRegistrationsRecord record, UnixTimeUtc now) => record.expiresAt >= now;

    public async Task DeleteAsync(Guid tokenId)
    {
        await clientRegistrationsTable.DeleteAsync(tokenId);
        await odinContextCache.ResetAsync();
    }

    /// <summary>
    /// Deletes several, dropping the cached contexts once rather than once per row.
    /// </summary>
    public async Task DeleteManyAsync(IEnumerable<Guid> tokenIds)
    {
        foreach (var tokenId in tokenIds)
        {
            await clientRegistrationsTable.DeleteAsync(tokenId);
        }

        await odinContextCache.ResetAsync();
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