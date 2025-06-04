using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;

namespace Odin.Services.Membership.Connections.Requests;

/// <summary>
/// Enables introducing identities to each other
/// </summary>
public class CircleNetworkIntroductionService : PeerServiceBase,
    INotificationHandler<ConnectionFinalizedNotification>,
    INotificationHandler<ConnectionBlockedNotification>,
    INotificationHandler<ConnectionDeletedNotification>,
    INotificationHandler<ConnectionRequestReceivedNotification>
{
    private readonly TenantContext _tenantContext;
    private readonly CircleNetworkRequestService _circleNetworkRequestService;
    private readonly ILogger<CircleNetworkIntroductionService> _logger;
    private readonly IMediator _mediator;
    private readonly PeerOutbox _peerOutbox;
    private readonly IdentityDatabase _db;
    private readonly PushNotificationService _pushNotificationService;
    private readonly IDriveManager _driveManager;

    /// <summary>
    /// Enables introducing identities to each other
    /// </summary>
    public CircleNetworkIntroductionService(OdinConfiguration odinConfiguration,
        CircleNetworkService circleNetworkService,
        CircleNetworkRequestService circleNetworkRequestService,
        ILogger<CircleNetworkIntroductionService> logger,
        IOdinHttpClientFactory odinHttpClientFactory,
        FileSystemResolver fileSystemResolver,
        IMediator mediator,
        PeerOutbox peerOutbox,
        PushNotificationService pushNotificationService,
        IDriveManager driveManager,
        TenantContext tenantContext,
        IdentityDatabase db)
        : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
    {
        _circleNetworkRequestService = circleNetworkRequestService;
        _logger = logger;
        _mediator = mediator;
        _peerOutbox = peerOutbox;
        _db = db;
        _pushNotificationService = pushNotificationService;
        _driveManager = driveManager;
        _tenantContext = tenantContext;
    }

    private const string ReceivedIntroductionContextKey = "f2f5c94c-c299-4122-8aa2-744d91f3b12f";

    private static readonly ThreeKeyValueStorage ReceivedIntroductionValueStorage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(ReceivedIntroductionContextKey));

    private static readonly byte[] ReceivedIntroductionDataType = Guid.Parse("0b844f10-9580-4cef-82e6-45b21eb40f62").ToByteArray();


    /// <summary>
    /// Introduces a group of identities to each other
    /// </summary>
    public async Task<IntroductionResult> SendIntroductions(IntroductionGroup group, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);

        OdinValidationUtils.AssertNotNull(group, nameof(group));
        OdinValidationUtils.AssertValidRecipientList(group.Recipients, allowEmpty: false);

        var driveId = (await _driveManager.GetDriveAsync(SystemDriveConstants.TransientTempDrive)).Id;

        async Task<bool> EnqueueOutboxItem(OdinId recipient, Introduction introduction)
        {
            try
            {
                OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
                OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

                var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);

                var item = new OutboxFileItem
                {
                    Recipient = recipient,
                    Priority = 50, //super high priority to ensure these are sent quickly,
                    Type = OutboxItemType.SendIntroduction,
                    AttemptCount = 0,
                    File = new InternalDriveFileId()
                    {
                        DriveId = driveId,
                        FileId = recipient.ToHashId() //SequentialGuid.CreateGuid()
                    },
                    DependencyFileId = default,
                    State = new OutboxItemState
                    {
                        TransferInstructionSet = null,
                        OriginalTransitOptions = null,
                        EncryptedClientAuthToken = clientAuthToken.ToPortableBytes(),
                        Data = OdinSystemSerializer.Serialize(introduction).ToUtf8ByteArray()
                    },
                };

                await _peerOutbox.AddItemAsync(item, useUpsert: true);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to enqueue introduction for recipient: [{recipient}]", recipient);
                return false;
            }

            return true;
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);

        OdinValidationUtils.AssertNotNull(group, nameof(group));
        OdinValidationUtils.AssertValidRecipientList(group.Recipients, allowEmpty: false);

        var recipients = group.Recipients.ToOdinIdList().Without(odinContext.Tenant);
        // var bytes = ByteArrayUtil.Combine(recipients.Select(i => i.ToByteArray()).ToArray());
        // group.Signature = Sign(bytes, odinContext);


        var result = new IntroductionResult();
        foreach (var recipient in recipients)
        {
            var introduction = new Introduction
            {
                Message = group.Message,
                Identities = recipients.ToDomainNames(),
                Timestamp = UnixTimeUtc.Now()
            };

            result.RecipientStatus[recipient] = await EnqueueOutboxItem(recipient, introduction);
        }

        return result;
    }

    /// <summary>
    /// Stores an incoming introduction
    /// </summary>
    public async Task ReceiveIntroductions(SharedSecretEncryptedPayload payload, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.AllowIntroductions);

        _logger.LogDebug("Receiving introductions from {sender}", odinContext.GetCallerOdinIdOrFail());

        OdinValidationUtils.AssertNotNull(payload, nameof(payload));

        var payloadBytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
        Introduction introduction = OdinSystemSerializer.Deserialize<Introduction>(payloadBytes.ToStringFromUtf8Bytes());

        OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
        OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

        introduction.Timestamp = UnixTimeUtc.Now();
        var introducer = odinContext.GetCallerOdinIdOrFail();

        var driveId = await _driveManager.GetDriveIdByAliasAsync(SystemDriveConstants.TransientTempDrive);

        //Store the introductions by the identity to which you're being introduces
        foreach (var identity in introduction.Identities.ToOdinIdList().Without(odinContext.Tenant))
        {
            // Note: we do not indicate if you're already connected or
            // have blocked the identity being introduced as we do not 
            // want to communicate any such information to the introducer
            var icr = await CircleNetworkService.GetIcrAsync(identity, odinContext, overrideHack: true);
            if (icr.IsConnected() || icr.Status == ConnectionStatus.Blocked)
            {
                continue;
            }

            var iid = new IdentityIntroduction()
            {
                IntroducerOdinId = introducer,
                Identity = identity,
                Message = introduction.Message,
                Received = UnixTimeUtc.Now()
            };

            await SaveAndEnqueueToConnect(iid, driveId.GetValueOrDefault());
        }

        var notification = new IntroductionsReceivedNotification()
        {
            IntroducerOdinId = introducer,
            Introduction = introduction,
            OdinContext = odinContext
        };

        await _pushNotificationService.EnqueueNotification(introducer, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.OwnerAppId,
                TypeId = notification.NotificationTypeId,
                TagId = introducer,
                Silent = false,
                // UnEncryptedJson = OdinSystemSerializer.Serialize(new
                // {
                //     IntroducerOdinId = introducer,
                //     Introduction = introduction,
                // })
            },
            OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.SendPushNotifications));

        await _mediator.Publish(notification);
    }

    public async Task ForceAutoAcceptEligibleConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var incomingConnectionRequests = await _circleNetworkRequestService.GetPendingRequestsAsync(PageOptions.All, odinContext);
        _logger.LogDebug("Running AutoAccept for incomingConnectionRequests ({count} requests)",
            incomingConnectionRequests.Results.Count);

        foreach (PendingConnectionRequestHeader request in incomingConnectionRequests.Results)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("AutoAcceptEligibleConnectionRequests - Cancellation requested; breaking from loop");
                break;
            }

            await AutoAcceptEligibleConnectionRequestAsync(request, force: true, odinContext);
        }
    }

    private async Task AutoAcceptEligibleConnectionRequestAsync(PendingConnectionRequestHeader request, bool force,
        IOdinContext odinContext)
    {
        if (force && !odinContext.Caller.HasMasterKey)
        {
            return;
        }

        if (_tenantContext.Settings.DisableAutoAcceptIntroductionsForTests && !force)
        {
            return;
        }

        var sender = request.SenderOdinId;
        var requiresIcr = request.EccEncryptedPayload.KeyType == PublicPrivateKeyType.OnlineIcrEncryptedKey;
        if (requiresIcr && odinContext.PermissionsContext.GetIcrKey(failIfNotFound: false) == null)
        {
            _logger.LogDebug("Auto Accept attempting to accept connection request from {sender} that is " +
                             "encrypted with OnlineIcrEncryptedKey, however odinContext does not have ICR key " +
                             "available.  Bypassing this request.",
                sender);
            return;
        }

        try
        {
            var newContext = OdinContextUpgrades.UsePermissions(odinContext,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ReadConnections);

            var introduction = await this.GetIntroductionInternalAsync(sender);
            if (null != introduction)
            {
                _logger.LogDebug("Auto-accept connection request from {sender} due to received introduction", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            var existingSentRequest = await _circleNetworkRequestService.GetSentRequestAsync(sender, newContext);
            if (null != existingSentRequest)
            {
                _logger.LogDebug("Auto-accept connection request from {sender} due to an existing outgoing request", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            if (await CircleNetworkService.IsConnectedAsync(sender, newContext))
            {
                _logger.LogDebug("Auto-accept connection request from {sender} since there is already an ICR", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            _logger.LogDebug("Auto-accept was not executed for request from {sender}; no matching reasons to accept", sender);
        }
        catch (OdinClientException oce)
        {
            if (oce.ErrorCode == OdinClientErrorCode.IncomingRequestNotFound)
            {
                _logger.LogError(oce, "Failed while trying to auto-accept a connection request from {identity}.  The request was not found",
                    sender);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while trying to auto-accept a connection request from {identity}", sender);
        }
    }

    /// <summary>
    /// Sends connection requests for introductions
    /// </summary>
    public async Task SendOutstandingConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        //upgrading for use in a bg process
        var newOdinContext = OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.ReadCircleMembership);

        //get the introductions from the list
        var introductions = await GetReceivedIntroductionsAsync(newOdinContext);

        _logger.LogDebug("Sending outstanding connection requests to {introductionCount} introductions", introductions.Count);

        foreach (var intro in introductions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("SendOutstandingConnectionRequests - Cancellation requested; breaking from loop");
                break;
            }

            var recipient = intro.Identity;

            var hasOutstandingRequest = await _circleNetworkRequestService.HasPendingOrSentRequest(recipient, odinContext);
            if (hasOutstandingRequest)
            {
                _logger.LogDebug("{recipient} has an incoming or outgoing request; not sending connection request", recipient);
                break;
            }

            var alreadyConnected = await CircleNetworkService.IsConnectedAsync(recipient, odinContext);
            if (alreadyConnected)
            {
                _logger.LogDebug("{recipient} is already connected; not sending connection request", recipient);
                break;
            }

            await this.SendIntroductoryConnectionRequestInternalAsync(intro, cancellationToken, newOdinContext);
        }
    }

    public async Task<List<IdentityIntroduction>> GetReceivedIntroductionsAsync(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
        var results = await ReceivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(_db.KeyThreeValue,
            ReceivedIntroductionDataType);
        return results.ToList();
    }

    public async Task Handle(ConnectionFinalizedNotification notification, CancellationToken cancellationToken)
    {
        await DeleteIntroductionsToAsync(notification.OdinId);
    }

    public async Task Handle(ConnectionBlockedNotification notification, CancellationToken cancellationToken)
    {
        await using var tx = await _db.BeginStackedTransactionAsync();
        await DeleteIntroductionsToAsync(notification.OdinId);
        await DeleteIntroductionsFromAsync(notification.OdinId);
        tx.Commit();
    }

    public async Task Handle(ConnectionDeletedNotification notification, CancellationToken cancellationToken)
    {
        await using var tx = await _db.BeginStackedTransactionAsync();
        await DeleteIntroductionsToAsync(notification.OdinId);
        await DeleteIntroductionsFromAsync(notification.OdinId);
        tx.Commit();
    }

    public async Task Handle(ConnectionRequestReceivedNotification notification, CancellationToken cancellationToken)
    {
        await AutoAcceptEligibleConnectionRequestAsync(notification.Request, false, notification.OdinContext);
    }

    /// <summary>
    /// Sends connection requests for pending introductions if one has not already been sent or received
    /// </summary>
    private async Task SendIntroductoryConnectionRequestInternalAsync(IdentityIntroduction intro, CancellationToken cancellationToken,
        IOdinContext odinContext)
    {
        var recipient = intro.Identity;
        var introducer = intro.IntroducerOdinId;

        var id = Guid.NewGuid();
        var requestHeader = new ConnectionRequestHeader()
        {
            Id = id,
            Recipient = recipient,
            Message = intro.Message,
            IntroducerOdinId = introducer,
            ContactData = new ContactRequestData(),
            CircleIds = [],
            ConnectionRequestOrigin = ConnectionRequestOrigin.Introduction
        };

        await _circleNetworkRequestService.SendConnectionRequestAsync(requestHeader, cancellationToken, odinContext);
    }

    public async Task SendAutoConnectIntroduceeRequest(IdentityIntroduction iid,
        CancellationToken cancellationToken, IOdinContext odinContext)
    {
        await this.SendIntroductoryConnectionRequestInternalAsync(iid, cancellationToken, odinContext);
    }

    private async Task<IdentityIntroduction> GetIntroductionInternalAsync(OdinId identity)
    {
        var result = await ReceivedIntroductionValueStorage.GetAsync<IdentityIntroduction>(_db.KeyThreeValue, identity);
        return result;
    }

    private async Task AutoAcceptAsync(OdinId sender, IOdinContext odinContext)
    {
        var header = new AcceptRequestHeader()
        {
            Sender = sender,
            CircleIds = [],
            ContactData = new ContactRequestData(),
        };

        _logger.LogDebug("Attempting to auto-accept connection request from {sender}", sender);
        var newContext = OdinContextUpgrades.UsePermissions(odinContext,
            PermissionKeys.ReadCircleMembership,
            PermissionKeys.ManageFeed);

        await _circleNetworkRequestService.AcceptConnectionRequestAsync(header, tryOverrideAcl: true, newContext);
    }

    private async Task SaveAndEnqueueToConnect(IdentityIntroduction iid, Guid driveId)
    {
        var recipient = iid.Identity;

        try
        {
            await ReceivedIntroductionValueStorage.UpsertAsync(_db.KeyThreeValue,
                recipient,
                dataTypeKey: iid.IntroducerOdinId.ToHashId().ToByteArray(),
                ReceivedIntroductionDataType, iid);

            if (!_tenantContext.Settings.DisableAutoAcceptIntroductionsForTests)
            {
                var item = new OutboxFileItem
                {
                    Recipient = recipient,
                    Priority = 55, //super high priority to ensure these are sent quickly,
                    Type = OutboxItemType.ConnectIntroducee,
                    AttemptCount = 0,
                    File = new InternalDriveFileId()
                    {
                        DriveId = driveId,
                        FileId = recipient.ToHashId()
                    },
                    DependencyFileId = default,
                    State = new OutboxItemState
                    {
                        TransferInstructionSet = null,
                        OriginalTransitOptions = null,
                        EncryptedClientAuthToken = default,
                        Data = OdinSystemSerializer.Serialize(iid).ToUtf8ByteArray()
                    },
                };

                await _peerOutbox.AddItemAsync(item, useUpsert: true);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to enqueue ConnectIntroducee for recipient: [{recipient}]", recipient);
        }
    }

    private async Task DeleteIntroductionsToAsync(OdinId identity)
    {
        _logger.LogDebug("Deleting introduction sent to {identity}", identity);
        await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValue, identity);
    }

    private async Task DeleteIntroductionsFromAsync(OdinId introducer)
    {
        _logger.LogDebug("Deleting introduction sent from {identity}", introducer);

        var introductionsFromIdentity =
            await ReceivedIntroductionValueStorage.GetByDataTypeAsync<IdentityIntroduction>(_db.KeyThreeValue,
                introducer.ToHashId().ToByteArray());

        foreach (var introduction in introductionsFromIdentity)
        {
            await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValue, introduction.Identity);
        }
    }

    public async Task DeleteIntroductionsAsync(IOdinContext odinContext, UnixTimeUtc? maxDate = null)
    {
        if (maxDate == null)
        {
            _logger.LogDebug("Deleting all introductions");
        }
        else
        {
            _logger.LogDebug("Deleting all introductions before {maxDate}", maxDate.GetValueOrDefault().ToDateTime().ToShortDateString());
        }

        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);
        var results = await ReceivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(_db.KeyThreeValue,
            ReceivedIntroductionDataType);
        foreach (var intro in results)
        {
            if (maxDate != null && intro.Received < maxDate)
            {
                await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValue, intro.Identity);
            }
            else
            {
                await ReceivedIntroductionValueStorage.DeleteAsync(_db.KeyThreeValue, intro.Identity);
            }
        }
    }
}