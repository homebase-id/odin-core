using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;

namespace Odin.Hosting.Cli.Commands.ClientTokenRegistrationUpgrade
{
    public class ClientTokenRegistrationConverter(
        ILogger<ClientTokenRegistrationConverter> logger,
        ClientRegistrationStorage clientRegistrationStorage,
        TableKeyThreeValueCached tblKeyThreeValue,
        TableKeyValue tblKeyValue)
    {
        private static readonly Guid AppClientDataTypeId = Guid.Parse("54e60e2f-4687-449c-83ad-6ae6ff4ba1cf");
        private static readonly byte[] AppClientDataType = AppClientDataTypeId.ToByteArray();
        private const string AppClientContextKey = "fb080b07-0566-4db8-bc0d-daed6b50b104";

        private static readonly ThreeKeyValueStorage AppClientValueStorage = TenantSystemStorage
            .CreateThreeKeyValueStorage(Guid.Parse(AppClientContextKey));

        public async Task UpdateAppClientRegistration(OdinId odinId)
        {
            logger.LogDebug("Upgrading app client token storage: [{identity}]", odinId);

            // get the existing tokens directly from the table
            var oldClients = await AppClientValueStorage.GetByCategoryAsync<AppClient>(tblKeyThreeValue, AppClientDataType);
            foreach (var oldClient in oldClients)
            {
                var newAppClient = new AppClientRegistration(oldClient.AppId, oldClient.FriendlyName, oldClient.AccessRegistration);

                logger.LogDebug(
                    "Creating new app client registration: [{identity}] for appId:{app} friendlyName:{name} accessRegId:{access}",
                    odinId,
                    newAppClient.AppId,
                    newAppClient.FriendlyName,
                    newAppClient.AccessRegistration);

                await clientRegistrationStorage.SaveAsync(newAppClient);
            }
        }

        public async Task UpdateOwnerClientRegistration(OdinId odinId)
        {
            logger.LogDebug("Upgrading owner client token storage: [{identity}]", odinId);

            var allRecords = await tblKeyValue.GetAll();
            foreach (var record in allRecords)
            {
                var oldOwnerConsoleToken = OdinSystemSerializer.Deserialize<OwnerConsoleToken>(record.data.ToStringFromUtf8Bytes());

                if (oldOwnerConsoleToken != null)
                {
                    logger.LogDebug("Creating new owner console token: [{identity}] for owner:{owner}", odinId, oldOwnerConsoleToken.Id);

                    var newServerToken = new OwnerConsoleClientRegistration
                    {
                        Id = oldOwnerConsoleToken.Id,
                        SharedSecret = oldOwnerConsoleToken.SharedSecret,
                        ExpiryUnixTime = oldOwnerConsoleToken.ExpiryUnixTime,
                        TokenEncryptedKek = oldOwnerConsoleToken.TokenEncryptedKek,
                        IssuedTo = odinId,
                    };

                    await clientRegistrationStorage.SaveAsync(newServerToken);
                }
            }
        }
    }
}