using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.AppNotification;

namespace Odin.Services.Membership.Connections;

public class CircleNetworkStorage
{
    private readonly Guid _icrKeyStorageId = Guid.Parse("42739542-22eb-49cb-b43a-110acf2b18a1");
    private readonly CircleMembershipService _circleMembershipService;
    private readonly TenantSystemStorage _tenantSystemStorage;

    private readonly SingleKeyValueStorage _icrKeyStorage;

    private readonly SingleKeyValueStorage _peerIcrClientStorage;

    public CircleNetworkStorage(TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _circleMembershipService = circleMembershipService;

        const string icrKeyStorageContextKey = "9035bdfa-e25d-4449-82a5-fd8132332dea";
        _icrKeyStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(icrKeyStorageContextKey));

        const string peerIcrClientStorageContextKey = "0ee6aeff-2c21-412d-8050-1a47d025af46";
        _peerIcrClientStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(peerIcrClientStorageContextKey));
    }

    public async Task<IdentityConnectionRegistration> GetAsync(OdinId odinId)
    {
        var record = await _tenantSystemStorage.Connections.GetAsync(odinId);

        if (null == record)
        {
            return null;
        }

        return await MapFromStorageAsync(record);
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public async Task UpsertAsync(IdentityConnectionRegistration icr, IOdinContext odinContext)
    {
        var icrAccessRecord = new IcrAccessRecord()
        {
            AccessGrant = icr.AccessGrant,
            OriginalContactData = icr.OriginalContactData,
            EncryptedClientAccessToken = icr.EncryptedClientAccessToken.EncryptedData
        };

        // TODO CONNECTIONS
        //db.CreateCommitUnitOfWork(() =>
        //{
        var odinHashId = icr.OdinId.ToHashId();

        //Reconcile circle grants in the table
        await _circleMembershipService.DeleteMemberFromAllCirclesAsync(icr.OdinId, DomainType.Identity);
        foreach (var (circleId, circleGrant) in icr.AccessGrant.CircleGrants)
        {
            var circleMembers =
                await _circleMembershipService.GetDomainsInCircleAsync(circleId, odinContext, overrideHack: true);
            var isMember = circleMembers.Any(d => OdinId.ToHashId(d.Domain) == icr.OdinId.ToHashId());

            if (!isMember)
            {
                await _circleMembershipService.AddCircleMemberAsync(circleId, icr.OdinId, circleGrant, DomainType.Identity);
            }
        }

        // remove all app grants, 
        await _tenantSystemStorage.AppGrants.DeleteByIdentityAsync(odinHashId);

        // Now write the latest
        foreach (var (appId, appCircleGrantDictionary) in icr.AccessGrant.AppGrants)
        {
            foreach (var (circleId, appCircleGrant) in appCircleGrantDictionary)
            {
                await _tenantSystemStorage.AppGrants.UpsertAsync(new AppGrantsRecord()
                {
                    odinHashId = odinHashId,
                    appId = appId,
                    circleId = circleId,
                    data = OdinSystemSerializer.Serialize(appCircleGrant).ToUtf8ByteArray()
                });
            }
        }

        // Clearing these so they are not serialized on
        // the connections record.  Instead, we give them
        // each their own table
        icrAccessRecord.AccessGrant.AppGrants.Clear();
        icrAccessRecord.AccessGrant.CircleGrants.Clear();

        var record = new ConnectionsRecord()
        {
            identity = icr.OdinId,
            status = (int)icr.Status,
            modified = UnixTimeUtcUnique.Now(),
            displayName = "",
            data = OdinSystemSerializer.Serialize(icrAccessRecord).ToUtf8ByteArray()
        };

        await _tenantSystemStorage.Connections.UpsertAsync(record);

        //});
    }

    public async Task DeleteAsync(OdinId odinId)
    {
        // TODO CONNECTIONS
        //db.CreateCommitUnitOfWork(() =>  {
        await _tenantSystemStorage.Connections.DeleteAsync(odinId);
        await _tenantSystemStorage.AppGrants.DeleteByIdentityAsync(odinId.ToHashId());
        await _circleMembershipService.DeleteMemberFromAllCirclesAsync(odinId, DomainType.Identity);
        // });
    }

    public async Task SavePeerIcrClientAsync(PeerIcrClient client)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        await _peerIcrClientStorage.UpsertAsync(db, client.AccessRegistration.Id, client);
    }

    public async Task<PeerIcrClient> GetPeerIcrClientAsync(Guid accessRegId)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        return await _peerIcrClientStorage.GetAsync<PeerIcrClient>(db, accessRegId);
    }

    public async Task<(IEnumerable<IdentityConnectionRegistration>, UnixTimeUtcUnique? nextCursor)> GetListAsync(int count,
        UnixTimeUtcUnique? cursor, ConnectionStatus connectionStatus)
    {
        var adjustedCursor = cursor.HasValue ? cursor.GetValueOrDefault().uniqueTime == 0 ? null : cursor : null;
        var (records, nextCursor) =
            await _tenantSystemStorage.Connections.PagingByCreatedAsync(count, (int)connectionStatus, adjustedCursor);
        var mappedRecords = await Task.WhenAll(records.Select(record => MapFromStorageAsync(record)));
        return (mappedRecords, nextCursor);
        // WAS: return (records.Select(record => MapFromStorageAsync(record)), nextCursor);
    }


    /// <summary>
    /// Creates a new icr key; fails if one already exists
    /// </summary>
    /// <param name="masterKey"></param>
    /// <exception cref="OdinClientException"></exception>
    public async Task CreateIcrKeyAsync(SensitiveByteArray masterKey)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        var existingKey = await _icrKeyStorage.GetAsync<IcrKeyRecord>(db, _icrKeyStorageId);
        if (null != existingKey)
        {
            throw new OdinClientException("IcrKey already exists");
        }

        var icrKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

        var record = new IcrKeyRecord()
        {
            MasterKeyEncryptedIcrKey = new SymmetricKeyEncryptedAes(masterKey, icrKey),
            Created = UnixTimeUtc.Now()
        };

        await _icrKeyStorage.UpsertAsync(db, _icrKeyStorageId, record);
    }

    public async Task<SymmetricKeyEncryptedAes> GetMasterKeyEncryptedIcrKeyAsync()
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        var key = await _icrKeyStorage.GetAsync<IcrKeyRecord>(db, _icrKeyStorageId);
        return key?.MasterKeyEncryptedIcrKey;
    }

    private async Task<IdentityConnectionRegistration> MapFromStorageAsync(ConnectionsRecord record)
    {
        var json = record.data.ToStringFromUtf8Bytes();
        var data = OdinSystemSerializer.Deserialize<IcrAccessRecord>(json);

        var odinHashId = record.identity.ToHashId();

        var circleGrants = await _circleMembershipService.GetCirclesGrantsByDomainAsync(record.identity, DomainType.Identity);
        foreach (var circleGrant in circleGrants)
        {
            data.AccessGrant.CircleGrants.Add(circleGrant.CircleId, circleGrant);
        }

        var allAppGrants = await _tenantSystemStorage.AppGrants.GetByOdinHashIdAsync(odinHashId) ?? new List<AppGrantsRecord>();

        foreach (var appGrantRecord in allAppGrants)
        {
            var appCircleGrant = OdinSystemSerializer.Deserialize<AppCircleGrant>(appGrantRecord.data.ToStringFromUtf8Bytes());
            data.AccessGrant.AddUpdateAppCircleGrant(appCircleGrant);
        }

        // data.AccessGrant.AppGrants
        return new IdentityConnectionRegistration()
        {
            OdinId = record.identity,
            Status = (ConnectionStatus)record.status,
            Created = record.created.ToUnixTimeUtc().milliseconds,
            LastUpdated = record.modified.HasValue ? record.modified.Value.ToUnixTimeUtc().milliseconds : 0,
            AccessGrant = data.AccessGrant,
            OriginalContactData = data.OriginalContactData,
            EncryptedClientAccessToken = new EncryptedClientAccessToken()
            {
                EncryptedData = data.EncryptedClientAccessToken
            }
        };
    }
}

public class IcrKeyRecord
{
    public SymmetricKeyEncryptedAes MasterKeyEncryptedIcrKey { get; set; }
    public UnixTimeUtc Created { get; set; }
}

public class IcrAccessRecord
{
    /// <summary>
    /// The drives and permissions granted to this connection
    /// </summary>
    public AccessExchangeGrant AccessGrant { get; set; }

    // public byte[] EncryptedClientAccessToken { get; set; }
    public SymmetricKeyEncryptedAes EncryptedClientAccessToken { get; set; }
    public ContactRequestData OriginalContactData { get; set; }
}