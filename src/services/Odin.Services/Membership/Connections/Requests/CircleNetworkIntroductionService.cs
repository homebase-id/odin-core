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
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;
using Refit;

namespace Odin.Services.Membership.Connections.Requests;

/// <summary>
/// Enables introducing identities to each other
/// </summary>
public class CircleNetworkIntroductionService : PeerServiceBase,
    INotificationHandler<ConnectionFinalizedNotification>,
    INotificationHandler<ConnectionBlockedNotification>,
    INotificationHandler<ConnectionDeletedNotification>
{
    private readonly byte[] _receivedIntroductionDataType = Guid.Parse("0b844f10-9580-4cef-82e6-45b21eb40f62").ToByteArray();

    private readonly OdinConfiguration _odinConfiguration;

    private readonly CircleNetworkRequestService _circleNetworkRequestService;

    private readonly ILogger<CircleNetworkIntroductionService> _logger;
    private readonly IOdinHttpClientFactory _odinHttpClientFactory;
    private readonly TenantSystemStorage _tenantSystemStorage;
    private readonly IMediator _mediator;
    private readonly PushNotificationService _pushNotificationService;

    private readonly ThreeKeyValueStorage _receivedIntroductionValueStorage;

    public CircleNetworkIntroductionService(
        OdinConfiguration odinConfiguration,
        CircleNetworkService circleNetworkService,
        CircleNetworkRequestService circleNetworkRequestService,
        ILogger<CircleNetworkIntroductionService> logger,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantSystemStorage tenantSystemStorage,
        FileSystemResolver fileSystemResolver,
        IMediator mediator,
        PushNotificationService pushNotificationService) : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
    {
        _odinConfiguration = odinConfiguration;
        _circleNetworkRequestService = circleNetworkRequestService;
        _logger = logger;
        // _logger = logger;
        _odinHttpClientFactory = odinHttpClientFactory;
        _tenantSystemStorage = tenantSystemStorage;
        _mediator = mediator;
        _pushNotificationService = pushNotificationService;

        const string receivedIntroductionContextKey = "f2f5c94c-c299-4122-8aa2-744d91f3b12f";
        _receivedIntroductionValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(receivedIntroductionContextKey));
    }


    /// <summary>
    /// Introduces a group of identities to each other
    /// </summary>
    public async Task<IntroductionResult> SendIntroductions(IntroductionGroup group, IOdinContext odinContext)
    {
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

            var success = await MakeIntroductionAsync(recipient, introduction, odinContext);
            result.RecipientStatus[recipient] = success;
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

        //Store the introductions by the identity to which you're being introduces
        foreach (var identity in introduction.Identities.ToOdinIdList().Without(odinContext.Tenant))
        {
            // Note: we do not check if you're already connected or
            // have blocked the identity being introduced as we do not 
            // want to communicate any such information to the introducer

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
        var db = _tenantSystemStorage.IdentityDatabase;
        var newContext = OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.SendPushNotifications);
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
            newContext, db);

        await _mediator.Publish(notification);
    }

    public async Task AutoAcceptEligibleConnectionRequestsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var incomingConnectionRequests = await _circleNetworkRequestService.GetPendingRequestsAsync(PageOptions.All, odinContext);
        _logger.LogInformation("Running AutoAccept for incomingConnectionRequests ({count} requests)",
            incomingConnectionRequests.Results.Count);

        foreach (var request in incomingConnectionRequests.Results)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("AutoAcceptEligibleConnectionRequests - Cancellation requested; breaking from loop");

                break;
            }

            var sender = request.SenderOdinId;

            try
            {
                var introduction = await this.GetIntroductionInternalAsync(sender);
                if (null != introduction)
                {
                    _logger.LogDebug("Auto-accept connection request from {sender} due to received introduction", sender);
                    await AutoAcceptAsync(sender, odinContext);
                    return;
                }

                var existingSentRequest = await _circleNetworkRequestService.GetSentRequest(sender, odinContext);
                if (null != existingSentRequest)
                {
                    _logger.LogDebug("Auto-accept connection request from {sender} due to an existing outgoing request", sender);
                    await AutoAcceptAsync(sender, odinContext);
                    return;
                }

                if (await CircleNetworkService.IsConnectedAsync(sender, odinContext))
                {
                    _logger.LogDebug("Auto-accept connection request from {sender} since there is already an ICR", sender);
                    await AutoAcceptAsync(sender, odinContext);
                    return;
                }

                var incomingRequest = await _circleNetworkRequestService.GetPendingRequestAsync(sender, odinContext);
                if (incomingRequest?.IntroducerOdinId != null)
                {
                    var introducerIcr = await CircleNetworkService.GetIcrAsync(incomingRequest.IntroducerOdinId.Value, odinContext);

                    if (introducerIcr.IsConnected() &&
                        introducerIcr.AccessGrant.CircleGrants.Values.Any(v =>
                            v.PermissionSet?.HasKey(PermissionKeys.AllowIntroductions) ?? false))
                    {
                        _logger.LogDebug(
                            "Auto-accept connection request from {sender} since sender was introduced by " +
                            "[{introducer}]; who is connected and has {permission}",
                            sender,
                            introducerIcr.OdinId,
                            nameof(PermissionKeys.AllowIntroductions));
                        await AutoAcceptAsync(sender, odinContext);
                        return;
                    }
                }

                _logger.LogDebug("Auto-accept was not executed for request from {sender}; no matching reasons to accept", sender);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed while trying to auto-accept a connection request from {identity}", sender);
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

        _logger.LogDebug("Sending outstanding connection requests to {introductionCount} introductions", introductions.Count);

        foreach (var intro in introductions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("SendOutstandingConnectionRequests - Cancellation requested; breaking from loop");
                break;
            }

            var recipient = intro.Identity;

            var hasOutstandingRequest = await _circleNetworkRequestService.HasPendingOrSentRequest(recipient, newOdinContext);
            if (hasOutstandingRequest)
            {
                _logger.LogDebug("{recipient} has an incoming or outgoing request; not sending connection request", recipient);
                continue;
            }

            var alreadyConnected = await CircleNetworkService.IsConnectedAsync(recipient, newOdinContext);
            if (alreadyConnected)
            {
                _logger.LogDebug("{recipient} is already connected; not sending connection request", recipient);
                continue;
            }

            try
            {
                if (intro.SendAttemptCount <= maxSendAttempts)
                {
                    await this.TrySendConnectionRequestAsync(intro, newOdinContext);
                }
                else
                {
                    _logger.LogDebug("Not sending introduction to {intro} (introduced by {introducer}); it has reached " +
                                     "maxSendAttempts of {max}", intro.Identity, intro.IntroducerOdinId, maxSendAttempts);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed sending Introduced-connection-request to {identity}. This was attempt #:{attemptNumber} of {maxSendAttempts}.  Continuing to next introduction.",
                    intro.Identity, intro.SendAttemptCount, maxSendAttempts);
            }
        }
    }

    public async Task<List<IdentityIntroduction>> GetReceivedIntroductionsAsync(IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.ReadConnectionRequests);
        var results = await _receivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(db, _receivedIntroductionDataType);
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

    /// <summary>
    /// Introduces <see cref="Introduction.Identities"/> to the recipient identity
    /// </summary>
    private async Task<bool> MakeIntroductionAsync(OdinId recipient, Introduction introduction, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
        OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

        bool success = false;
        try
        {
            var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);

            ApiResponse<HttpContent> response;
            await TryRetry.WithDelayAsync(
                _odinConfiguration.Host.PeerOperationMaxAttempts,
                _odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () =>
                {
                    var json = OdinSystemSerializer.Serialize(introduction);
                    var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), clientAuthToken.SharedSecret);
                    var client = _odinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkRequestHttpClient>(recipient,
                        clientAuthToken.ToAuthenticationToken());

                    response = await client.MakeIntroduction(encryptedPayload);
                    success = response.IsSuccessStatusCode;
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }

        return success;
    }

    private async Task<IdentityIntroduction> GetIntroductionInternalAsync(OdinId identity)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        var result = await _receivedIntroductionValueStorage.GetAsync<IdentityIntroduction>(db, identity);
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
            _logger.LogInformation("Attempting to auto-accept connection request from {sender}", sender);
            var newContext = OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.ReadCircleMembership);
            await _circleNetworkRequestService.AcceptConnectionRequestAsync(header, tryOverrideAcl: true, newContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-except connection request: original-sender: {originalSender}", header.Sender);
        }
    }

    /// <summary>
    /// Sends connection requests for pending introductions if one has not already been sent or received
    /// </summary>
    private async Task TrySendConnectionRequestAsync(IdentityIntroduction intro, IOdinContext odinContext)
    {
        var recipient = intro.Identity;
        var introducer = intro.IntroducerOdinId;

        const int minDaysSinceLastSend = 3; //TODO: config
        if (intro.LastProcessed != UnixTimeUtc.ZeroTime && intro.LastProcessed.AddDays(minDaysSinceLastSend) < UnixTimeUtc.Now())
        {
            _logger.LogDebug(
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

        await _circleNetworkRequestService.SendConnectionRequestAsync(requestHeader, odinContext);
    }

    private async Task UpsertIntroductionAsync(IdentityIntroduction intro)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        await _receivedIntroductionValueStorage.UpsertAsync(db, intro.Identity, dataTypeKey: intro.IntroducerOdinId.ToHashId().ToByteArray(),
            _receivedIntroductionDataType, intro);
    }

    private async Task DeleteIntroductionsToAsync(OdinId identity)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        _logger.LogDebug("Deleting introduction sent to {identity}", identity);
        await _receivedIntroductionValueStorage.DeleteAsync(db, identity);
    }

    private async Task DeleteIntroductionsFromAsync(OdinId introducer)
    {
        _logger.LogDebug("Deleting introduction sent from {identity}", introducer);

        var db = _tenantSystemStorage.IdentityDatabase;
        var introductionsFromIdentity = await 
            _receivedIntroductionValueStorage.GetByDataTypeAsync<IdentityIntroduction>(db, introducer.ToHashId().ToByteArray());

        foreach (var introduction in introductionsFromIdentity)
        {
            await _receivedIntroductionValueStorage.DeleteAsync(db, introduction.Identity);
        }
    }

    public async Task DeleteIntroductionsAsync(IOdinContext odinContext)
    {
        _logger.LogDebug("Deleting all introductions");

        var db = _tenantSystemStorage.IdentityDatabase;
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);
        var results = await _receivedIntroductionValueStorage.GetByCategoryAsync<IdentityIntroduction>(db, _receivedIntroductionDataType);
        foreach (var intro in results)
        {
            await _receivedIntroductionValueStorage.DeleteAsync(db, intro.Identity);
        }
    }
}