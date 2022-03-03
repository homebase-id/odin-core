using System.Threading.Tasks;
using Youverse.Core.Cryptography;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthService
    {
        ValueTask<string> CreateAuthorizationCode(string initiator, string subject);
        ValueTask<(bool, byte[])> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode);
        ValueTask<(bool, byte[])> ValidateAuthorizationCode(string initiator, string authorizationCode);

        ValueTask<(YouAuthSession, SensitiveByteArray?, SensitiveByteArray?)> CreateSession(string subject, SensitiveByteArray? remoteKey);

        ValueTask DeleteSession(string subject);
    }

}