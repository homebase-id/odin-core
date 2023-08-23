using System.Collections.Generic;
using System.Linq;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Services.Membership.Connections;

public class CircleNetworkStorage
{
    private readonly GuidId _icrKeyStorageId = GuidId.FromString("icr_key");
    private readonly CircleMembershipService _circleMembershipService;
    private readonly TenantSystemStorage _tenantSystemStorage;

    public CircleNetworkStorage(TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _circleMembershipService = circleMembershipService;
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
            _circleMembershipService.DeleteMemberFromAllCircles(icr.OdinId);
            foreach (var (circleId, circleGrant) in icr.AccessGrant.CircleGrants)
            {
                var circleMembers = _circleMembershipService.GetDomainsInCircle(circleId);
                var isMember = circleMembers.Any(d => OdinId.ToHashId(d.Domain) == icr.OdinId.ToHashId());

                if (!isMember)
                {
                    _circleMembershipService.AddCircleMember(circleId, icr.OdinId, circleGrant, DomainType.Identity);
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
            _circleMembershipService.DeleteMemberFromAllCircles(odinId);
        }
    }

    public IEnumerable<IdentityConnectionRegistration> GetList(int count, UnixTimeUtcUnique? cursor, out UnixTimeUtcUnique? nextCursor,
        ConnectionStatus connectionStatus)
    {
        var adjustedCursor = cursor.HasValue ? cursor.GetValueOrDefault().uniqueTime == 0 ? null : cursor : null;
        var records = _tenantSystemStorage.Connections.PagingByCreated(count, (int)connectionStatus, adjustedCursor, out nextCursor);
        return records.Select(MapFromStorage);
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

        var circleGrants = _circleMembershipService.GetCirclesByDomain(record.identity);
        foreach (var circleGrant in circleGrants)
        {
            data.AccessGrant.CircleGrants.Add(circleGrant.CircleId, circleGrant);
        }

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