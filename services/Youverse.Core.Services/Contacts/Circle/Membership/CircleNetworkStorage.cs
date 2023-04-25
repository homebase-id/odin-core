using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

public class CircleNetworkStorage
{
    private readonly GuidId _key = GuidId.FromString("circle_network_storage");
    private readonly TenantSystemStorage _tenantSystemStorage;
    private readonly CircleDefinitionService _circleDefinitionService;

    public CircleNetworkStorage(TenantSystemStorage tenantSystemStorage, CircleDefinitionService circleDefinitionService)
    {
        _tenantSystemStorage = tenantSystemStorage;
        _circleDefinitionService = circleDefinitionService;
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
            ClientAccessTokenId = icr.ClientAccessTokenId,
            ClientAccessTokenHalfKey = icr.ClientAccessTokenHalfKey,
            ClientAccessTokenSharedSecret = icr.ClientAccessTokenSharedSecret,
            OriginalContactData = icr.OriginalContactData
        };

        using (_tenantSystemStorage.CreateCommitUnitOfWork())
        {
            var odinHashId = icr.OdinId.ToHashId();

            //Reconcile circle grants in the table
            _tenantSystemStorage.CircleMemberStorage.DeleteByCircleMember(odinHashId);
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
                        data = DotYouSystemSerializer.Serialize(new CircleMemberStorageData
                        {
                            OdinId = icr.OdinId,
                            CircleGrant = circleGrant
                        }).ToUtf8ByteArray()
                    };

                    _tenantSystemStorage.CircleMemberStorage.AddCircleMembers(new List<CircleMemberRecord>() { circleMemberRecord });
                }
            }

            // remove all app grants, 
            _tenantSystemStorage.AppGrants.Delete(odinHashId);

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
                        data = DotYouSystemSerializer.Serialize(appCircleGrant).ToUtf8ByteArray()
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
                data = DotYouSystemSerializer.Serialize(icrAccessRecord).ToUtf8ByteArray()
            };

            _tenantSystemStorage.Connections.Upsert(record);
        }
    }

    public void Delete(OdinId odinId)
    {
        using (_tenantSystemStorage.CreateCommitUnitOfWork())
        {
            _tenantSystemStorage.Connections.Delete(odinId);
            _tenantSystemStorage.AppGrants.Delete(odinId.ToHashId());
            _tenantSystemStorage.CircleMemberStorage.DeleteMembersFromAllCircles(new List<Guid>() { odinId.ToHashId() });
        }
    }

    public IEnumerable<IdentityConnectionRegistration> GetList(int count, UnixTimeUtcUnique? cursor, out UnixTimeUtcUnique? nextCursor,
        ConnectionStatus connectionStatus)
    {
        var records = _tenantSystemStorage.Connections.PagingByCreated(count, (int)connectionStatus, cursor, out nextCursor);
        return records.Select(MapFromStorage);
    }

    private IdentityConnectionRegistration MapFromStorage(ConnectionsRecord record)
    {
        var json = record.data.ToStringFromUtf8Bytes();
        var data = DotYouSystemSerializer.Deserialize<IcrAccessRecord>(json);

        var odinHashId = record.identity.ToHashId();

        var circleMemberRecords = _tenantSystemStorage.CircleMemberStorage.GetMemberCirclesAndData(odinHashId);
        foreach (var circleMemberRecord in circleMemberRecords)
        {
            var sd = DotYouSystemSerializer.Deserialize<CircleMemberStorageData>(circleMemberRecord.data.ToStringFromUtf8Bytes());
            data.AccessGrant.CircleGrants.Add(circleMemberRecord.circleId, sd.CircleGrant);
        }

        //TODO: this only returns one app grant. i need the whole list
        var allAppGrants = _tenantSystemStorage.AppGrants.GetByOdinHashId(odinHashId) ?? new List<AppGrantsRecord>();

        foreach (var appGrantRecord in allAppGrants)
        {
            // var appId = appGrantRecord.appId;
            // var circleId = appGrantRecord.circleId;
            var appCircleGrant = DotYouSystemSerializer.Deserialize<AppCircleGrant>(appGrantRecord.data.ToStringFromUtf8Bytes());
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
            ClientAccessTokenId = data.ClientAccessTokenId,
            ClientAccessTokenHalfKey = data.ClientAccessTokenHalfKey,
            ClientAccessTokenSharedSecret = data.ClientAccessTokenSharedSecret,
            OriginalContactData = data.OriginalContactData
        };
    }

    public List<OdinId> GetCircleMembers(GuidId circleId)
    {
        //Note: this list is a cache of members for a circle.  the source of truth is the
        //IdentityConnectionRegistration.AccessExchangeGrant.CircleGrants property for each OdinId
        var memberBytesList = _tenantSystemStorage.CircleMemberStorage.GetCircleMembers(circleId);
        var result = memberBytesList.Select(item =>
        {
            var data = DotYouSystemSerializer.Deserialize<CircleMemberStorageData>(item.data.ToStringFromUtf8Bytes());
            return data.OdinId;
        }).ToList();

        return result;
    }
}

public class IcrAccessRecord
{
    /// <summary>
    /// The drives and permissions granted to this connection
    /// </summary>
    public AccessExchangeGrant AccessGrant { get; set; }

    /// <summary>
    /// The Id of the <see cref="ClientAccessToken"/> to be sent when communicating with this OdinId's host
    /// </summary>
    public Guid ClientAccessTokenId { get; set; }

    /// <summary>
    /// The AccessTokenHalfKey of the <see cref="ClientAccessToken"/> to be sent when communicating with this OdinId's host
    /// </summary>
    public byte[] ClientAccessTokenHalfKey { get; set; }

    /// <summary>
    /// The SharedSecret of the <see cref="ClientAccessToken"/> used to encrypt payloads when
    /// communicating with this OdinId's host.  This is never sent over the wire.
    /// </summary>
    public byte[] ClientAccessTokenSharedSecret { get; set; } //TODO: this needs to be encrypted when stored; 

    public ContactRequestData OriginalContactData { get; set; }
}