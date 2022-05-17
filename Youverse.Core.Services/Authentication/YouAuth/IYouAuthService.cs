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

        // ValueTask<(YouAuthSession, ClientAccessToken?)> CreateSession(string subject, ClientAuthToken? clientAuthToken);
        ValueTask<ClientAccessToken> CreateSession(string subject, ClientAuthenticationToken? icrClientAuthToken);

        ValueTask DeleteSession(string subject);
    }
}