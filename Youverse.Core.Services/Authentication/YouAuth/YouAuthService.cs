using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthService : IYouAuthService
    {
        private readonly ILogger<YouAuthService> _logger;
        private readonly IYouAuthAuthorizationCodeManager _youAuthAuthorizationCodeManager;
        private readonly IYouAuthSessionManager _youSessionManager;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;

        private readonly ICircleNetworkService _circleNetwork;

        private readonly ExchangeGrantService _exchangeGrantService;
        //

        public YouAuthService(
            ILogger<YouAuthService> logger,
            IYouAuthAuthorizationCodeManager youAuthAuthorizationCodeManager,
            IYouAuthSessionManager youSessionManager,
            IDotYouHttpClientFactory dotYouHttpClientFactory,
            ICircleNetworkService circleNetwork, ExchangeGrantService exchangeGrantService)
        {
            _logger = logger;
            _youAuthAuthorizationCodeManager = youAuthAuthorizationCodeManager;
            _youSessionManager = youSessionManager;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _circleNetwork = circleNetwork;
            _exchangeGrantService = exchangeGrantService;
        }

        //

        public ValueTask<string> CreateAuthorizationCode(string initiator, string subject)
        {
            return _youAuthAuthorizationCodeManager.CreateAuthorizationCode(initiator, subject);
        }

        //

        public async ValueTask<(bool, ClientAuthToken)> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode)
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

            var dotYouId = new DotYouIdentity(subject);
            var response = await _dotYouHttpClientFactory
                .CreateClient<IPerimeterHttpClient>(dotYouId, YouAuthDefaults.AppId, false)
                .ValidateAuthorizationCodeResponse(initiator, authorizationCode);

            //NOTE: this is option #2 in YouAuth - DI Host to DI Host, returns caller remote key to unlock xtoken

            if (response.IsSuccessStatusCode)
            {
                if (null != response.Content && response.Content.Length > 0)
                {
                    var clientAuthTokenBytes = response.Content;

                    if (ClientAuthToken.TryParse(clientAuthTokenBytes.StringFromUTF8Bytes(), out var clientAuthToken))
                    {
                        return (true, clientAuthToken);
                    }

                    //TODO: log a warning here that a bad payload was returned.
                    return (true, null);
                }

                return (true, null)!;
            }

            _logger.LogError("Validation of authorization code failed. HTTP status = {HttpStatusCode}", (int) response.StatusCode);
            return (false, null)!;
        }

        //

        public async ValueTask<(bool, byte[])> ValidateAuthorizationCode(string initiator, string authorizationCode)
        {
            var isValid = await _youAuthAuthorizationCodeManager.ValidateAuthorizationCode(initiator, authorizationCode);

            byte[] clientAuthTokenBytes = Array.Empty<byte>();
            if (isValid)
            {
                string dotYouId = initiator;
                var info = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity) dotYouId, isValid);
                if (info.IsConnected())
                {
                    var clientAuthToken = new ClientAuthToken()
                    {
                        Id = info.ClientAccessTokenId,
                        AccessTokenHalfKey = info.ClientAccessTokenHalfKey.ToSensitiveByteArray()
                    };

                    //TODO: RSA Encrypt or used shared secret?
                    clientAuthTokenBytes = clientAuthToken.ToString().ToUtf8ByteArray();
                }
            }

            return (isValid, clientAuthTokenBytes);
        }

        //

        public async ValueTask<(YouAuthSession, ClientAccessToken?)> CreateSession(string subject, ClientAuthToken? clientAuthToken)
        {
            AccessRegistration? browserAccessRegistration = null;
            ClientAccessToken? browserClientAccessToken = null;

            //If they were not connected, we need to create a new EGR and AccessReg for the browser
            if (clientAuthToken == null)
            {
                //TODO: need to consider putting the dotyouid on the egr so we can upgrade it when 
                (browserAccessRegistration, browserClientAccessToken) = await _exchangeGrantService.RegisterIdentityExchangeGrantForUnencryptedData((DotYouIdentity) subject, null, null);
            }
            else
            {
                //If we're given a client auth token, it means that subject was connected, so we just need to create a browser specific AccessReg
                //look up the EGR key using the clientAuthToken
                (browserAccessRegistration, browserClientAccessToken) = await _exchangeGrantService.AddClientToExchangeGrant(clientAuthToken);
            }

            var session = await _youSessionManager.CreateSession(subject, browserAccessRegistration?.Id);
            return (session, browserClientAccessToken);
        }

        //

        public ValueTask DeleteSession(string subject)
        {
            return _youSessionManager.DeleteFromSubject(subject);
        }

        //
    }

    //
}