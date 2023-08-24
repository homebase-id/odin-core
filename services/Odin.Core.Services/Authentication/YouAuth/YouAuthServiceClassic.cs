#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthServiceClassic : IYouAuthService
    {
        private readonly ILogger<YouAuthServiceClassic> _logger;
        private readonly IYouAuthAuthorizationCodeManager _youAuthAuthorizationCodeManager;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly IYouAuthRegistrationServiceClassic _registrationServiceClassic;
        private readonly CircleNetworkService _circleNetwork;

        //

        public YouAuthServiceClassic(
            ILogger<YouAuthServiceClassic> logger,
            IYouAuthAuthorizationCodeManager youAuthAuthorizationCodeManager,
            IOdinHttpClientFactory odinHttpClientFactory,
            CircleNetworkService circleNetwork, IYouAuthRegistrationServiceClassic registrationServiceClassic)
        {
            _logger = logger;
            _youAuthAuthorizationCodeManager = youAuthAuthorizationCodeManager;
            _odinHttpClientFactory = odinHttpClientFactory;
            _circleNetwork = circleNetwork;
            _registrationServiceClassic = registrationServiceClassic;
        }

        //

        public ValueTask<string> CreateAuthorizationCode(string initiator, string subject)
        {
            return _youAuthAuthorizationCodeManager.CreateAuthorizationCode(initiator, subject);
        }

        //

        public async ValueTask<(bool, ClientAuthenticationToken?)> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode)
        {
            var odinId = new OdinId(subject);
            var response = await _odinHttpClientFactory
                .CreateClient<IYouAuthPerimeterHttpClient>(odinId)
                .ValidateAuthorizationCodeResponse(initiator, authorizationCode);

            //NOTE: this is option #2 in YouAuth - DI Host to DI Host, returns caller remote key to unlock xtoken

            if (response.IsSuccessStatusCode)
            {
                if (null != response.Content && response.Content.Length > 0)
                {
                    var clientAuthTokenBytes = response.Content;

                    if (ClientAuthenticationToken.TryParse(clientAuthTokenBytes.ToStringFromUtf8Bytes(), out var remoteIcrClientAuthToken))
                    {
                        return (true, remoteIcrClientAuthToken);
                    }

                    //TODO: log a warning here that a bad payload was returned.
                    return (true, null);
                }

                return (true, null)!;
            }

            _logger.LogError("Validation of authorization code failed. HTTP status = {HttpStatusCode}", (int)response.StatusCode);
            return (false, null)!;
        }

        //

        public async ValueTask<(bool, byte[])> ValidateAuthorizationCode(string initiator, string authorizationCode)
        {
            var isValid = await _youAuthAuthorizationCodeManager.ValidateAuthorizationCode(initiator, authorizationCode, out var tempIcrKey);

            //
            // If the code is good, and the caller is connected, return the auth token
            //
            byte[] clientAuthTokenBytes = Array.Empty<byte>();
            if (isValid)
            {
                string odinId = initiator;
                var info = await _circleNetwork.GetIdentityConnectionRegistration((OdinId)odinId, isValid);
                if (info.IsConnected())
                {
                    //TODO: RSA Encrypt or used shared secret?
                    clientAuthTokenBytes = info.CreateClientAuthToken(tempIcrKey).ToString().ToUtf8ByteArray();
                    tempIcrKey?.Wipe();
                }
            }

            return (isValid, clientAuthTokenBytes);
        }

        //

        public async ValueTask<ClientAccessToken> RegisterBrowserAccess(string odinId, ClientAuthenticationToken? remoteIcrClientAuthToken)
        {
            var browserClientAccessToken = await _registrationServiceClassic.RegisterYouAuthAccess(odinId, remoteIcrClientAuthToken!);
            return browserClientAccessToken;
        }

        //

        public ValueTask DeleteSession(string subject)
        {
            //TODO: need to delete an access registration?
            return ValueTask.CompletedTask;
        }

        //
    }

    //
}