using System;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication.AppAuth
{
    //TODO: need to fully implement
    public class AppAuthenticationService : IAppAuthenticationService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly ILogger<IAppAuthenticationService> _logger;
        private const string AppAuthTokenCollection = "apptko";

        public AppAuthenticationService(DotYouContext context, ISystemStorage systemStorage, IAppRegistrationService appRegistrationService, ILogger<IAppAuthenticationService> logger)
        {
            _systemStorage = systemStorage;
            _appRegistrationService = appRegistrationService;
            _logger = logger;
        }

        public async Task<DotYouAuthenticationResult> Authenticate(AppDevice appDevice)
        {
            //TODO: check against the owner authentication service to ensure the owner has a valid session
            
            //TODO: need to validate the app is not revoked

            //TODO: need to validate that this deviceUid has not been rejected/blocked
            
            return new DotYouAuthenticationResult()
            {
                SessionToken = Guid.Empty,
                ClientHalfKek = new SecureKey(Guid.Empty.ToByteArray())
            };
        }

        public async Task<AppDevice> ValidateSessionToken(Guid token)
        {
            //TODO: look up appdevice from token storage

            var appDevice = new AppDevice()
            {
                ApplicationId = Guid.Empty,
                DeviceUid = Guid.Empty.ToByteArray()
            };

            
            //TODO: need to validate the app is not revoked
            // var appReg = await _appRegistrationService.GetAppRegistration(appDevice.ApplicationId);
            // if (null == appReg || appReg.IsRevoked)
            // {
            //     //TODO: security audit
            //     _logger.LogInformation($"Revoked app attempted validation [{appDevice.ApplicationId}] on device [{appDevice.DeviceUid}]");
            //     return null;
            // }
            //
            // //TODO: need to validate that this deviceUid has not been rejected/blocked
            // var deviceReg = await _appRegistrationService.GetAppDeviceRegistration(appDevice.ApplicationId, appDevice.DeviceUid);
            // if (null == deviceReg || deviceReg.IsRevoked)
            // {
            //     //TODO: security audit
            //     _logger.LogInformation($"Revoked device attempted validation [{appDevice.ApplicationId}] on device [{appDevice.DeviceUid}]");
            //     return null;
            // }
            
            return appDevice;
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