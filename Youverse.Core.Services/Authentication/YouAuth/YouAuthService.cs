using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Exchange;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;

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

        private readonly XTokenService _xTokenService;
        //

        public YouAuthService(
            ILogger<YouAuthService> logger,
            IYouAuthAuthorizationCodeManager youAuthAuthorizationCodeManager,
            IYouAuthSessionManager youSessionManager,
            IDotYouHttpClientFactory dotYouHttpClientFactory,
            ICircleNetworkService circleNetwork, XTokenService xTokenService)
        {
            _logger = logger;
            _youAuthAuthorizationCodeManager = youAuthAuthorizationCodeManager;
            _youSessionManager = youSessionManager;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _circleNetwork = circleNetwork;
            _xTokenService = xTokenService;
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

            if (response.IsSuccessStatusCode)
            {
                if (null != response.Content && response.Content.Length > 0)
                {
                    return (true, response.Content);
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

            byte[] halfKey = Array.Empty<byte>();
            if (isValid)
            {
                string dotYouId = initiator;
                var info = await _circleNetwork.GetConnectionInfo((DotYouIdentity) dotYouId, isValid);
                if (info.IsConnected())
                {
                    //TODO: RSA Encrypt
                    //halfKey = info.XToken.DriveKeyHalfKey.KeyEncrypted;
                    halfKey = info.RemoteHalf;
                }
            }

            return (isValid, halfKey);
        }

        //

        public async ValueTask<(YouAuthSession, byte[]?)> CreateSession(string subject, SensitiveByteArray? xTokenHalfKey)
        {
            XToken token = null;
            byte[] halfKey = null;
            
            if (xTokenHalfKey != null)
            {
                var connection = await _circleNetwork.GetConnectionInfo((DotYouIdentity) subject, xTokenHalfKey);
                if (connection.IsConnected())
                {
                    var xToken = connection.XToken;
                    (token, halfKey) = await _xTokenService.CloneXToken(xToken, xTokenHalfKey);
                }
            }

            var session = await _youSessionManager.CreateSession(subject, token);
            return (session, halfKey);
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