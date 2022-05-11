using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Notification
{
    /// <summary>
    /// Notification service across DIs for various changes in data. (i.e. Notify other DIs when my name changes)
    /// </summary>
    public class TransDiNotificationService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;

        public TransDiNotificationService(DotYouContextAccessor contextAccessor, IDotYouHttpClientFactory dotYouHttpClientFactory)
        {
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
        }


        public async Task<SendNotificationResult> SendNotification(DotYouIdentity recipient)
        {
            var results = await this.SendNotification(new List<DotYouIdentity>() {recipient});
            return results.FirstOrDefault();
        }

        public async Task<List<SendNotificationResult>> SendNotification(IEnumerable<DotYouIdentity> recipients)
        {
            SharedSecretEncryptedNotification payload = new SharedSecretEncryptedNotification()
            {
                InitializationVector = ByteArrayUtil.GetRndByteArray(16),
                Data = null
            };

            var results = new List<SendNotificationResult>();

            foreach (var recipient in recipients)
            {
                var response = await _dotYouHttpClientFactory.CreateClient<ITransDiNotificationClient>(recipient).Notify(payload);

                var status = response.IsSuccessStatusCode ? SendNotificationStatus.Delivered : SendNotificationStatus.Failed;
                results.Add(new SendNotificationResult() {Recipient = recipient, Status = status});
            }

            return results;
        }
    }
}