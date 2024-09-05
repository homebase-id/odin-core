using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;
using Refit;


namespace Odin.Services.Membership.Connections.Requests;

/// <summary>
/// Enables introducing identities to each other
/// </summary>
public class CircleNetworkIntroductionService : PeerServiceBase
{
    private readonly byte[] _receivedIntroductionDataType = Guid.Parse("9b844f10-9580-4cef-82e6-45b21eb40f62").ToByteArray();

    private readonly OdinConfiguration _odinConfiguration;
    private readonly CircleNetworkRequestService _circleNetworkRequestService;
    private readonly ILogger<CircleNetworkIntroductionService> _logger;
    private readonly IOdinHttpClientFactory _odinHttpClientFactory;
    private readonly TransitInboxBoxStorage _inboxBoxStorage;
    private readonly IMediator _mediator;
    private readonly DriveManager _driveManager;

    private readonly ThreeKeyValueStorage _receivedIntroductionValueStorage;

    public CircleNetworkIntroductionService(
        OdinConfiguration odinConfiguration,
        CircleNetworkService circleNetworkService,
        CircleNetworkRequestService circleNetworkRequestService,
        ILogger<CircleNetworkIntroductionService> logger,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantSystemStorage tenantSystemStorage,
        FileSystemResolver fileSystemResolver,
        TransitInboxBoxStorage inboxBoxStorage,
        IMediator mediator,
        DriveManager driveManager) : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
    {
        _odinConfiguration = odinConfiguration;
        _circleNetworkRequestService = circleNetworkRequestService;
        _logger = logger;
        _odinHttpClientFactory = odinHttpClientFactory;
        _inboxBoxStorage = inboxBoxStorage;
        _mediator = mediator;
        _driveManager = driveManager;


        const string receivedIntroductionContextKey = "d2f5c94c-c299-4122-8aa2-744d91f3b12d";
        _receivedIntroductionValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(receivedIntroductionContextKey));
    }


    /// <summary>
    /// Introduces a group of identities to each other
    /// </summary>
    public async Task<IntroductionResult> SendIntroductions(IntroductionGroup group, IOdinContext odinContext, DatabaseConnection cn)
    {
        OdinValidationUtils.AssertNotNull(group, nameof(group));
        OdinValidationUtils.AssertValidRecipientList(group.Recipients, allowEmpty: false);

        var recipients = group.Recipients.ToOdinIdList();

        var result = new IntroductionResult();
        foreach (var recipient in recipients.Without(odinContext.Tenant))
        {
            var introduction = new Introduction
            {
                Message = group.Message,
                Identities = recipients.Without(recipient).ToDomainNames(),
                Timestamp = UnixTimeUtc.Now()
            };

            var success = await MakeIntroduction(recipient, introduction, odinContext, cn);
            result.RecipientStatus[recipient] = success;
        }

        return result;
    }

    /// <summary>
    /// Stores an incoming introduction
    /// </summary>
    public async Task ReceiveIntroductions(SharedSecretEncryptedPayload payload, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.AllowIntroductions);
        
        OdinValidationUtils.AssertNotNull(payload, nameof(payload));

        var payloadBytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
        Introduction introduction = OdinSystemSerializer.Deserialize<Introduction>(payloadBytes.ToStringFromUtf8Bytes());

        OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
        OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

        introduction.Timestamp = UnixTimeUtc.Now();
        var introducerOdinId = odinContext.GetCallerOdinIdOrFail();

        foreach (var identity in introduction.Identities.ToOdinIdList())
        {
            // Note: we do not check if you're already connected or
            // have blocked the identity being introduced as we do not 
            // want to communicate any such information to the introducer
            
            var iid = new IdentityIntroduction()
            {
                IntroducerOdinId = introducerOdinId,
                Identity = identity,
                Message = introduction.Message
            };

            var key = MakeReceivedIntroductionKey(identity);
            _receivedIntroductionValueStorage.Upsert(cn, key, GuidId.Empty, _receivedIntroductionDataType, iid);

            await EnqueueInboxItemToSendConnectionRequest(iid, odinContext, cn);
        }

        await _mediator.Publish(new IntroductionsReceivedNotification()
        {
            IntroducerOdinId = introducerOdinId,
            Introduction = introduction,
            OdinContext = odinContext,
            DatabaseConnection = cn
        });

        await Task.CompletedTask;
    }

    public async Task AutoAcceptEligibleConnectionRequests(IOdinContext odinContext, DatabaseConnection connection)
    {
        // get all the introductions
        odinContext.Caller.AssertHasMasterKey();

        var incomingConnectionRequests = await _circleNetworkRequestService.GetPendingRequests(PageOptions.All, odinContext, connection);

        foreach (var request in incomingConnectionRequests.Results)
        {
            var sender = request.SenderOdinId;

            var introduction = await this.GetIntroduction(sender, connection);
            if (null != introduction)
            {
                await AutoAccept(sender, odinContext, connection);
            }

            var existingSentRequest = await _circleNetworkRequestService.GetSentRequest(sender, odinContext, connection);
            if (null != existingSentRequest)
            {
                await AutoAccept(sender, odinContext, connection);
            }
        }
    }

    /// <summary>
    /// Sends connection requests for pending introductions if one has not already been sent or received
    /// </summary>
    public async Task SendConnectionRequests(OdinId sender, IdentityIntroduction identityIntroduction, IOdinContext odinContext, DatabaseConnection cn)
    {
        // var hasOutstandingRequest = await _circleNetworkRequestService.HasPendingOrSentRequest(identityIntroduction.Identity, odinContext, cn);
        //
        // if (hasOutstandingRequest)
        // {
        //     //nothing to do
        //     _logger.LogDebug("Pending or sent request already exist for introduced identity [{iid}]", identityIntroduction.Identity);
        //     return;
        // }

        var id = Guid.NewGuid();
        var requestHeader = new ConnectionRequestHeader()
        {
            Id = id,
            Recipient = identityIntroduction.Identity,
            Message = identityIntroduction.Message,
            IntroducerOdinId = sender,
            ContactData = new ContactRequestData(),
            CircleIds = [],
            ConnectionRequestOrigin = ConnectionRequestOrigin.Introduction
        };

        await _circleNetworkRequestService.SendConnectionRequest(requestHeader, odinContext, cn);
    }

    public Task<List<IdentityIntroduction>> GetReceivedIntroductions(IOdinContext odinContext, DatabaseConnection cn)
    {
        var results = _receivedIntroductionValueStorage.GetByCategory<IdentityIntroduction>(cn, _receivedIntroductionDataType);
        return Task.FromResult(results.ToList());
    }

    private Guid MakeReceivedIntroductionKey(OdinId recipient)
    {
        var combined = ByteArrayUtil.Combine(recipient.ToHashId().ToByteArray(), _receivedIntroductionDataType);
        var bytes = ByteArrayUtil.ReduceSHA256Hash(combined);
        return new Guid(bytes);
    }

    private async Task EnqueueInboxItemToSendConnectionRequest(IdentityIntroduction item, IOdinContext odinContext, DatabaseConnection cn)
    {
        //hack - the inbox requires a target drive
        var targetDrive = SystemDriveConstants.FeedDrive;
        var driveId = await _driveManager.GetDriveIdByAlias(targetDrive, cn);

        await _inboxBoxStorage.Add(new TransferInboxItem
        {
            Id = Guid.NewGuid(),
            InstructionType = TransferInstructionType.HandleIntroductions,
            AddedTimestamp = UnixTimeUtc.Now(),
            Sender = odinContext.GetCallerOdinIdOrFail(),
            Priority = 190,

            Data = OdinSystemSerializer.Serialize(item).ToUtf8ByteArray(),

            FileId = Guid.NewGuid(), //hack
            DriveId = driveId.GetValueOrDefault(),
            TransferFileType = TransferFileType.Normal,
            FileSystemType = FileSystemType.Standard,


            GlobalTransitId = default,
            SharedSecretEncryptedKeyHeader = null,
            TransferInstructionSet = null,
            EncryptedFeedPayload = null
        }, cn);

        await _mediator.Publish(new InboxItemReceivedNotification
        {
            OdinContext = odinContext,
            TargetDrive = targetDrive,
            FileSystemType = default,
            TransferFileType = TransferFileType.Normal,
            DatabaseConnection = cn
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Introduces <see cref="Introduction.Identities"/> to the recipient identity
    /// </summary>
    private async Task<bool> MakeIntroduction(OdinId recipient, Introduction introduction, IOdinContext odinContext, DatabaseConnection cn)
    {
        OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
        OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

        bool success = false;
        try
        {
            var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, cn, false);

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
            throw e.InnerException ?? e;
        }

        return success;
    }

    private Task<IdentityIntroduction> GetIntroduction(OdinId identity, DatabaseConnection cn)
    {
        var key = MakeReceivedIntroductionKey(identity);
        var result = _receivedIntroductionValueStorage.Get<IdentityIntroduction>(cn, key);
        return Task.FromResult(result);
    }

    private async Task AutoAccept(OdinId sender, IOdinContext odinContext, DatabaseConnection connection)
    {
        var header = new AcceptRequestHeader()
        {
            Sender = sender,
            CircleIds = [],
            ContactData = new ContactRequestData(),
        };

        await _circleNetworkRequestService.AcceptConnectionRequest(header, tryOverrideAcl: true, odinContext, connection);
    }
}