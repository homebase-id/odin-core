using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer;
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
        private readonly ILogger<CircleNetworkIntroductionService> _logger;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;

        private readonly ThreeKeyValueStorage _receivedIntroductionValueStorage;
        private readonly ThreeKeyValueStorage _sentIntroductionValueStorage;

        public CircleNetworkIntroductionService(
            OdinConfiguration odinConfiguration,
            CircleNetworkService cns,
            ILogger<CircleNetworkIntroductionService> logger,
            IOdinHttpClientFactory odinHttpClientFactory,
            TenantSystemStorage tenantSystemStorage,
            FileSystemResolver fileSystemResolver) : base(odinHttpClientFactory, cns, fileSystemResolver)
        {
            _odinConfiguration = odinConfiguration;
            _logger = logger;
            _odinHttpClientFactory = odinHttpClientFactory;


            const string sentIntroductionContextKey = "103ad71c-5f07-4945-bac7-d5405af53959";
            _sentIntroductionValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(sentIntroductionContextKey));

            const string receivedIntroductionContextKey = "d2f5c94c-c299-4122-8aa2-744d91f3b12d";
            _receivedIntroductionValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(receivedIntroductionContextKey));
        }


        /// <summary>
        /// Sends a request a single identity asking it to make an introduction to the <see cref="IntroductionRequest.Recipients"/> on behalf of the caller
        /// </summary>
        public async Task SendIntroductionRequest(IntroductionRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidOdinId(request.Requester, out var requesterOdinId);
            OdinValidationUtils.AssertValidRecipientList(request.Recipients, allowEmpty: false);

            try
            {
                //TODO: Move all of this to the outbox
                var clientAuthToken = await ResolveClientAccessToken(requesterOdinId, odinContext, cn, false);

                ApiResponse<HttpContent> response;
                await TryRetry.WithDelayAsync(
                    _odinConfiguration.Host.PeerOperationMaxAttempts,
                    _odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () =>
                    {
                        var json = OdinSystemSerializer.Serialize(request);
                        var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), clientAuthToken.SharedSecret);
                        var client = _odinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkRequestHttpClient>(requesterOdinId,
                            clientAuthToken.ToAuthenticationToken());
                        response = await client.DeliverIntroductionRequest(encryptedPayload);

                        if (!response.IsSuccessStatusCode)
                        {
                            //TODO logging, etc
                            throw new OdinClientException("Failed to send introduction request");
                        }
                    });
            }
            catch (TryRetryException)
            {
                //TODO logging, etc
                throw new OdinClientException("Failed to send introduction request");
            }

            // store a list of identities to which I want to be introduced
            foreach (var r in request.Recipients)
            {
                var introduction = new Introduction
                {
                    Identity = r,
                    Timestamp = UnixTimeUtc.Now()
                };

                var key = MakeSentIntroductionKey(new OdinId(r));
                _sentIntroductionValueStorage.Upsert(cn, key, GuidId.Empty, _sentIntroductionDataType, introduction);
            }
        }

        /// <summary>
        /// Handles when a calling identity wants this identity to make introduction to other identities to which I'm connected
        /// </summary>
        public async Task ReceiveIntroductionRequest(SharedSecretEncryptedPayload payload, IOdinContext odinContext, DatabaseConnection cn)
        {
            OdinValidationUtils.AssertNotNull(payload, nameof(payload));

            var payloadBytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
            IntroductionRequest request = OdinSystemSerializer.Deserialize<IntroductionRequest>(payloadBytes.ToStringFromUtf8Bytes());

            //TODO: move to outbox
            foreach (var r in request.Recipients)
            {
                var recipient = (OdinId)r;

                var introduction = new Introduction
                {
                    Identity = odinContext.Caller.OdinId,
                    Timestamp = UnixTimeUtc.Now()
                };

                await this.MakeIntroduction(introduction, recipient, odinContext, cn);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Introduces <see cref="Introduction.Identity"/> to the recipient identity
        /// </summary>
        public async Task MakeIntroduction(Introduction request, OdinId recipient, IOdinContext odinContext, DatabaseConnection cn)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidOdinId(request.Identity, out _);

            try
            {
                var newOdinContext = OdinContextUpgrades.UpgradeToUseTransit(odinContext);
                //TODO: Move all of this to the outbox
                var clientAuthToken = await ResolveClientAccessToken(recipient, newOdinContext, cn, false);

                ApiResponse<HttpContent> response;
                await TryRetry.WithDelayAsync(
                    _odinConfiguration.Host.PeerOperationMaxAttempts,
                    _odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () =>
                    {
                        var json = OdinSystemSerializer.Serialize(request);
                        var encryptedPayload = SharedSecretEncryptedPayload.Encrypt(json.ToUtf8ByteArray(), clientAuthToken.SharedSecret);
                        var client = _odinHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkRequestHttpClient>(recipient,
                            clientAuthToken.ToAuthenticationToken());

                        response = await client.MakeIntroduction(encryptedPayload);

                        if (!response.IsSuccessStatusCode)
                        {
                            //TODO logging, etc
                            throw new OdinClientException("Failed to send introduction request");
                        }
                    });
            }
            catch (TryRetryException)
            {
                //TODO logging, etc
                throw new OdinClientException("Failed to send introduction request");
            }
        }

        public async Task ReceiveIntroduction(SharedSecretEncryptedPayload payload, IOdinContext odinContext, DatabaseConnection cn)
        {
            OdinValidationUtils.AssertNotNull(payload, nameof(payload));

            //TODO validate you're not already connected to request.Identity 

            var payloadBytes = payload.Decrypt(odinContext.PermissionsContext.SharedSecretKey);
            Introduction introduction = OdinSystemSerializer.Deserialize<Introduction>(payloadBytes.ToStringFromUtf8Bytes());
            introduction.Timestamp = UnixTimeUtc.Now();

            var key = MakeReceivedIntroductionKey(new OdinId(introduction.Identity));
            _receivedIntroductionValueStorage.Upsert(cn, key, GuidId.Empty, _receivedIntroductionDataType, introduction);

            await Task.CompletedTask;
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
    }
}