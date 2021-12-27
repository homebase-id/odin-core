using System;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication.AppAuth
{
    //TODO: need to fully implement
    public class AppAuthenticationService : IAppAuthenticationService
    {
        private readonly ISystemStorage _systemStorage;
        private const string AppAuthTokenCollection = "apptko";

        public AppAuthenticationService(DotYouContext context, ILogger<IOwnerAuthenticationService> logger, ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }

        public async Task<DotYouAuthenticationResult> Authenticate(AppDevice appDevice)
        {
            return new DotYouAuthenticationResult()
            {
                SessionToken = Guid.Empty,
                ClientHalfKek = new SecureKey(Guid.Empty.ToByteArray())
            };
        }

        public Task<bool> IsValidAppDevice(Guid sessionToken, out AppDevice appDevice)
        {
            //TODO: look up from token storage
            
            //TODO: need to validate the app is not revoked

            //TODO: need to validate that this deviceUid has not been rejected/blocked

            appDevice = new AppDevice()
            {
                AppId = "super bob",
                DeviceUid = Guid.Empty.ToByteArray()
            };

            return Task.FromResult(true);
        }

        public Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            return Task.CompletedTask;
        }

        public void ExpireToken(Guid token)
        {
        }

        // private async Task<LoginTokenData> GetValidatedEntry(Guid token)
        // {
        //     var entry = await _systemStorage.WithTenantSystemStorageReturnSingle<LoginTokenData>(AppAuthTokenCollection, s => s.Get(token));
        //     AssertTokenIsValid(entry);
        //     return entry;
        // }
        //
        // private bool IsAuthTokenEntryValid(LoginTokenData entry)
        // {
        //     var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        //     var valid =
        //         null != entry &&
        //         entry.Id != Guid.Empty &&
        //         entry.ExpiryUnixTime > now;
        //
        //     return valid;
        // }
        //
        // private void AssertTokenIsValid(LoginTokenData entry)
        // {
        //     if (IsAuthTokenEntryValid(entry) == false)
        //     {
        //         throw new AuthenticationException();
        //     }
        // }
    }
}