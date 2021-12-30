using System;
using System.Collections.Generic;
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
    public class AppAuthenticationService : IAppAuthenticationService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly ILogger<IAppAuthenticationService> _logger;
        private const string AppAuthSessionCollection = "apptko";

        private readonly Dictionary<Guid, AppAuthAuthorizationCode> _authCodes;

        public AppAuthenticationService(DotYouContext context, ISystemStorage systemStorage, IAppRegistrationService appRegistrationService, ILogger<IAppAuthenticationService> logger)
        {
            _systemStorage = systemStorage;
            _appRegistrationService = appRegistrationService;
            _logger = logger;

            _authCodes = new Dictionary<Guid, AppAuthAuthorizationCode>();
        }

        public async Task<Guid> CreateSessionToken(AppDevice appDevice)
        {
            //TODO: might need check against the owner authentication
            //service to ensure the owner has a valid session.  this is done
            //by the webapi so not sure if its needed 

            var appReg = await _appRegistrationService.GetAppRegistration(appDevice.ApplicationId);
            if (null == appReg || appReg.IsRevoked)
            {
                throw new YouverseSecurityException($"App [{appDevice.ApplicationId}] is revoked or not registered");
            }

            var appDeviceReg = await _appRegistrationService.GetAppDeviceRegistration(appDevice.ApplicationId, appDevice.DeviceUid);
            if (null == appDeviceReg || appDeviceReg.IsRevoked)
            {
                throw new YouverseSecurityException($"Device [{string.Join("-", appDevice.DeviceUid)}] is revoked or not registered");
            }

            //TODO: determine the default length of sessions
            var session = new AppAuthSession(Guid.NewGuid(), appDevice, TimeSpan.FromDays(100));

            var authCode = Guid.NewGuid();

            //TODO: config
            if (!_authCodes.TryAdd(authCode, new AppAuthAuthorizationCode(session, TimeSpan.FromSeconds(15))))
            {
                throw new YouverseSecurityException("Failed to create session token");
            }

            return authCode;
        }

        public async Task<DotYouAuthenticationResult> ExchangeAuthCode(AuthCodeExchangeRequest request)
        {
            if (!_authCodes.Remove(request.AuthCode, out var code) || null == code.Session || code.HasExpired())
            {
                throw new YouverseSecurityException($"Invalid authcode during exchange: {request.AuthCode}");
            }

            if (!(code.Session.AppDevice.ApplicationId == request.AppDevice.ApplicationId && ByteArrayUtil.EquiByteArrayCompare(code.Session.AppDevice.DeviceUid, request.AppDevice.DeviceUid)))
            {
                throw new YouverseSecurityException($"Invalid authcode during exchange: {request.AuthCode}");
            }

            //Note: we do not store the client 1/2 kek in the auth session
            this._systemStorage.WithTenantSystemStorage<AppAuthSession>(AppAuthSessionCollection, s => s.Save(code.Session));

            //TODO: read the client half kek and return with auth result.
            return new DotYouAuthenticationResult()
            {
                SessionToken = code.Session.Id,
                ClientHalfKek = new SecureKey(Guid.Empty.ToByteArray())
            };
        }

        public async Task<SessionValidationResult> ValidateSessionToken(Guid token)
        {
            var session = await this._systemStorage.WithTenantSystemStorageReturnSingle<AppAuthSession>(AppAuthSessionCollection, s => s.Get(token));

            if (null == session || session.HasExpired())
            {
                return new SessionValidationResult() {IsValid = false, AppDevice = null};
            }

            var appDevice = session.AppDevice;

            var appReg = await _appRegistrationService.GetAppRegistration(appDevice.ApplicationId);
            if (null == appReg || appReg.IsRevoked)
            {
                _logger.LogInformation($"Revoked app attempted validation [{appDevice.ApplicationId}] on device [{appDevice.DeviceUid}]");
                return new SessionValidationResult() {IsValid = false, AppDevice = null};
            }

            var deviceReg = await _appRegistrationService.GetAppDeviceRegistration(appDevice.ApplicationId, appDevice.DeviceUid);
            if (null == deviceReg || deviceReg.IsRevoked)
            {
                //TODO: security audit
                _logger.LogInformation($"Revoked device attempted validation [{appDevice.ApplicationId}] on device [{appDevice.DeviceUid}]");
                return new SessionValidationResult() {IsValid = false, AppDevice = null};
            }

            return new SessionValidationResult
            {
                IsValid = true,
                AppDevice = appDevice
            };
        }

        public Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            throw new NotImplementedException("");
        }

        public void ExpireSession(Guid token)
        {
            this._systemStorage.WithTenantSystemStorage<AppAuthSession>(AppAuthSessionCollection, s => s.Delete(token));
        }

        private class AppAuthAuthorizationCode
        {
            public UInt64 CreatedAt { get; }
            public UInt64 ExpiresAt { get; }

            public AppAuthSession Session { get; }

            public AppAuthAuthorizationCode(AppAuthSession session, TimeSpan lifetime)
            {
                this.Session = session;
                CreatedAt = DateTimeExtensions.UnixTimeMilliseconds();
                ExpiresAt = CreatedAt + (UInt64) lifetime.TotalMilliseconds;
            }

            public bool HasExpired() => DateTimeExtensions.UnixTimeMilliseconds() > ExpiresAt;
        }
    }
}