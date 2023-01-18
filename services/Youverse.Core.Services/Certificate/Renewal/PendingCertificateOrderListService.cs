using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Identity;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Certificate.Renewal
{
    /// <summary>
    /// List of identities with an outstanding order to have an SSL certificate created 
    /// </summary>
    public class PendingCertificateOrderListService:IDisposable
    {
        private readonly TableOutbox _table;
        private readonly KeyValueDatabase _db;
        private object _hack = new object();

        public PendingCertificateOrderListService(string dataPath)
        {
            Guard.Argument(dataPath, nameof(dataPath)).NotNull().NotEmpty();
            var finalPath = PathUtil.OsIfy(dataPath);

            if (!Directory.Exists(finalPath))
            {
                Directory.CreateDirectory(finalPath!);
            }

            var filePath = PathUtil.OsIfy($"{dataPath}\\cert.db");
            _db = new KeyValueDatabase($"URI=file:{filePath}");
            _db.CreateDatabase(false);

            // TODO: NOT ALLOWED. THIS WILL MESS UP SOMEHOW.
            _table = new TableOutbox(_db, _hack);
        }

        public void Add(DotYouIdentity identity)
        {
            //Note: I use sender here because boxId has a unique constraint; and we only a sender in this table once.
            //I swallow the exception because there's no direct way to see if a record exists for this sender already
            // byte[] fileId = new byte[] { 1, 1, 2, 3, 5 };

            byte[] boxId = GuidId.FromString("pcol").Value.ToByteArray();
            byte[] fileId = identity.ToGuidIdentifier().ToByteArray();
            try
            {
                _table.InsertRow(boxId, fileId, 0, identity.Id.ToLower().ToUtf8ByteArray());
            }
            catch (System.Data.SQLite.SQLiteException ex)
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
        public async Task<(IEnumerable<DotYouIdentity>, byte[] marker)> GetIdentities()
        {
            var records = _table.PopAll(out var marker);
            
            //see Add method.  fileId = dotYouId
            var senders = records.Select(item => new DotYouIdentity(item.value.ToStringFromUtf8Bytes())).ToList();
            return (senders, marker);
        }

        public void MarkComplete(byte[] marker)
        {
            _table.PopCommit(marker);
        }

        public void MarkFailure(byte[] marker)
        {
            _table.PopCancel(marker);
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}