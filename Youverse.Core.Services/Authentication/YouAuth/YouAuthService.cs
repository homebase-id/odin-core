using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Exchange;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
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

        private readonly ExchangeTokenService _exchangeTokenService;
        //

        public YouAuthService(
            ILogger<YouAuthService> logger,
            IYouAuthAuthorizationCodeManager youAuthAuthorizationCodeManager,
            IYouAuthSessionManager youSessionManager,
            IDotYouHttpClientFactory dotYouHttpClientFactory,
            ICircleNetworkService circleNetwork, ExchangeTokenService exchangeTokenService)
        {
            _logger = logger;
            _youAuthAuthorizationCodeManager = youAuthAuthorizationCodeManager;
            _youSessionManager = youSessionManager;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _circleNetwork = circleNetwork;
            _exchangeTokenService = exchangeTokenService;
        }

        //

        public ValueTask<string> CreateAuthorizationCode(string initiator, string subject)
        {
            return _youAuthAuthorizationCodeManager.CreateAuthorizationCode(initiator, subject);
        }

        //

        public async ValueTask<(bool, byte[])> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode)
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
                .CreateClient<IPerimeterHttpClient>(dotYouId, YouAuthDefaults.AppId)
                .ValidateAuthorizationCodeResponse(initiator, authorizationCode);

            //NOTE: this is option #2 in YouAuth - DI Host to DI Host, returns caller remote key to unlock xtoken

            if (response.IsSuccessStatusCode)
            {
                if (null != response.Content && response.Content.Length > 0)
                {
                    var remoteIdentityConnectionKey = response.Content;
                    return (true, remoteIdentityConnectionKey);
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

            byte[] remoteGrantKey = Array.Empty<byte>();
            if (isValid)
            {
                string dotYouId = initiator;
                var info = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity) dotYouId, isValid);
                if (info.IsConnected())
                {
                    //TODO: RSA Encrypt
                    remoteGrantKey = info.RemoteGrantKey;
                }
            }

            return (isValid, remoteGrantKey);
        }

        //

        public async ValueTask<(YouAuthSession, SensitiveByteArray?, SensitiveByteArray?)> CreateSession(string subject, SensitiveByteArray? remoteIdentityConnectionKey)
        {
            XTokenRegistration tokenRegistration = null;
            SensitiveByteArray xToken = null;
            SensitiveByteArray childSharedSecret = null;

            if (remoteIdentityConnectionKey != null)
            {
                var connection = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity) subject, remoteIdentityConnectionKey);
                if (connection.IsConnected())
                {
                    var reg = connection.ExchangeRegistration;
                    (tokenRegistration, xToken, childSharedSecret) = await _exchangeTokenService.RegisterXToken(reg, remoteIdentityConnectionKey);
                }
            }

            var session = await _youSessionManager.CreateSession(subject, tokenRegistration);
            return (session, xToken, childSharedSecret);
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