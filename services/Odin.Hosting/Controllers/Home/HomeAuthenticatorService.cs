#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace Odin.Hosting.Controllers.Home
{
    public sealed class HomeAuthenticatorService
    {
        private readonly ILogger<HomeAuthenticatorService> _logger;
        private readonly IYouAuthAuthorizationCodeManager _youAuthAuthorizationCodeManager;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly IHomeRegistrationService _registrationService;
        private readonly CircleNetworkService _circleNetwork;

        //

        public HomeAuthenticatorService(
            ILogger<HomeAuthenticatorService> logger,
            IYouAuthAuthorizationCodeManager youAuthAuthorizationCodeManager,
            IOdinHttpClientFactory odinHttpClientFactory,
            CircleNetworkService circleNetwork, IHomeRegistrationService registrationService)
        {
            _logger = logger;
            _youAuthAuthorizationCodeManager = youAuthAuthorizationCodeManager;
            _odinHttpClientFactory = odinHttpClientFactory;
            _circleNetwork = circleNetwork;
            _registrationService = registrationService;
        }

        //

        public async ValueTask<YouAuthTokenResponse?> ExchangeCodeForToken(OdinId odinId, string authorizationCode, string digest)
        {
            var tokenRequest = new YouAuthTokenRequest
            {
                Code = authorizationCode,
                SecretDigest = digest
            };

            var response = await _odinHttpClientFactory
                .CreateClient<IHomePerimeterHttpClient>(odinId)
                .ExchangeCodeForToken(tokenRequest);

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                return response.Content;
            }

            return null;
            
            //TODO: need to determine how to handle these scenarios
            
            // if (response.StatusCode == HttpStatusCode.BadRequest)
            // {
            // }
            //
            // if (response.StatusCode == HttpStatusCode.NotFound)
            // {
            //     throw new OdinClientException("");
            // }

            // throw new OdinSystemException("unhandled scenario");
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
            var browserClientAccessToken = await _registrationService.RegisterYouAuthAccess(odinId, remoteIcrClientAuthToken!);
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