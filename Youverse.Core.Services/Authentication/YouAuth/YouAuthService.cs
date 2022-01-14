using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthService : IYouAuthService
    {
        private readonly ILogger<YouAuthService> _logger;
        private readonly IYouAuthAuthorizationCodeManager _youAuthAuthorizationCodeManager;
        private readonly IYouAuthSessionManager _youSessionManager;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;

        //

        public YouAuthService(
            ILogger<YouAuthService> logger,
            IYouAuthAuthorizationCodeManager youAuthAuthorizationCodeManager,
            IYouAuthSessionManager youSessionManager,
            IDotYouHttpClientFactory dotYouHttpClientFactory)
        {
            _logger = logger;
            _youAuthAuthorizationCodeManager = youAuthAuthorizationCodeManager;
            _youSessionManager = youSessionManager;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
        }

        //

        public ValueTask<string> CreateAuthorizationCode(string initiator, string subject)
        {
            return _youAuthAuthorizationCodeManager.CreateAuthorizationCode(initiator, subject);
        }

        //

        public async ValueTask<bool> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode)
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
                .CreateClient<IPerimeterHttpClient>(dotYouId, Guid.Parse("00000000-1111-0000-0000-00000000011"))
                .ValidateAuthorizationCodeResponse(initiator, authorizationCode);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogError("Validation of authorization code failed. HTTP status = {HttpStatusCode}", (int)response.StatusCode);
            return false;
        }

        //

        public ValueTask<bool> ValidateAuthorizationCode(string initiator, string authorizationCode)
        {
            return _youAuthAuthorizationCodeManager.ValidateAuthorizationCode(initiator, authorizationCode);
        }

        //

        public ValueTask<YouAuthSession> CreateSession(string subject)
        {
            return _youSessionManager.CreateSession(subject);
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