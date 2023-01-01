using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;

namespace Youverse.Core.Services.Contacts.Circle.Notification
{
    /// <summary>
    /// Notification service across DIs for various changes in data. (i.e. Notify other DIs when my name changes)
    /// </summary>
    public class CircleNetworkNotificationService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ICircleNetworkService _circleNetworkService;

        public CircleNetworkNotificationService(DotYouContextAccessor contextAccessor, IDotYouHttpClientFactory dotYouHttpClientFactory, ICircleNetworkService circleNetworkService)
        {
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _circleNetworkService = circleNetworkService;
        }

        public async Task NotifyConnections(CircleNetworkNotification notification)
        {
            //TODO: need to switch this to a background call; also work in chunks

            var connections = await _circleNetworkService.GetConnectedIdentities(PageOptions.All);

            foreach (var connection in connections.Results)
            {
                var recipient = connection.DotYouId;
                this.SendNotification(recipient, notification);
            }
        }

        public async Task<SendNotificationResult> SendNotification(DotYouIdentity recipient, CircleNetworkNotification notification)
        {
            var payload = await this.Encrypt(recipient, notification);

            var clientAuthToken = await _circleNetworkService.GetConnectionAuthToken(recipient);
            var response = await _dotYouHttpClientFactory.CreateClientUsingAccessToken<ICircleNetworkNotificationClient>(recipient, clientAuthToken).Notify(payload);
            var status = response.IsSuccessStatusCode ? SendNotificationStatus.Delivered : SendNotificationStatus.Failed;
            return new SendNotificationResult()
            {
                Recipient = recipient,
                Status = status
            };
        }
        
        public async Task ReceiveNotification(SharedSecretEncryptedNotification encryptedNotification)
        {
            var sender = _contextAccessor.GetCurrent().Caller.DotYouId;

            var notification = await this.Decrypt(sender, encryptedNotification);

            //route the notification
            switch (notification.TargetSystemApi)
            {
                case SystemApi.CircleNetwork:
                    await _circleNetworkService.HandleNotification(sender, notification);
                    break;

                case SystemApi.Contact:
                case SystemApi.CircleNetworkRequests:
                    throw new NotImplementedException($"System notifications are not support for {Enum.GetName(notification.TargetSystemApi)}");

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task<CircleNetworkNotification> Decrypt(DotYouIdentity sender, SharedSecretEncryptedNotification encryptedNotification)
        {
            var sharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
            
            var jsonBytes = Cryptography.Crypto.AesCbc.Decrypt(encryptedNotification.Data, ref sharedSecret, encryptedNotification.InitializationVector);
            var json = jsonBytes.ToStringFromUtf8Bytes();
            return DotYouSystemSerializer.Deserialize<CircleNetworkNotification>(json);
        }

        private async Task<SharedSecretEncryptedNotification> Encrypt(DotYouIdentity recipient, object notification)
        {
            var identityReg = await _circleNetworkService.GetIdentityConnectionRegistration(recipient);
            var sharedSecret = identityReg.ClientAccessTokenSharedSecret.ToSensitiveByteArray();

            var json = DotYouSystemSerializer.Serialize(notification);
            var iv = ByteArrayUtil.GetRndByteArray(16);

            var encryptedData = Cryptography.Crypto.AesCbc.Encrypt(
                data: json.ToUtf8ByteArray(),
                key: ref sharedSecret,
                iv: iv);

            SharedSecretEncryptedNotification payload = new SharedSecretEncryptedNotification()
            {
                InitializationVector = iv,
                Data = encryptedData
            };

            return payload;
        }
    }
}