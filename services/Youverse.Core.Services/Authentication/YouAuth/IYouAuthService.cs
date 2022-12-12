using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.ExchangeGrants;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
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
        ValueTask<ClientAccessToken> RegisterBrowserAccess(string dotYouId, ClientAuthenticationToken? remoteIcrClientAuthToken);

        ValueTask DeleteSession(string subject);
    }
}