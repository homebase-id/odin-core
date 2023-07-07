using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Membership.Definition;
using Odin.Core.Services.Contacts.Circle.Requests;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Mediator;
using Odin.Core.Storage;
using Odin.Core.Time;
using PermissionSet = Odin.Core.Services.Authorization.Permissions.PermissionSet;

namespace Odin.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Manages the Icr keys
    /// </summary>
    public class IcrKeyService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly CircleNetworkStorage _storage;

        public IcrKeyService(OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage)
        {
            _contextAccessor = contextAccessor;
            _storage = new CircleNetworkStorage(tenantSystemStorage);
        }
        
        /// <summary>
        /// Creates initial encryption keys
        /// </summary>
        public async Task CreateInitialKeys()
        {
            var mk = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            _storage.CreateIcrKey(mk);
            await Task.CompletedTask;
        }

        public SensitiveByteArray? GetIcrKey()
        {
            return this.GetDecryptedIcrKey();
        }

        public SymmetricKeyEncryptedAes ReEncryptIcrKey(SensitiveByteArray encryptionKey)
        {
            var rawIcrKey = GetDecryptedIcrKey();
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(ref encryptionKey, ref rawIcrKey);
            rawIcrKey.Wipe();
            return encryptedIcrKey;
        }
        
        public EncryptedClientAccessToken EncryptClientAccessTokenUsingIrcKey(ClientAccessToken clientAccessToken)
        {
            var rawIcrKey = GetDecryptedIcrKey();
            var k = EncryptedClientAccessToken.Encrypt(rawIcrKey, clientAccessToken);
            rawIcrKey.Wipe();
            return k;
        }
        
        //

        private SensitiveByteArray GetDecryptedIcrKey()
        {
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var masterKeyEncryptedIcrKey = _storage.GetMasterKeyEncryptedIcrKey();
            return masterKeyEncryptedIcrKey.DecryptKeyClone(ref masterKey);
        }
    }
}