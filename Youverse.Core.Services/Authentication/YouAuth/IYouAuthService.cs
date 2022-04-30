using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.ExchangeGrants;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthService
    {
        ValueTask<string> CreateAuthorizationCode(string initiator, string subject);
        ValueTask<(bool, byte[])> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode);
        ValueTask<(bool, byte[])> ValidateAuthorizationCode(string initiator, string authorizationCode);

        ValueTask<(YouAuthSession, ClientAccessToken?)> CreateSession(string subject, SensitiveByteArray? remoteIdentityConnectionKey);

        ValueTask DeleteSession(string subject);
    }
}