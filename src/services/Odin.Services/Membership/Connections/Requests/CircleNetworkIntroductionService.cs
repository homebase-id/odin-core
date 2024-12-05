using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Refit;

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
    DriveManager driveManager)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver,
            odinConfiguration),
        INotificationHandler<ConnectionFinalizedNotification>,
        INotificationHandler<ConnectionBlockedNotification>,
        INotificationHandler<ConnectionDeletedNotification>
{
    private const string ReceivedIntroductionContextKey = "f2f5c94c-c299-4122-8aa2-744d91f3b12f";

    private static readonly ThreeKeyValueStorage ReceivedIntroductionValueStorage =
        TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(ReceivedIntroductionContextKey));

    private static readonly byte[] ReceivedIntroductionDataType = Guid.Parse("0b844f10-9580-4cef-82e6-45b21eb40f62").ToByteArray();

    private readonly OdinConfiguration _odinConfiguration = odinConfiguration;

    private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

    // _logger = logger;


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
                    Priority = 0, //super high priority to ensure these are sent quickly,
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
                Message = introduction.Message
            };

            await UpsertIntroductionAsync(iid);
        }

        var notification = new IntroductionsReceivedNotification()
        {
            IntroducerOdinId = introducer,
            Introduction = introduction,
            OdinContext = odinContext
        };

        var newContext = OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.SendPushNotifications);
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
            newContext);

        await mediator.Publish(notification);
    }

    public async Task AutoAcceptEligibleConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var incomingConnectionRequests = await circleNetworkRequestService.GetPendingRequestsAsync(PageOptions.All, odinContext);
        logger.LogInformation("Running AutoAccept for incomingConnectionRequests ({count} requests)",
            incomingConnectionRequests.Results.Count);

        foreach (var request in incomingConnectionRequests.Results)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("AutoAcceptEligibleConnectionRequests - Cancellation requested; breaking from loop");

                break;
            }

            var sender = request.SenderOdinId;

            try
            {
                var introduction = await this.GetIntroductionInternalAsync(sender);
                if (null != introduction)
                {
                    logger.LogDebug("Auto-accept connection request from {sender} due to received introduction", sender);
                    await AutoAcceptAsync(sender, odinContext);
                    return;
                }

                var existingSentRequest = await circleNetworkRequestService.GetSentRequest(sender, odinContext);
                if (null != existingSentRequest)
                {
                    logger.LogDebug("Auto-accept connection request from {sender} due to an existing outgoing request", sender);
                    await AutoAcceptAsync(sender, odinContext);
                    return;
                }

                if (await CircleNetworkService.IsConnectedAsync(sender, odinContext))
                {
                    logger.LogDebug("Auto-accept connection request from {sender} since there is already an ICR", sender);
                    await AutoAcceptAsync(sender, odinContext);
                    return;
                }

                var incomingRequest = await circleNetworkRequestService.GetPendingRequestAsync(sender, odinContext);
                if (incomingRequest?.IntroducerOdinId != null)
                {
                    var introducerIcr = await CircleNetworkService.GetIcrAsync(incomingRequest.IntroducerOdinId.Value, odinContext);

                    if (introducerIcr.IsConnected() &&
                        introducerIcr.AccessGrant.CircleGrants.Values.Any(v =>
                            v.PermissionSet?.HasKey(PermissionKeys.AllowIntroductions) ?? false))
                    {
                        logger.LogDebug(
                            "Auto-accept connection request from {sender} since sender was introduced by " +
                            "[{introducer}]; who is connected and has {permission}",
                            sender,
                            introducerIcr.OdinId,
                            nameof(PermissionKeys.AllowIntroductions));
                        await AutoAcceptAsync(sender, odinContext);
                        return;
                    }
                }

                logger.LogDebug("Auto-accept was not executed for request from {sender}; no matching reasons to accept", sender);
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "Failed while trying to auto-accept a connection request from {identity}", sender);
            }
        }
    }

    /// <summary>
    /// Sends connection requests for introductions
    /// </summary>
    public async Task SendOutstandingConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        const int maxSendAttempts = 30;

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

            var hasOutstandingRequest = await circleNetworkRequestService.HasPendingOrSentRequest(recipient, newOdinContext);
            if (hasOutstandingRequest)
            {
                logger.LogDebug("{recipient} has an incoming or outgoing request; not sending connection request", recipient);
                continue;
            }

            var alreadyConnected = await CircleNetworkService.IsConnectedAsync(recipient, newOdinContext);
            if (alreadyConnected)
            {
                logger.LogDebug("{recipient} is already connected; not sending connection request", recipient);
                continue;
            }

            try
            {
                if (intro.SendAttemptCount <= maxSendAttempts)
                {
                    await this.TrySendConnectionRequestAsync(intro, cancellationToken, newOdinContext);
                }
                else
                {
                    logger.LogDebug("Not sending introduction to {intro} (introduced by {introducer}); it has reached " +
                                    "maxSendAttempts of {max}", intro.Identity, intro.IntroducerOdinId, maxSendAttempts);
                }
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex,
                    "Failed sending Introduced-connection-request to {identity}. This was attempt #:{attemptNumber} of {maxSendAttempts}.  Continuing to next introduction.",
                    intro.Identity, intro.SendAttemptCount, maxSendAttempts);
            }
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


    private SignatureData Sign(byte[] data, IOdinContext odinContext)
    {
        var password = Guid.NewGuid().ToByteArray().ToSensitiveByteArray();

        OdinId signer = odinContext.GetCallerOdinIdOrFail();

        var eccKey = new EccFullKeyData(password, EccKeySize.P384, 1);
        var signature = SignatureData.NewSignature(data, signer, password, eccKey);
        return signature;
    }

    private bool VerifySignature(SignatureData signature, byte[] data)
    {
        bool isValid = SignatureData.Verify(signature, data);
        return isValid;
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
            logger.LogInformation(ex, "Failed to auto-except connection request: original-sender: {originalSender}", header.Sender);
        }
    }

    /// <summary>
    /// Sends connection requests for pending introductions if one has not already been sent or received
    /// </summary>
    private async Task TrySendConnectionRequestAsync(IdentityIntroduction intro, CancellationToken cancellationToken,
        IOdinContext odinContext)
    {
        var recipient = intro.Identity;
        var introducer = intro.IntroducerOdinId;

        const int minDaysSinceLastSend = 3; //TODO: config
        if (intro.LastProcessed != UnixTimeUtc.ZeroTime && intro.LastProcessed.AddDays(minDaysSinceLastSend) < UnixTimeUtc.Now())
        {
            logger.LogDebug(
                "Ignoring introduction to {recipient} from {introducer} since we last processed this less than {days} days ago",
                recipient,
                introducer,
                minDaysSinceLastSend);

            return;
        }

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

        intro.SendAttemptCount++;
        intro.LastProcessed = UnixTimeUtc.Now();
        await UpsertIntroductionAsync(intro);

        await circleNetworkRequestService.SendConnectionRequestAsync(requestHeader, cancellationToken, odinContext);
    }

    private async Task UpsertIntroductionAsync(IdentityIntroduction intro)
    {
        await ReceivedIntroductionValueStorage.UpsertAsync(tblKeyThreeValue, intro.Identity,
            dataTypeKey: intro.IntroducerOdinId.ToHashId().ToByteArray(),
            ReceivedIntroductionDataType, intro);
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