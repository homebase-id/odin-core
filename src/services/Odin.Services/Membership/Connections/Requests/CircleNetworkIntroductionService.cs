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
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;
using Refit;


namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Enables introducing identities to each other
    /// </summary>
    public class CircleNetworkIntroductionService : PeerServiceBase
    {
        private readonly byte[] _sentIntroductionDataType = Guid.Parse("268c5895-2c8c-46e4-8b3e-7ecc9df078c6").ToByteArray();
        private readonly byte[] _receivedIntroductionDataType = Guid.Parse("9b844f10-9580-4cef-82e6-45b21eb40f62").ToByteArray();

        private readonly OdinConfiguration _odinConfiguration;
        private readonly CircleNetworkRequestService _circleNetworkRequestService;
        private readonly ILogger<CircleNetworkIntroductionService> _logger;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly TransitInboxBoxStorage _inboxBoxStorage;
        private readonly IMediator _mediator;
        private readonly DriveManager _driveManager;

        private readonly ThreeKeyValueStorage _receivedIntroductionValueStorage;
        private readonly ThreeKeyValueStorage _sentIntroductionValueStorage;

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


            const string sentIntroductionContextKey = "103ad71c-5f07-4945-bac7-d5405af53959";
            _sentIntroductionValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(sentIntroductionContextKey));

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
        public async Task ReceiveIntroduction(SharedSecretEncryptedPayload payload, IOdinContext odinContext, DatabaseConnection cn)
        {
            OdinValidationUtils.AssertNotNull(payload, nameof(payload));

            var payloadBytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
            Introduction introduction = OdinSystemSerializer.Deserialize<Introduction>(payloadBytes.ToStringFromUtf8Bytes());

            OdinValidationUtils.AssertNotNull(introduction, nameof(introduction));
            OdinValidationUtils.AssertValidRecipientList(introduction.Identities, allowEmpty: false);

            introduction.Timestamp = UnixTimeUtc.Now();

            foreach (var identity in introduction.Identities.ToOdinIdList())
            {
                //TODO validate you're not already connected to request.Identity and that you're not blocked
                // var existingConnection = this._circleNetworkService.GetIdentityConnectionAccessRegistration(identity,...);

                var key = MakeReceivedIntroductionKey(identity);
                _receivedIntroductionValueStorage.Upsert(cn, key, GuidId.Empty, _receivedIntroductionDataType, introduction);

                await EnqueueInboxItemToSendConnectionRequest(identity, introduction, odinContext, cn);
            }


            await Task.CompletedTask;
        }

        /// <summary>
        /// Sends connection requests for pending introductions if one has not already been sent or received
        /// </summary>
        public async Task SendConnectionRequests(OdinId sender, IdentityIntroduction identityIntroduction, IOdinContext odinContext, DatabaseConnection cn)
        {
            var hasOutstandingRequest = await _circleNetworkRequestService.HasPendingOrSentRequest(identityIntroduction.Identity, odinContext, cn);

            if (hasOutstandingRequest)
            {
                //nothing to do
                _logger.LogDebug("Pending or sent request already exist for introduced identity [{iid}]", identityIntroduction.Identity);
                return;
            }

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = identityIntroduction.Identity,
                // Message = $"Hello!  I was introduced to you by {sender}.  Let's connect!",
                Message = identityIntroduction.Message,
                IntroducerOdinId = sender,
                ContactData = new ContactRequestData(),
                CircleIds = [SystemCircleConstants.AutoConnectionsCircleId]
            };

            await _circleNetworkRequestService.SendConnectionRequest(requestHeader, odinContext, cn);
        }

        public Task<List<Introduction>> GetReceivedIntroductions(IOdinContext odinContext, DatabaseConnection cn)
        {
            var results = _receivedIntroductionValueStorage.GetByCategory<Introduction>(cn, _receivedIntroductionDataType);
            return Task.FromResult(results.ToList());
        }

        public Task<List<Introduction>> GetSentIntroductions(IOdinContext odinContext, DatabaseConnection cn)
        {
            var results = _sentIntroductionValueStorage.GetByCategory<Introduction>(cn, _sentIntroductionDataType);
            return Task.FromResult(results.ToList());
        }

        private Guid MakeSentIntroductionKey(OdinId recipient)
        {
            var combined = ByteArrayUtil.Combine(recipient.ToHashId().ToByteArray(), _sentIntroductionDataType);
            var bytes = ByteArrayUtil.ReduceSHA256Hash(combined);
            return new Guid(bytes);
        }

        private Guid MakeReceivedIntroductionKey(OdinId recipient)
        {
            var combined = ByteArrayUtil.Combine(recipient.ToHashId().ToByteArray(), _receivedIntroductionDataType);
            var bytes = ByteArrayUtil.ReduceSHA256Hash(combined);
            return new Guid(bytes);
        }

        private async Task EnqueueInboxItemToSendConnectionRequest(OdinId identity, Introduction introduction, IOdinContext odinContext, DatabaseConnection cn)
        {
            //hack - the inbox requires a target drive
            var targetDrive = SystemDriveConstants.FeedDrive;
            var driveId = await _driveManager.GetDriveIdByAlias(targetDrive, cn);

            var item = new IdentityIntroduction()
            {
                Identity = identity,
                Message = introduction.Message
            };

            await _inboxBoxStorage.Add(new TransferInboxItem
            {
                Id = Guid.NewGuid(),
                InstructionType = TransferInstructionType.HandleIntroductions,
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),
                Priority = 190,

                Data = OdinSystemSerializer.Serialize(item),

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

            if (success)
            {
                // keep a record
                var key = MakeSentIntroductionKey(recipient);
                _sentIntroductionValueStorage.Upsert(cn, key, GuidId.Empty, _sentIntroductionDataType, introduction);
            }

            return success;
        }
    }
}