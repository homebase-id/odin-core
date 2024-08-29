using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

namespace Odin.Services.Membership.Connections;

public class CircleNetworkStorage
{
    private readonly Guid _icrKeyStorageId = Guid.Parse("42739542-22eb-49cb-b43a-110acf2b18a1");
    private readonly CircleMembershipService _circleMembershipService;
    private readonly TenantSystemStorage _tenantSystemStorage;

    private readonly SingleKeyValueStorage _icrKeyStorage;

    public CircleNetworkStorage(TenantSystemStorage tenantSystemStorage, CircleMembershipService circleMembershipService)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _circleMembershipService = circleMembershipService;

        const string icrKeyStorageContextKey = "9035bdfa-e25d-4449-82a5-fd8132332dea";
        _icrKeyStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(icrKeyStorageContextKey));
    }

    public IdentityConnectionRegistration Get(OdinId odinId, DatabaseConnection cn)
    {
        var record = _tenantSystemStorage.Connections.Get(odinId);

        if (null == record)
        {
            return null;
        }

        return MapFromStorage(record, cn);
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public void Upsert(IdentityConnectionRegistration icr, IOdinContext odinContext, DatabaseConnection cn)
    {
        var icrAccessRecord = new IcrAccessRecord()
        {
            AccessGrant = icr.AccessGrant,
            OriginalContactData = icr.OriginalContactData,
            EncryptedClientAccessToken = icr.EncryptedClientAccessToken.EncryptedData
        };

        cn.CreateCommitUnitOfWork(() =>
        {
            var odinHashId = icr.OdinId.ToHashId();

            //Reconcile circle grants in the table
            _circleMembershipService.DeleteMemberFromAllCircles(icr.OdinId, DomainType.Identity, cn);
            foreach (var (circleId, circleGrant) in icr.AccessGrant.CircleGrants)
            {
                var circleMembers =
                    _circleMembershipService.GetDomainsInCircle(circleId, odinContext, cn, overrideHack: true);
                var isMember = circleMembers.Any(d => OdinId.ToHashId(d.Domain) == icr.OdinId.ToHashId());

                if (!isMember)
                {
                    _circleMembershipService.AddCircleMember(circleId, icr.OdinId, circleGrant, DomainType.Identity, cn);
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
        });
    }

    public void Delete(OdinId odinId, DatabaseConnection cn)
    {
        cn.CreateCommitUnitOfWork(() =>
        {
            _tenantSystemStorage.Connections.Delete(odinId);
            _tenantSystemStorage.AppGrants.DeleteByIdentity(odinId.ToHashId());
            _circleMembershipService.DeleteMemberFromAllCircles(odinId, DomainType.Identity, cn);
        });
    }

    public IEnumerable<IdentityConnectionRegistration> GetList(int count, UnixTimeUtcUnique? cursor, out UnixTimeUtcUnique? nextCursor,
        ConnectionStatus connectionStatus, DatabaseConnection cn)
    {
        var adjustedCursor = cursor.HasValue ? cursor.GetValueOrDefault().uniqueTime == 0 ? null : cursor : null;
        var records = _tenantSystemStorage.Connections.PagingByCreated(count, (int)connectionStatus, adjustedCursor, out nextCursor);
        return records.Select(record => MapFromStorage(record, cn));
    }


    /// <summary>
    /// Creates a new icr key; fails if one already exists
    /// </summary>
    /// <param name="masterKey"></param>
    /// <exception cref="OdinClientException"></exception>
    public void CreateIcrKey(SensitiveByteArray masterKey, DatabaseConnection cn)
    {
        var existingKey = _icrKeyStorage.Get<IcrKeyRecord>(cn, _icrKeyStorageId);
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

        _icrKeyStorage.Upsert(cn, _icrKeyStorageId, record);
    }

    public SymmetricKeyEncryptedAes GetMasterKeyEncryptedIcrKey(DatabaseConnection cn)
    {
        var key = _icrKeyStorage.Get<IcrKeyRecord>(cn, _icrKeyStorageId);
        return key?.MasterKeyEncryptedIcrKey;
    }

    private IdentityConnectionRegistration MapFromStorage(ConnectionsRecord record, DatabaseConnection cn)
    {
        var json = record.data.ToStringFromUtf8Bytes();
        var data = OdinSystemSerializer.Deserialize<IcrAccessRecord>(json);

        var odinHashId = record.identity.ToHashId();

        var circleGrants = _circleMembershipService.GetCirclesGrantsByDomain(record.identity, DomainType.Identity, cn);
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