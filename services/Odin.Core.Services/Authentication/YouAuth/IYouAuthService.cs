#nullable enable
using System.Threading.Tasks;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthService
    {
        ValueTask<string> CreateAuthorizationCode(string initiator, string subject);
        ValueTask<(bool, ClientAuthenticationToken?)> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode);
        ValueTask<(bool, byte[])> ValidateAuthorizationCode(string initiator, string authorizationCode);

        /// <summary>
        /// Creates an <see cref="AccessRegistration"/> for the browser and <see cref="ClientAccessToken"/> for use with the YouAuth Protocol
        /// </summary>
        /// <returns></returns>
        ValueTask<ClientAccessToken> RegisterBrowserAccess(string odinId, ClientAuthenticationToken? remoteIcrClientAuthToken);

        ValueTask DeleteSession(string subject);
    }
}