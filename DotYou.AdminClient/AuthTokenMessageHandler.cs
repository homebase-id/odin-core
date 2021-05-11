using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DotYou.Types.Admin;

namespace DotYou.AdminClient
{
    /// <summary>
    /// Adds the auth token as a header to all http requests
    /// </summary>
    public class AuthTokenMessageHandler : DelegatingHandler
    {
        private AuthState _state;

        public AuthTokenMessageHandler(AuthState state)
        {
            _state = state;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.Add(DotYouHeaderNames.AuthToken, _state.Token.ToString());
            return base.SendAsync(request, cancellationToken);
        }
    }
}