using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Services.Util;
using Refit;

namespace Odin.Services.Peer
{
    /// <summary>
    /// Base class for the transit subsystem providing various functions specific to Transit
    /// </summary>
    public abstract class PeerServiceBase(
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration)
    {
        protected readonly IOdinHttpClientFactory OdinHttpClientFactory = odinHttpClientFactory;

        protected readonly OdinConfiguration OdinConfiguration = odinConfiguration;

        protected readonly CircleNetworkService CircleNetworkService = circleNetworkService;

        protected FileSystemResolver FileSystemResolver { get; } = fileSystemResolver;

        protected SharedSecretEncryptedTransitPayload CreateSharedSecretEncryptedPayload(ClientAccessToken token, object o)
        {
            var iv = ByteArrayUtil.GetRndByteArray(16);
            // var key = token?.SharedSecret ?? new SensitiveByteArray(Guid.Empty.ToByteArray());
            var jsonBytes = OdinSystemSerializer.Serialize(o).ToUtf8ByteArray();
            // var encryptedBytes = AesCbc.Encrypt(jsonBytes, ref key, iv);
            var encryptedBytes = jsonBytes;

            var payload = new SharedSecretEncryptedTransitPayload()
            {
                Iv = iv,
                Data = Convert.ToBase64String(encryptedBytes)
            };

            return payload;
        }

        protected async Task<ClientAccessToken> ResolveClientAccessTokenAsync(OdinId recipient, IOdinContext odinContext,
            bool failIfNotConnected = true)
        {
            //TODO: this check is duplicated in the TransitQueryService.CreateClient method; need to centralize
            odinContext.PermissionsContext.AssertHasAtLeastOnePermission(
                PermissionKeys.UseTransitWrite,
                PermissionKeys.UseTransitRead);

            //Note here we overrideHack the permission check because we have either UseTransitWrite or UseTransitRead
            var icr = await CircleNetworkService.GetIcrAsync(recipient, odinContext, overrideHack: true);
            if (icr?.IsConnected() == false)
            {
                if (failIfNotConnected)
                {
                    throw new OdinClientException("Cannot resolve client access token; not connected",
                        OdinClientErrorCode.NotAConnectedIdentity);
                }

                return null;
            }


            return icr!.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey());
        }

        protected async Task<(ClientAccessToken token, IPeerReactionHttpClient client)> CreateReactionContentClientAsync(OdinId odinId,
            IOdinContext odinContext,
            FileSystemType? fileSystemType = null)
        {
            var token = await ResolveClientAccessTokenAsync(odinId, odinContext, false);

            if (token == null)
            {
                var httpClient = OdinHttpClientFactory.CreateClient<IPeerReactionHttpClient>(odinId, fileSystemType);
                return (null, httpClient);
            }
            else
            {
                var httpClient = OdinHttpClientFactory.CreateClientUsingAccessToken<IPeerReactionHttpClient>(
                    odinId,
                    token.ToAuthenticationToken(),
                    fileSystemType);
                return (token, httpClient);
            }
        }

        protected async Task<(ClientAccessToken token, T client)> CreateHttpClientAsync<T>(
            OdinId odinId,
            IdentityDatabase db,
            IOdinContext odinContext)
        {
            var token = await ResolveClientAccessTokenAsync(odinId, odinContext);

            var httpClient = OdinHttpClientFactory.CreateClientUsingAccessToken<T>(
                odinId,
                token.ToAuthenticationToken());

            return (token, httpClient);
        }

        protected async Task<T> DecryptUsingSharedSecretAsync<T>(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
        {
            var caller = odinContext.Caller.OdinId;
            OdinValidationUtils.AssertIsTrue(caller.HasValue, "Caller OdinId missing");

            //TODO: put decryption back in place
            // var t = await ResolveClientAccessToken(caller!.Value, tokenSource);
            // var sharedSecret = t.SharedSecret;
            // var encryptedBytes = Convert.FromBase64String(payload.Data);
            // var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref sharedSecret, payload.Iv);

            var decryptedBytes = Convert.FromBase64String(payload.Data);
            var json = decryptedBytes.ToStringFromUtf8Bytes();
            await Task.CompletedTask;
            return OdinSystemSerializer.Deserialize<T>(json);
        }

        /// <summary>
        /// Looks up a file by a global transit identifier
        /// </summary>
        protected async Task<InternalDriveFileId?> ResolveInternalFile(GlobalTransitIdFileIdentifier file, IOdinContext odinContext,
            IdentityDatabase db,
            bool failIfNull = false)
        {
            var (_, fileId) = await FileSystemResolver.ResolveFileSystem(file, odinContext, db);

            if (failIfNull && fileId == null)
            {
                // throw new OdinRemoteIdentityException($"Invalid global transit id {file.GlobalTransitId} on drive {file.TargetDrive}");
                // logger.LogInformation($"Invalid global transit id {file.GlobalTransitId} on drive {file.TargetDrive}");
                throw new OdinClientException($"Invalid global transit id {file.GlobalTransitId} on drive {file.TargetDrive}",
                    OdinClientErrorCode.InvalidGlobalTransitId);
            }

            return fileId;
        }

        protected async Task<PeerTryRetryResult<TApiResponse>> ExecuteRequestWithHandlingAsync<TApiResponse>(
            OdinId remoteIdentity,
            Task<ApiResponse<TApiResponse>> task,
            CancellationToken cancellationToken,
            IOdinContext odinContext)
        {
            var result = await this.ExecuteRequestAsync(task, cancellationToken);
            var response = result.Response;
            if (!response.IsSuccessStatusCode)
            {
                var issueType = MapIssueType(response);

                switch (issueType)
                {
                    case PeerRequestIssueType.None:
                    case PeerRequestIssueType.DnsResolutionFailure:
                        break;

                    case PeerRequestIssueType.ForbiddenWithInvalidRemoteIcr:
                    case PeerRequestIssueType.Forbidden:
                        await CircleNetworkService.DisconnectAsync(remoteIdentity, odinContext);
                        throw new OdinSecurityException("Remote server returned 403");

                    case PeerRequestIssueType.ServiceUnavailable:
                        throw new OdinClientException("Remote server returned 503", OdinClientErrorCode.RemoteServerReturnedUnavailable);

                    case PeerRequestIssueType.InternalServerError:
                        throw new OdinClientException("Remote server returned 500",
                            OdinClientErrorCode.RemoteServerReturnedInternalServerError);

                    case PeerRequestIssueType.Unhandled:
                        throw new OdinSystemException($"Unhandled peer error response: {response.StatusCode}");

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return result;
        }

        /// <summary>
        /// Executes a http request with retry and error mapping
        /// </summary>
        protected async Task<PeerTryRetryResult<TApiResponse>> ExecuteRequestAsync<TApiResponse>(
            Task<ApiResponse<TApiResponse>> task, CancellationToken cancellationToken)
        {
            var result = new PeerTryRetryResult<TApiResponse>();

            try
            {
                await TryRetry.WithDelayAsync(
                    OdinConfiguration.Host.PeerOperationMaxAttempts,
                    OdinConfiguration.Host.PeerOperationDelayMs,
                    cancellationToken,
                    async () => { result.Response = await task; });

                result.IssueType = MapIssueType(result.Response);
            }
            catch (TryRetryException tryRetryException)
            {
                var e = tryRetryException.InnerException!;

                // these can be thrown when the remote identity's DNS is not resolving
                if (e is TaskCanceledException or HttpRequestException or OperationCanceledException or SocketException)
                {
                    result.IssueType = PeerRequestIssueType.DnsResolutionFailure;
                }
                else
                {
                    throw e;
                }
            }

            return result;
        }


        private PeerRequestIssueType MapIssueType<T>(ApiResponse<T> response)
        {
            PeerRequestIssueType issueType = PeerRequestIssueType.None;

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var remoteIcrIsInvalid = response.Headers.TryGetValues(HttpHeaderConstants.RemoteServerIcrIssue, out var values) &&
                                         bool.TryParse(values.SingleOrDefault() ?? bool.FalseString, out var isIcrIssue) && isIcrIssue;

                if (remoteIcrIsInvalid)
                {
                    issueType = PeerRequestIssueType.ForbiddenWithInvalidRemoteIcr;
                }
                else
                {
                    issueType = PeerRequestIssueType.Forbidden;
                }
            }

            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                issueType = PeerRequestIssueType.InternalServerError;
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                issueType = PeerRequestIssueType.ServiceUnavailable;
            }

            if (!response.IsSuccessStatusCode || response.Content == null)
            {
                issueType = PeerRequestIssueType.Unhandled;
            }

            return issueType;
        }
    }
}