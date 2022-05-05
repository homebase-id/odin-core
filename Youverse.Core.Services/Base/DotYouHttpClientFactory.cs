using System;
using System.Net.Http;
using System.Security.Authentication;
using Dawn;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class DotYouHttpClientFactory : IDotYouHttpClientFactory
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ICertificateResolver _certificateResolver;
        private readonly ICircleNetworkService _circleNetworkService;

        public DotYouHttpClientFactory(DotYouContextAccessor contextAccessor, ICertificateResolver certificateResolver, ICircleNetworkService circleNetworkService)
        {
            _contextAccessor = contextAccessor;
            _certificateResolver = certificateResolver;
            _circleNetworkService = circleNetworkService;
        }

        public IPerimeterHttpClient CreateClient(DotYouIdentity dotYouId, bool requireClientAccessToken = false)
        {
            return this.CreateClient<IPerimeterHttpClient>(dotYouId, null, requireClientAccessToken);
        }

        public T CreateClientWithAccessToken<T>(DotYouIdentity dotYouId, ClientAuthToken clientAuthToken, Guid? appIdOverride = null)
        {
            return this.CreateClientInternal<T>(dotYouId, clientAuthToken, true, appIdOverride);
        }

        public T CreateClient<T>(DotYouIdentity dotYouId, Guid? appIdOverride = null, bool requireClientAccessToken = true)
        {
            if (requireClientAccessToken)
            {
                //TODO: need to NOT use the override version of GetIdentityConnectionRegistration but rather pass in some identifying token?
                var identityReg = _circleNetworkService.GetIdentityConnectionRegistration(dotYouId, true).GetAwaiter().GetResult();
                if (identityReg.IsConnected())
                {
                    var clientAuthToken = new ClientAuthToken()
                    {
                        Id = identityReg.AccessRegistrationId,
                        AccessTokenHalfKey = identityReg.ClientAccessTokenHalfKey.ToSensitiveByteArray()
                    };

                    return this.CreateClientInternal<T>(dotYouId, clientAuthToken, true, appIdOverride);
                }

                throw new MissingDataException("Client Access token required to create DotYouHttpClient");
            }

            return this.CreateClientInternal<T>(dotYouId, null, false, appIdOverride);
        }

        ///
        private T CreateClientInternal<T>(DotYouIdentity dotYouId, ClientAuthToken clientAuthToken, bool requireClientAccessToken, Guid? appIdOverride = null)
        {
            Guard.Argument(dotYouId.Id, nameof(dotYouId)).NotNull();

            if (requireClientAccessToken)
            {
                Guard.Argument(clientAuthToken, nameof(clientAuthToken)).NotNull();
                Guard.Argument(clientAuthToken.Id, nameof(clientAuthToken.Id)).Require(x => x != Guid.Empty);
                Guard.Argument(clientAuthToken.AccessTokenHalfKey, nameof(clientAuthToken.AccessTokenHalfKey)).Require(x => x.IsSet());
                Guard.Argument(clientAuthToken, nameof(clientAuthToken)).NotNull();
            }

            var appId = appIdOverride.HasValue ? appIdOverride.ToString() : _contextAccessor.GetCurrent().AppContext?.AppId.ToString() ?? "";
            Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();

            var handler = new HttpClientHandler();

            //HACK: this appIdOverride is strange but required so the background sender
            //can specify the app since it doesnt know
            // Console.WriteLine("CreateClient -> Loading certificate");
            var cert = _certificateResolver.GetSslCertificate();
            if (null == cert)
            {
                throw new Exception($"No certificate configured for {dotYouId}");
            }

            handler.ClientCertificates.Add(cert);
            handler.AllowAutoRedirect = false; //we should go directly to the endpoint; nothing in between
            handler.SslProtocols = SslProtocols.None; //allow OS to choose;
            //handler.ServerCertificateCustomValidationCallback

            var client = new HttpClient(handler)
            {
                BaseAddress = new UriBuilder()
                {
                    Scheme = "https",
                    Host = dotYouId
                }.Uri
            };

            client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, appId);
            if (null != clientAuthToken)
            {
                //TODO: need to encrypt this token somehow? (shared secret?)
                client.DefaultRequestHeaders.Add(DotYouHeaderNames.ClientAuthToken, clientAuthToken.ToString());
            }

            var ogClient = RestService.For<T>(client);
            return ogClient;
        }
    }
}