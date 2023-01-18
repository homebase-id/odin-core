﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using LazyCache;
using LazyCache.Providers;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.Ocsp;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Storage;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly ExchangeGrantService _exchangeGrantService;

        private readonly GuidId _appRegistrationDataType = GuidId.FromString("__app_reg");
        private readonly ThreeKeyValueStorage _appRegistrationValueStorage;

        private readonly GuidId _appClientDataType = GuidId.FromString("__app_client_reg");
        private readonly ThreeKeyValueStorage _appClientValueStorage;

        private readonly DotYouContextCache _cache;
        private readonly TenantContext _tenantContext;

        private readonly CircleDefinitionService _circleDefinitionService;


        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ITenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, YouverseConfiguration config, TenantContext tenantContext, CircleDefinitionService circleDefinitionService)
        {
            _contextAccessor = contextAccessor;
            _tenantSystemStorage = tenantSystemStorage;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _circleDefinitionService = circleDefinitionService;

            _appRegistrationValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _appClientValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _cache = new DotYouContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        public async Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();
            Guard.Argument(request.AppId, nameof(request.AppId)).Require(request.AppId != Guid.Empty);

            if (request.AuthorizedCircles?.Any() ?? false)
            {
                Guard.Argument(request.AuthorizedCircles, nameof(request.AuthorizedCircles)).Require(circles => { return circles.All(cid => _circleDefinitionService.IsEnabled(cid)); });
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grant = await _exchangeGrantService.CreateExchangeGrant(request.PermissionSet, request.Drives, masterKey);
            var circleMemberGrant = await _exchangeGrantService.CreateExchangeGrant(request.CircleMemberPermissionSet, request.CircleMemberDrives, masterKey);
            
            var appReg = new AppRegistration()
            {
                AppId = request.AppId,
                Name = request.Name,
                Grant = grant,
                CircleMemberGrant = circleMemberGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            _appRegistrationValueStorage.Upsert(appReg.AppId, GuidId.Empty, _appRegistrationDataType, appReg);
            return appReg.Redacted();
        }

        public async Task UpdateAppPermissions(GuidId appId, PermissionSet permissions, IEnumerable<DriveGrantRequest> drives)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null == appReg)
            {
                throw new YouverseClientException("Invalid AppId", YouverseClientErrorCode.AppNotRegistered);
            }

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grant = await _exchangeGrantService.CreateExchangeGrant(permissions, drives, masterKey);
            appReg.Grant = grant;

            _appRegistrationValueStorage.Upsert(appId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetPermissionContextCache();
        }

        public async Task UpdateAppAuthorizedCircles(GuidId appId, List<Guid> authorizedCircles, PermissionSet permissions, IEnumerable<DriveGrantRequest> drives)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            /*
                michael 2 months ago
                @todd
                the "on/off", when "on", it means "apply this app's security vector" to the circle in question. Over 
                time we'll of course have more than one app. It means on the backend, with the app registration, you'll 
                need to have saved the App's security vector for circles. 
                
                And when you calculate the permission for a circle
                - you'll need to OR together all the security vectors for all the Apps for that circle.
                - And then OR that vector together with other permissions for the circle.
             */

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null == appReg)
            {
                throw new YouverseClientException("Invalid AppId", YouverseClientErrorCode.AppNotRegistered);
            }

            //TODO: examine if the circles changed - update exchange grants
            bool circlesHaveChanged = false;

            if (circlesHaveChanged)
            {
                //TODO: how to apply the permissions to all users with-in the circles
            }

            appReg.AuthorizedCircles = authorizedCircles;
            _appRegistrationValueStorage.Upsert(appId, GuidId.Empty, _appRegistrationDataType, appReg);
            ResetPermissionContextCache();
        }

        public async Task<AppClientRegistrationResponse> RegisterClient(GuidId appId, byte[] clientPublicKey, string friendlyName)
        {
            Guard.Argument(appId, nameof(appId)).Require(x => x != Guid.Empty);
            Guard.Argument(clientPublicKey, nameof(clientPublicKey)).NotNull().Require(x => x.Length > 200);
            Guard.Argument(friendlyName, nameof(friendlyName)).NotNull().NotEmpty();

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (appReg == null)
            {
                throw new YouverseClientException("App must be registered to add a client", YouverseClientErrorCode.AppNotRegistered);
            }

            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, _contextAccessor.GetCurrent().Caller.GetMasterKey(), ClientTokenType.Other);

            var appClient = new AppClient(appId, accessRegistration);
            _appClientValueStorage.Upsert(accessRegistration.Id, appReg.AppId, _appClientDataType, appClient);

            //RSA encrypt using the public key and send to client
            var tokenBytes = cat.ToAuthenticationToken().ToPortableBytes();
            var sharedSecret = cat.SharedSecret.GetKey();

            var data = ByteArrayUtil.Combine(tokenBytes, sharedSecret);
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(clientPublicKey);
            var encryptedData = publicKey.Encrypt(data);

            data.WriteZeros();

            return new AppClientRegistrationResponse()
            {
                EncryptionVersion = 1,
                Token = cat.Id,
                Data = encryptedData
            };
        }

        public async Task<RedactedAppRegistration> GetAppRegistration(GuidId appId)
        {
            var result = await GetAppRegistrationInternal(appId);
            return result?.Redacted();
        }

        public async Task<DotYouContext> GetPermissionContext(ClientAuthenticationToken token)
        {
            var creator = new Func<Task<DotYouContext>>(async delegate
            {
                var (isValid, accessReg, appReg) = this.ValidateClientAuthToken(token).GetAwaiter().GetResult();

                if (!isValid)
                {
                    throw new YouverseSecurityException("Invalid token");
                }

                var grantDictionary = new Dictionary<string, ExchangeGrant>
                {
                    { "app_exchange_grant", appReg.Grant }
                };

                //Note: isOwner = true because we passed ValidateClientAuthToken for an ap token above 
                var permissionContext = _exchangeGrantService.CreatePermissionContext(token,
                    grantDictionary,
                    accessReg,
                    includeAnonymousDrives: true).GetAwaiter().GetResult();

                var dotYouContext = new DotYouContext()
                {
                    Caller = new CallerContext(
                        dotYouId: _tenantContext.HostDotYouId,
                        masterKey: null,
                        securityLevel: SecurityGroupType.Owner)
                };

                dotYouContext.SetPermissionContext(permissionContext);
                return dotYouContext;
            });

            var result = await _cache.GetOrAddContext(token, creator);
            return result;
        }

        public async Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(ClientAuthenticationToken authToken)
        {
            var appClient = _appClientValueStorage.Get<AppClient>(authToken.Id);
            if (null == appClient)
            {
                return (false, null, null);
            }

            var appReg = await this.GetAppRegistrationInternal(appClient.AppId);

            if (null == appReg || null == appReg.Grant)
            {
                return (false, null, null);
            }

            if (appClient.AccessRegistration.IsRevoked || appReg.Grant.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, appClient.AccessRegistration, appReg);
        }

        public async Task RevokeApp(GuidId appId)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = true;
            }

            //TODO: revoke all clients? or is the one flag enough?
            _appRegistrationValueStorage.Upsert(appId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetPermissionContextCache();
        }

        /// <summary>
        /// Empties the cache and creates a new instance that can be built
        /// </summary>
        private void ResetPermissionContextCache()
        {
            _cache.Reset();
        }

        public async Task RemoveAppRevocation(GuidId appId)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            _appRegistrationValueStorage.Upsert(appId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetPermissionContextCache();
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients()
        {
            var list = _tenantSystemStorage.ThreeKeyValueStorage.GetByKey3<AccessRegistration>(_appClientDataType);
            var resp = list.Select(accessReg => new RegisteredAppClientResponse()
            {
                IsRevoked = accessReg.IsRevoked,
                Created = accessReg.Created,
                AccessRegistrationClientType = accessReg.AccessRegistrationClientType
            }).ToList();

            return resp;
        }

        public Task<List<RedactedAppRegistration>> GetRegisteredApps()
        {
            var apps = _appRegistrationValueStorage.GetByKey3<AppRegistration>(_appRegistrationDataType);
            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return Task.FromResult(redactedList);
        }

        private async Task<AppRegistration> GetAppRegistrationInternal(GuidId appId)
        {
            var appReg = _appRegistrationValueStorage.Get<AppRegistration>(appId);
            return appReg;
        }
    }
}