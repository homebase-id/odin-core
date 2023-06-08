#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Membership;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthService : IYouAuthService
    {
        private readonly ILogger<YouAuthService> _logger;
        private readonly IYouAuthAuthorizationCodeManager _youAuthAuthorizationCodeManager;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly IYouAuthRegistrationService _registrationService;
        private readonly ICircleNetworkService _circleNetwork;

        //

        public YouAuthService(
            ILogger<YouAuthService> logger,
            IYouAuthAuthorizationCodeManager youAuthAuthorizationCodeManager,
            IOdinHttpClientFactory odinHttpClientFactory,
            ICircleNetworkService circleNetwork, IYouAuthRegistrationService registrationService)
        {
            _logger = logger;
            _youAuthAuthorizationCodeManager = youAuthAuthorizationCodeManager;
            _odinHttpClientFactory = odinHttpClientFactory;
            _circleNetwork = circleNetwork;
            _registrationService = registrationService;
        }

        //

        public ValueTask<string> CreateAuthorizationCode(string initiator, string subject)
        {
            return _youAuthAuthorizationCodeManager.CreateAuthorizationCode(initiator, subject);
        }

        //

        public async ValueTask<(bool, ClientAuthenticationToken?)> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode)
        {
            // var queryString = QueryString.Create(new Dictionary<string, string>()
            // {
            //     {YouAuthDefaults.Initiator, initiator},
            //     {YouAuthDefaults.AuthorizationCode, authorizationCode},
            // });
            //
            // var url = $"https://{subject}".UrlAppend(
            //     Constants.Urls.AuthenticationBasePath,
            //     YouAuthDefaults.CompleteCodeFlowPath,
            //     queryString.ToUriComponent());

            // var request = new HttpRequestMessage(HttpMethod.Get, url);
            // var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);

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
            var isValid = await _youAuthAuthorizationCodeManager.ValidateAuthorizationCode(initiator, authorizationCode);

            byte[] clientAuthTokenBytes = Array.Empty<byte>();
            if (isValid)
            {
                string odinId = initiator;
                var info = await _circleNetwork.GetIdentityConnectionRegistration((OdinId)odinId, isValid);
                if (info.IsConnected())
                {
                    //TODO: RSA Encrypt or used shared secret?
                    clientAuthTokenBytes = info.CreateClientAuthToken().ToString().ToUtf8ByteArray();
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