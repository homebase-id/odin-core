using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage.Sqlite.ServerDatabase;

namespace Youverse.Core.Services.Certificate.Renewal
{
    /// <summary>
    /// List of identities with an outstanding order to have an SSL certificate created 
    /// </summary>
    public class PendingCertificateOrderListService
    {
        private readonly ServerSystemStorage _serverSystemStorage;

        public PendingCertificateOrderListService(ServerSystemStorage serverSystemStorage)
        {
            _serverSystemStorage = serverSystemStorage;
        }

        public void Add(OdinId identity)
        {
            try
            {
                _serverSystemStorage.tblCron.Insert(new CronItem()
                {
                    identityId = identity,
                    type = 2,
                    data = identity.DomainName.ToLower().ToUtf8ByteArray()
                });
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                //ignore constraint error code as it just means we tried to insert the sender twice.
                //it's only needed once
                if (ex.ErrorCode != 19) //constraint
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Gets a list of identities awaiting certificate validation/creation.
        /// </summary>
        public async Task<(IEnumerable<OdinId>, Guid marker)> GetIdentities()
        {
            var records = _serverSystemStorage.tblCron.Pop(1, out var marker);

            //see Add method.  fileId = odinId
            var senders = records.Select(item => new OdinId(item.data.ToStringFromUtf8Bytes())).ToList();
            var result = (senders, marker);
            return await Task.FromResult(result);
        }

        public void MarkComplete(Guid marker)
        {
            _serverSystemStorage.tblCron.PopCommitList(new List<Guid>() { marker });
        }

        public void MarkFailure(Guid marker)
        {
            _serverSystemStorage.tblCron.PopCancelList(new List<Guid>() { marker });
        }
    }
}