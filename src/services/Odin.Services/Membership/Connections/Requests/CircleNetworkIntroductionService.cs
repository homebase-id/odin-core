using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
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
public class CircleNetworkIntroductionService(
    OdinConfiguration odinConfiguration,
    CircleNetworkService circleNetworkService,
    CircleNetworkRequestService circleNetworkRequestService,
    ILogger<CircleNetworkIntroductionService> logger,
    IOdinHttpClientFactory odinHttpClientFactory,
    FileSystemResolver fileSystemResolver,
    IMediator mediator,
    TableKeyThreeValue tblKeyThreeValue,
    PeerOutbox peerOutbox,
    PushNotificationService pushNotificationService,
    PublicPrivateKeyService publicPrivateKeyService,
    DriveManager driveManager,
    TenantContext tenantContext)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver,
            odinConfiguration),
        INotificationHandler<ConnectionFinalizedNotification>,
        INotificationHandler<ConnectionBlockedNotification>,
        INotificationHandler<ConnectionDeletedNotification>,
        INotificationHandler<ConnectionRequestReceivedNotification>
{
    private readonly TenantContext _tenantContext = tenantContext;
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


        var driveId = (await driveManager.GetDriveAsync(SystemDriveConstants.TransientTempDrive)).Id;

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

                await peerOutbox.AddItemAsync(item, useUpsert: true);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to enqueue introduction for recipient: [{recipient}]", recipient);
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

        logger.LogDebug("Receiving introductions from {sender}", odinContext.GetCallerOdinIdOrFail());

        OdinValidationUtils.AssertNotNull(payload, nameof(payload));

        var payloadBytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
        Introduction introduction = OdinSystemSerializer.Deserialize<Introduction>(payloadBytes.ToStringFromUtf8Bytes());

        OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
        OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

        introduction.Timestamp = UnixTimeUtc.Now();
        var introducer = odinContext.GetCallerOdinIdOrFail();

        var driveId = await driveManager.GetDriveIdByAliasAsync(SystemDriveConstants.TransientTempDrive);

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

        await pushNotificationService.EnqueueNotification(introducer, new AppNotificationOptions()
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

        await mediator.Publish(notification);
    }

    public async Task AutoAcceptEligibleConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var incomingConnectionRequests = await circleNetworkRequestService.GetPendingRequestsAsync(PageOptions.All, odinContext);
        logger.LogDebug("Running AutoAccept for incomingConnectionRequests ({count} requests)",
            incomingConnectionRequests.Results.Count);

        foreach (PendingConnectionRequestHeader request in incomingConnectionRequests.Results)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("AutoAcceptEligibleConnectionRequests - Cancellation requested; breaking from loop");
                break;
            }

            await AutoAcceptEligibleConnectionRequestAsync(request.SenderOdinId, odinContext);
        }
    }

    private async Task AutoAcceptEligibleConnectionRequestAsync(OdinId sender, IOdinContext odinContext)
    {
        try
        {
            var newContext = OdinContextUpgrades.UsePermissions(odinContext,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ReadConnections);

            var introduction = await this.GetIntroductionInternalAsync(sender);
            if (null != introduction)
            {
                logger.LogDebug("Auto-accept connection request from {sender} due to received introduction", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            var existingSentRequest = await circleNetworkRequestService.GetSentRequest(sender, newContext);
            if (null != existingSentRequest)
            {
                logger.LogDebug("Auto-accept connection request from {sender} due to an existing outgoing request", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            if (await CircleNetworkService.IsConnectedAsync(sender, newContext))
            {
                logger.LogDebug("Auto-accept connection request from {sender} since there is already an ICR", sender);
                await AutoAcceptAsync(sender, newContext);
                return;
            }

            // var incomingRequest = await circleNetworkRequestService.GetPendingRequestAsync(sender, newContext);
            // if (incomingRequest?.IntroducerOdinId != null)
            // {
            //     var introducerIcr = await CircleNetworkService.GetIcrAsync(incomingRequest.IntroducerOdinId.Value, newContext);
            //
            //     if (introducerIcr.IsConnected() &&
            //         introducerIcr.AccessGrant.CircleGrants.Values.Any(v =>
            //             v.PermissionSet?.HasKey(PermissionKeys.AllowIntroductions) ?? false))
            //     {
            //         logger.LogDebug(
            //             "Auto-accept connection request from {sender} since sender was introduced by " +
            //             "[{introducer}]; who is connected and has {permission}",
            //             sender,
            //             introducerIcr.OdinId,
            //             nameof(PermissionKeys.AllowIntroductions));
            //         await AutoAcceptAsync(sender, newContext);
            //         return;
            //     }
            // }

            logger.LogDebug("Auto-accept was not executed for request from {sender}; no matching reasons to accept", sender);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed while trying to auto-accept a connection request from {identity}", sender);
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

        logger.LogDebug("Sending outstanding connection requests to {introductionCount} introductions", introductions.Count);

        foreach (var intro in introductions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("SendOutstandingConnectionRequests - Cancellation requested; breaking from loop");
                break;
            }

            var recipient = intro.Identity;

            var hasOutstandingRequest = await circleNetworkRequestService.HasPendingOrSentRequest(recipient, odinContext);
            if (hasOutstandingRequest)
            {
                logger.LogDebug("{recipient} has an incoming or outgoing request; not sending connection request", recipient);
                break;
            }

            var alreadyConnected = await CircleNetworkService.IsConnectedAsync(recipient, odinContext);
            if (alreadyConnected)
            {
                logger.LogDebug("{recipient} is already connected; not sending connection request", recipient);
                break;
            }

            await this.SendIntroductoryConnectionRequestAsync(intro, cancellationToken, newOdinContext);
        }
    }

    public async Task<List<IdentityIntroduction>> GetReceivedIntroductionsAsync(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
        var results = await ReceivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(tblKeyThreeValue,
            ReceivedIntroductionDataType);
        return results.ToList();
    }

    public async Task Handle(ConnectionFinalizedNotification notification, CancellationToken cancellationToken)
    {
        await DeleteIntroductionsToAsync(notification.OdinId);
    }

    public async Task Handle(ConnectionBlockedNotification notification, CancellationToken cancellationToken)
    {
        //TODO CONNECTIONS
        // await db.CreateCommitUnitOfWorkAsync(async () =>
        {
            await DeleteIntroductionsToAsync(notification.OdinId);
            await DeleteIntroductionsFromAsync(notification.OdinId);
        }
        //);
    }

    public async Task Handle(ConnectionDeletedNotification notification, CancellationToken cancellationToken)
    {
        //TODO CONNECTIONS
        // await db.CreateCommitUnitOfWorkAsync(async () =>
        {
            await DeleteIntroductionsToAsync(notification.OdinId);
            await DeleteIntroductionsFromAsync(notification.OdinId);
        }
        //);
    }

    public async Task Handle(ConnectionRequestReceivedNotification notification, CancellationToken cancellationToken)
    {
        await AutoAcceptEligibleConnectionRequestAsync(notification.Sender, notification.OdinContext);
    }

    /// <summary>
    /// Sends connection requests for pending introductions if one has not already been sent or received
    /// </summary>
    private async Task SendIntroductoryConnectionRequestAsync(IdentityIntroduction intro, CancellationToken cancellationToken,
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

        await circleNetworkRequestService.SendConnectionRequestAsync(requestHeader, cancellationToken, odinContext);
    }

    public async Task<PeerTryRetryResult<AutoConnectResult>> SendAutoConnectIntroduceeRequest(IdentityIntroduction iid,
        CancellationToken cancellationToken, IOdinContext odinContext)
    {
        await this.SendIntroductoryConnectionRequestAsync(iid, cancellationToken, odinContext);
        return null;
    }

    /// <summary>
    /// Auto-connects an introducee
    /// </summary>
    public async Task<AutoConnectResult> AcceptIntroduceeAutoConnectRequest(EccEncryptedPayload payload,
        CancellationToken cancellationToken,
        IOdinContext odinContext)
    {
        var payloadBytes = await publicPrivateKeyService.EccDecryptPayload(PublicPrivateKeyType.OfflineKey, payload, odinContext);
        var request = OdinSystemSerializer.Deserialize<IntroductionAutoConnectRequest>(payloadBytes.ToStringFromUtf8Bytes());

        if (_tenantContext.Settings.DisableAutoAcceptIntroductions)
        {
            //
            // Fall back to connection request
            //
            logger.LogDebug("Received introducee auto connect request but auto-accept is disabled, creating connection request instead");
            return new AutoConnectResult()
            {
                ConnectionSucceeded = false
            };
        }

        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertIsValidOdinId(request.Identity, out _);

        try
        {
            //
            // and on the sender side, also needs to create a connection
            // 
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to accept introducee request");
            return new AutoConnectResult()
            {
                ConnectionSucceeded = false
            };
        }

        return new AutoConnectResult()
        {
            ConnectionSucceeded = true
        };
    }

    private async Task<IdentityIntroduction> GetIntroductionInternalAsync(OdinId identity)
    {
        var result = await ReceivedIntroductionValueStorage.GetAsync<IdentityIntroduction>(tblKeyThreeValue, identity);
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

        try
        {
            logger.LogDebug("Attempting to auto-accept connection request from {sender}", sender);
            var newContext = OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.ReadCircleMembership);
            await circleNetworkRequestService.AcceptConnectionRequestAsync(header, tryOverrideAcl: true, newContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to auto-except connection request: original-sender: {originalSender}", header.Sender);
        }
    }

    private async Task SaveAndEnqueueToConnect(IdentityIntroduction iid, Guid driveId)
    {
        var recipient = iid.Identity;

        try
        {
            await ReceivedIntroductionValueStorage.UpsertAsync(tblKeyThreeValue,
                recipient,
                dataTypeKey: iid.IntroducerOdinId.ToHashId().ToByteArray(),
                ReceivedIntroductionDataType, iid);

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

            await peerOutbox.AddItemAsync(item, useUpsert: true);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to enqueue ConnectIntroducee for recipient: [{recipient}]", recipient);
        }
    }

    private async Task DeleteIntroductionsToAsync(OdinId identity)
    {
        logger.LogDebug("Deleting introduction sent to {identity}", identity);
        await ReceivedIntroductionValueStorage.DeleteAsync(tblKeyThreeValue, identity);
    }

    private async Task DeleteIntroductionsFromAsync(OdinId introducer)
    {
        logger.LogDebug("Deleting introduction sent from {identity}", introducer);


        var introductionsFromIdentity = await
            ReceivedIntroductionValueStorage.GetByDataTypeAsync<IdentityIntroduction>(tblKeyThreeValue,
                introducer.ToHashId().ToByteArray());

        foreach (var introduction in introductionsFromIdentity)
        {
            await ReceivedIntroductionValueStorage.DeleteAsync(tblKeyThreeValue, introduction.Identity);
        }
    }

    public async Task DeleteIntroductionsAsync(IOdinContext odinContext)
    {
        logger.LogDebug("Deleting all introductions");


        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);
        var results = await ReceivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(tblKeyThreeValue,
            ReceivedIntroductionDataType);
        foreach (var intro in results)
        {
            await ReceivedIntroductionValueStorage.DeleteAsync(tblKeyThreeValue, intro.Identity);
        }
    }
}