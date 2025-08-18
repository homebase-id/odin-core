using System.Text;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.Database.Notary;
using Odin.Core.Storage.Database.Notary.Table;
using Odin.Core.Time;

namespace Odin.Notarius
{
    public static class NotaryDatabaseUtil
    {
        /// <summary>
        /// Called once from the controller to make sure database is setup
        /// Need to set drop to false in production
        /// </summary>
        /// <param name="db"></param>
        public static async Task InitializeDatabaseAsync(NotaryDatabase db)
        {
            await db.MigrateDatabaseAsync();

            var r = await db.NotaryChain.GetLastLinkAsync();

            // If the database is empty then we need to create the genesis record
            if (r == null)
            {
                // Genesis ECC key
                // 
                var password = Guid.Empty.ToByteArray().ToSensitiveByteArray();
                var eccGenesis = new EccFullKeyData(password, EccKeySize.P384, 1);

                // Create the genesis block
                //
                var genesis = NewBlockChainRecord();

                genesis.identity = "id.odin.earth"; // or e.g. id.dot.one
                genesis.publicKeyJwkBase64Url = eccGenesis.PublicKeyJwkBase64Url(); // Would be nice with a real public key here from the actual identity
                genesis.previousHash = ByteArrayUtil.CalculateSHA256Hash(Guid.Empty.ToByteArray());
                genesis.signedPreviousHash = eccGenesis.Sign(password, ByteArrayUtil.Combine("Notarize-".ToUtf8ByteArray(), genesis.previousHash));
                genesis.notarySignature = "asdjkhasdjkhasdkhj".ToUtf8ByteArray(); //TODO FIX
                genesis.recordHash = CalculateRecordHash(genesis);
                VerifyBlockChainRecord(genesis, null, false);
                await db.NotaryChain.InsertAsync(genesis);
            }
        }


        public static NotaryChainRecord NewBlockChainRecord()
        {
            var r = new NotaryChainRecord();

            r.timestamp = UnixTimeUtc.Now();
            r.algorithm = EccFullKeyData.eccSignatureAlgorithmNames[(int)EccKeySize.P384];

            return r;
        }

        private static byte[] CombineRecordBytes(NotaryChainRecord record)
        {
            // Combine all columns, except ofc the recordHash, into a single byte array
            return ByteArrayUtil.Combine(record.previousHash,
                                         Encoding.UTF8.GetBytes(record.identity),
                                         ByteArrayUtil.Int64ToBytes(record.timestamp.milliseconds),
                                         record.signedPreviousHash,
                                         record.algorithm.ToUtf8ByteArray(),
                                         record.notarySignature,
                                         record.publicKeyJwkBase64Url.ToUtf8ByteArray());
        }

        /// <summary>
        /// Sets the KeyChainRecrod algorithm to SHA-256 and calculates the SHA-256.
        /// </summary>
        /// <param name="record">Is the new record we want to insert into the chain</param>
        /// <param name="previousHash">is the SHA-256 byte array of the last blockchain entry's hash value</param>
        /// <returns></returns>
        public static byte[] CalculateRecordHash(NotaryChainRecord record)
        {
            record.algorithm = HashUtil.SHA256Algorithm;

            // Compute hash for the combined byte array
            var hash = ByteArrayUtil.CalculateSHA256Hash(CombineRecordBytes(record));

            return hash;
        }

        /// <summary>
        /// Verifies the integrity of the previousHash, the signature and the hash
        /// </summary>
        /// <param name="record"></param>
        /// <param name="previousRowHash"></param>
        /// <returns></returns>
        public static bool VerifyBlockChainRecord(NotaryChainRecord record, NotaryChainRecord? previousRecord, bool checkTimeStamps)
        {
            var publicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(record.publicKeyJwkBase64Url);
            if (publicKey.VerifySignature(ByteArrayUtil.Combine("Notarize-".ToUtf8ByteArray(), record.previousHash), record.signedPreviousHash) == false)
                return false;

            if (previousRecord != null)
            {

                if (ByteArrayUtil.EquiByteArrayCompare(previousRecord.recordHash, record.previousHash) == false)
                    return false;

                var hash = CalculateRecordHash(record);
                if (ByteArrayUtil.EquiByteArrayCompare(hash, record.recordHash) == false)
                    return false;

                // Maybe we shouldn't do this. IDK.
                if (checkTimeStamps && (record.timestamp < previousRecord.timestamp))
                    return false;
            }

            return true;
        }


        // Verifies the entire chain
        public static async Task<bool> VerifyEntireBlockChainAsync(NotaryDatabase db)
        {
            await using var cn = await db.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "SELECT rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash FROM notaryChain ORDER BY rowid ASC;";

            await using var rdr = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow);
            NotaryChainRecord? previousRecord = null;

            while (await rdr.ReadAsync())
            {
                var record = db.NotaryChain.ReadRecordFromReaderAll(rdr);
                if (VerifyBlockChainRecord(record, previousRecord, true) == false)
                    return false;
                previousRecord = record;
            }

            return true;
       }
    }
}
