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
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Membership.Connections;

public class CircleNetworkStorage
{
    private readonly Guid _icrKeyStorageId = Guid.Parse("42739542-22eb-49cb-b43a-110acf2b18a1");
    private readonly CircleMembershipService _circleMembershipService;
    private readonly TableConnections _tblConnections;
    private readonly TableAppGrants _tblAppGrants;
    private readonly TableKeyValue _tblKeyValue;

    private readonly SingleKeyValueStorage _icrKeyStorage;
    
    private readonly SingleKeyValueStorage _peerIcrClientStorage;

    public CircleNetworkStorage(
        
        CircleMembershipService circleMembershipService,
        TableConnections tblConnections,
        TableAppGrants tblAppGrants,
        TableKeyValue tblKeyValue)
    {
        _circleMembershipService = circleMembershipService;
        _tblConnections = tblConnections;
        _tblAppGrants = tblAppGrants;
        _tblKeyValue = tblKeyValue;

        const string icrKeyStorageContextKey = "9035bdfa-e25d-4449-82a5-fd8132332dea";
        _icrKeyStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(icrKeyStorageContextKey));
        
        const string peerIcrClientStorageContextKey = "0ee6aeff-2c21-412d-8050-1a47d025af46";
        _peerIcrClientStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(peerIcrClientStorageContextKey));
    }

    public async Task<IdentityConnectionRegistration> GetAsync(OdinId odinId)
    {
        var record = await _tblConnections.GetAsync(odinId);

        if (null == record)
        {
            return null;
        }

        return await MapFromStorageAsync(record);
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public async Task UpsertAsync(IdentityConnectionRegistration icr, IOdinContext odinContext)
    {
        var icrAccessRecord = MapToStorageIcrAccessRecord(icr);

        //TODO CONNECTIONS
        // db.CreateCommitUnitOfWork(() =>
        {
            var odinHashId = icr.OdinId.ToHashId();

            //Reconcile circle grants in the table
            await _circleMembershipService.DeleteMemberFromAllCirclesAsync(icr.OdinId, DomainType.Identity);
            foreach (var (circleId, circleGrant) in icr.AccessGrant?.CircleGrants ?? [])
            {
                var circleMembers = await _circleMembershipService.GetDomainsInCircleAsync(circleId, odinContext, overrideHack: true);
                var isMember = circleMembers.Any(d => OdinId.ToHashId(d.Domain) == icr.OdinId.ToHashId());

                if (!isMember)
                {
                    await _circleMembershipService.AddCircleMemberAsync(circleId, icr.OdinId, circleGrant, DomainType.Identity);
                }
            }

            // remove all app grants, 
            await _tblAppGrants.DeleteByIdentityAsync(odinHashId);

            // Now write the latest
            foreach (var (appId, appCircleGrantDictionary) in icr.AccessGrant?.AppGrants ?? [])
            {
                foreach (var (circleId, appCircleGrant) in appCircleGrantDictionary)
                {
                    await _tblAppGrants.UpsertAsync(new AppGrantsRecord()
                    {
                        odinHashId = odinHashId,
                        appId = appId,
                        circleId = circleId,
                        data = OdinSystemSerializer.Serialize(appCircleGrant).ToUtf8ByteArray()
                    });
                }
            }

            var record = ToConnectionsRecord(icr.OdinId, icr.Status, icrAccessRecord);
            await _tblConnections.UpsertAsync(record);
        }
        //);
    }

    public async Task UpdateKeyStoreKeyAsync(OdinId identity, ConnectionStatus status, SymmetricKeyEncryptedAes masterKeyEncryptedKsk)
    {
        var existingRecord = await GetAsync(identity);
        var icrAccessRecord = MapToStorageIcrAccessRecord(existingRecord);

        icrAccessRecord.AccessGrant.MasterKeyEncryptedKeyStoreKey = masterKeyEncryptedKsk;
        icrAccessRecord.WeakKeyStoreKey = null;

        var record = ToConnectionsRecord(identity, status, icrAccessRecord);
        await _tblConnections.UpdateAsync(record);
    }

    public async Task UpdateClientAccessTokenAsync(OdinId identity, ConnectionStatus status, EncryptedClientAccessToken encryptedCat)
    {
        var existingRecord = await GetAsync(identity);
        var icrAccessRecord = MapToStorageIcrAccessRecord(existingRecord);

        icrAccessRecord.EncryptedClientAccessToken = encryptedCat.EncryptedData;
        icrAccessRecord.WeakClientAccessToken = null;

        var record = ToConnectionsRecord(identity, status, icrAccessRecord);
        await _tblConnections.UpdateAsync(record);
    }

    public async Task UpdateVerificationHashAsync(OdinId identity, ConnectionStatus status, byte[] hash)
    {
        var existingRecord = await GetAsync(identity);
        var icrAccessRecord = MapToStorageIcrAccessRecord(existingRecord);

        icrAccessRecord.VerificationHash64 = hash.ToBase64();
        var record = ToConnectionsRecord(identity, status, icrAccessRecord);
        await _tblConnections.UpdateAsync(record);
    }

    public async Task DeleteAsync(OdinId odinId)
    {
        //TODO CONNECTIONS
        // db.CreateCommitUnitOfWork(() =>
        {
            await _tblConnections.DeleteAsync(odinId);
            await _tblAppGrants.DeleteByIdentityAsync(odinId.ToHashId());
            await _circleMembershipService.DeleteMemberFromAllCirclesAsync(odinId, DomainType.Identity);
        }
        //);
    }

    public async Task<(IEnumerable<IdentityConnectionRegistration>, UnixTimeUtcUnique? nextCursor)> GetListAsync(int count, UnixTimeUtcUnique? cursor,
        ConnectionStatus connectionStatus)
    {
        var adjustedCursor = cursor.HasValue ? cursor.GetValueOrDefault().uniqueTime == 0 ? null : cursor : null;
        var (records, nextCursor) = await _tblConnections.PagingByCreatedAsync(count, (int)connectionStatus, adjustedCursor);
        var mappedRecords = await Task.WhenAll(records.Select(MapFromStorageAsync));
        return (mappedRecords, nextCursor);
    }

    /// <summary>
    /// Creates a new icr key; fails if one already exists
    /// </summary>
    /// <param name="masterKey"></param>
    /// <exception cref="OdinClientException"></exception>
    public async Task CreateIcrKeyAsync(SensitiveByteArray masterKey)
    {
        
        var existingKey = await _icrKeyStorage.GetAsync<IcrKeyRecord>(_tblKeyValue, _icrKeyStorageId);
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

        await _icrKeyStorage.UpsertAsync(_tblKeyValue, _icrKeyStorageId, record);
    }

    public async Task<SymmetricKeyEncryptedAes> GetMasterKeyEncryptedIcrKeyAsync()
    {
        
        var key = await _icrKeyStorage.GetAsync<IcrKeyRecord>(_tblKeyValue, _icrKeyStorageId);
        return key?.MasterKeyEncryptedIcrKey;
    }

    
    public async Task SavePeerIcrClientAsync(PeerIcrClient client)
    {
        
        await _peerIcrClientStorage.UpsertAsync(_tblKeyValue, client.AccessRegistration.Id, client);
    }

    public async Task<PeerIcrClient> GetPeerIcrClientAsync(Guid accessRegId)
    {
        
        return await _peerIcrClientStorage.GetAsync<PeerIcrClient>(_tblKeyValue, accessRegId);
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

        var allAppGrants = await _tblAppGrants.GetByOdinHashIdAsync(odinHashId) ?? new List<AppGrantsRecord>();

        foreach (var appGrantRecord in allAppGrants)
        {
            var appCircleGrant = OdinSystemSerializer.Deserialize<AppCircleGrant>(appGrantRecord.data.ToStringFromUtf8Bytes());
            data.AccessGrant.AddUpdateAppCircleGrant(appCircleGrant);
        }

        ConnectionRequestOrigin connectionOrigin = string.IsNullOrEmpty(data.ConnectionOrigin)
            ? ConnectionRequestOrigin.IdentityOwner
            : Enum.Parse<ConnectionRequestOrigin>(data.ConnectionOrigin);

        OdinId? introducerOdinId = string.IsNullOrEmpty(data.IntroducerOdinId?.Trim()) ? null : (OdinId)data.IntroducerOdinId;

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
            },

            TemporaryWeakClientAccessToken = string.IsNullOrEmpty(data.WeakClientAccessToken)
                ? null
                : OdinSystemSerializer.Deserialize<EccEncryptedPayload>(data.WeakClientAccessToken),

            TempWeakKeyStoreKey = string.IsNullOrEmpty(data.WeakKeyStoreKey)
                ? null
                : OdinSystemSerializer.Deserialize<EccEncryptedPayload>(data.WeakKeyStoreKey),

            ConnectionRequestOrigin = connectionOrigin,
            IntroducerOdinId = introducerOdinId,
            VerificationHash = data.VerificationHash64?.ToUtf8ByteArray()
        };
    }

    private static ConnectionsRecord ToConnectionsRecord(OdinId odinId, ConnectionStatus status, IcrAccessRecord icrAccessRecord)
    {
        // Clearing these so they are not serialized on
        // the connections record.  Instead, we give them
        // each their own table
        icrAccessRecord.AccessGrant?.AppGrants?.Clear();
        icrAccessRecord.AccessGrant?.CircleGrants?.Clear();

        var record = new ConnectionsRecord()
        {
            identity = odinId,
            status = (int)status,
            modified = UnixTimeUtcUnique.Now(),
            displayName = "",
            data = OdinSystemSerializer.Serialize(icrAccessRecord).ToUtf8ByteArray()
        };
        return record;
    }

    private static IcrAccessRecord MapToStorageIcrAccessRecord(IdentityConnectionRegistration icr)
    {
        var icrAccessRecord = new IcrAccessRecord
        {
            AccessGrant = icr.AccessGrant,
            OriginalContactData = icr.OriginalContactData,
            IntroducerOdinId = icr.IntroducerOdinId,
            VerificationHash64 = icr.VerificationHash?.ToBase64(),
            ConnectionOrigin = Enum.GetName(icr.ConnectionRequestOrigin),
            EncryptedClientAccessToken = icr.EncryptedClientAccessToken?.EncryptedData,
            WeakClientAccessToken = icr.TemporaryWeakClientAccessToken == null
                ? ""
                : OdinSystemSerializer.Serialize(icr.TemporaryWeakClientAccessToken),
            WeakKeyStoreKey = icr.TempWeakKeyStoreKey == null ? "" : OdinSystemSerializer.Serialize(icr.TempWeakKeyStoreKey)
        };
        return icrAccessRecord;
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

    public string WeakClientAccessToken { get; set; }

    public string WeakKeyStoreKey { get; set; }

    public ContactRequestData OriginalContactData { get; set; }
    public string IntroducerOdinId { get; init; }

    public string VerificationHash64 { get; set; }
    public string ConnectionOrigin { get; init; }
}