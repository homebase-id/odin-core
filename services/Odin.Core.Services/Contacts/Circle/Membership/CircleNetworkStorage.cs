using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Requests;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Services.Contacts.Circle.Membership;

public class CircleNetworkStorage
{
    private readonly GuidId _icrKeyStorageId = GuidId.FromString("icr_key");
    private readonly TenantSystemStorage _tenantSystemStorage;

    public CircleNetworkStorage(TenantSystemStorage tenantSystemStorage)
    {
        _tenantSystemStorage = tenantSystemStorage;
    }

    public IdentityConnectionRegistration Get(OdinId odinId)
    {
        var record = _tenantSystemStorage.Connections.Get(odinId);

        if (null == record)
        {
            return null;
        }

        return MapFromStorage(record);
    }

    public void Upsert(IdentityConnectionRegistration icr)
    {
        var icrAccessRecord = new IcrAccessRecord()
        {
            AccessGrant = icr.AccessGrant,
            OriginalContactData = icr.OriginalContactData,
            EncryptedClientAccessToken = icr.EncryptedClientAccessToken.EncryptedData
        };

        using (_tenantSystemStorage.CreateCommitUnitOfWork())
        {
            var odinHashId = icr.OdinId.ToHashId();

            //Reconcile circle grants in the table
            _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles(new List<Guid> { odinHashId });
            foreach (var (circleId, circleGrant) in icr.AccessGrant.CircleGrants)
            {
                var circleMembers = _tenantSystemStorage.CircleMemberStorage.GetCircleMembers(circleId);
                var isMember = circleMembers.Any(item => item.memberId == icr.OdinId.ToHashId());

                if (!isMember)
                {
                    var circleMemberRecord = new CircleMemberRecord()
                    {
                        circleId = circleId,
                        memberId = icr.OdinId.ToHashId(),
                        data = OdinSystemSerializer.Serialize(new CircleMemberStorageData
                        {
                            OdinId = icr.OdinId,
                            CircleGrant = circleGrant
                        }).ToUtf8ByteArray()
                    };

                    _tenantSystemStorage.CircleMemberStorage.AddCircleMembers(new List<CircleMemberRecord>() { circleMemberRecord });
                }
            }

            // remove all app grants, 
            _tenantSystemStorage.AppGrants.DeleteByIdentity(odinHashId);

            // Now write the latest
            foreach (var (appId, appCircleGrantDictionary) in icr.AccessGrant.AppGrants)
            {
                foreach (var (circleId, appCircleGrant) in appCircleGrantDictionary)
                {
                    _tenantSystemStorage.AppGrants.Upsert(new AppGrantsRecord()
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

            _tenantSystemStorage.Connections.Upsert(record);
        }
    }

    public void Delete(OdinId odinId)
    {
        using (_tenantSystemStorage.CreateCommitUnitOfWork())
        {
            _tenantSystemStorage.Connections.Delete(odinId);
            _tenantSystemStorage.AppGrants.DeleteByIdentity(odinId.ToHashId());
            _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles(new List<Guid>() { odinId.ToHashId() });
        }
    }

    public IEnumerable<IdentityConnectionRegistration> GetList(int count, UnixTimeUtcUnique? cursor, out UnixTimeUtcUnique? nextCursor,
        ConnectionStatus connectionStatus)
    {
        var adjustedCursor = cursor.HasValue ? cursor.GetValueOrDefault().uniqueTime == 0 ? null : cursor : null;
        var records = _tenantSystemStorage.Connections.PagingByCreated(count, (int)connectionStatus, adjustedCursor, out nextCursor);
        return records.Select(MapFromStorage);
    }
    
    public List<OdinId> GetCircleMembers(GuidId circleId)
    {
        //Note: this list is a cache of members for a circle.  the source of truth is the
        //IdentityConnectionRegistration.AccessExchangeGrant.CircleGrants property for each OdinId
        var memberBytesList = _tenantSystemStorage.CircleMemberStorage.GetCircleMembers(circleId);
        var result = memberBytesList.Select(item =>
        {
            var data = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(item.data.ToStringFromUtf8Bytes());
            return data.OdinId;
        }).ToList();

        return result;
    }

    /// <summary>
    /// Creates a new icr key; fails if one already exists
    /// </summary>
    /// <param name="masterKey"></param>
    /// <exception cref="OdinClientException"></exception>
    public void CreateIcrKey(SensitiveByteArray masterKey)
    {
        var existingKey = _tenantSystemStorage.SingleKeyValueStorage.Get<IcrKeyRecord>(_icrKeyStorageId);
        if (null != existingKey)
        {
            throw new OdinClientException("IcrKey already exists");
        }

        var icrKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

        var record = new IcrKeyRecord()
        {
            MasterKeyEncryptedIcrKey = new SymmetricKeyEncryptedAes(ref masterKey, ref icrKey),
            Created = UnixTimeUtc.Now()
        };

        _tenantSystemStorage.SingleKeyValueStorage.Upsert(_icrKeyStorageId, record);
    }

    public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey()
    {
        var key = _tenantSystemStorage.SingleKeyValueStorage.Get<IcrKeyRecord>(_icrKeyStorageId);
        return key?.MasterKeyEncryptedIcrKey;
    }
    
    private IdentityConnectionRegistration MapFromStorage(ConnectionsRecord record)
    {
        var json = record.data.ToStringFromUtf8Bytes();
        var data = OdinSystemSerializer.Deserialize<IcrAccessRecord>(json);

        var odinHashId = record.identity.ToHashId();

        var circleMemberRecords = _tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(odinHashId);
        foreach (var circleMemberRecord in circleMemberRecords)
        {
            var sd = OdinSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data.ToStringFromUtf8Bytes());
            data.AccessGrant.CircleGrants.Add(circleMemberRecord.circleId, sd.CircleGrant);
        }

        //TODO: this only returns one app grant. i need the whole list
        var allAppGrants = _tenantSystemStorage.AppGrants.GetByOdinHashId(odinHashId) ?? new List<AppGrantsRecord>();

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
            Created = record.created.uniqueTime,
            LastUpdated = record.modified?.uniqueTime ?? default,
            AccessGrant = data.AccessGrant,
            OriginalContactData = data.OriginalContactData,

            // ClientAccessTokenId = data.ClientAccessTokenId,
            // ClientAccessTokenHalfKey = data.ClientAccessTokenHalfKey,
            // ClientAccessTokenSharedSecret = data.ClientAccessTokenSharedSecret,

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