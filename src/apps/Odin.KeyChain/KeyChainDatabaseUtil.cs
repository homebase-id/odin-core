using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.Core.Time;
using Odin.Core;
using System.Text;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Storage.SQLite;

namespace Odin.KeyChain
{
    public static class KeyChainDatabaseUtil
    {
        /// <summary>
        /// Called once from the controller to make sure database is setup
        /// Need to set drop to false in production
        /// </summary>
        /// <param name="_db"></param>
        public static async Task InitializeDatabaseAsync(KeyChainDatabase _db, DatabaseConnection conn)
        {
            await _db.CreateDatabaseAsync(dropExistingTables: true); // Remove "true" for production

            var r = await _db.tblKeyChain.GetLastLinkAsync(conn);

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
                var signature = eccGenesis.Sign(password, ByteArrayUtil.Combine("PublicKeyChain-".ToUtf8ByteArray(), genesis.previousHash));
                genesis.signedPreviousHash = signature;
                genesis.recordHash = CalculateRecordHash(genesis);
                VerifyBlockChainRecord(genesis, null, false);
                await _db.tblKeyChain.InsertAsync(conn, genesis);
            }
        }

        public static KeyChainRecord NewBlockChainRecord()
        {
            var r = new KeyChainRecord();

            r.timestamp = UnixTimeUtc.Now();
            r.algorithm = EccFullKeyData.eccSignatureAlgorithmNames[(int) EccKeySize.P384];

            return r;
        }

        private static byte[] CombineRecordBytes(KeyChainRecord record)
        {
            // Combine all columns, except ofc the recordHash, into a single byte array
            return ByteArrayUtil.Combine(record.previousHash,
                                         Encoding.UTF8.GetBytes(record.identity),
                                         ByteArrayUtil.Int64ToBytes(record.timestamp.milliseconds),
                                         record.signedPreviousHash,
                                         record.algorithm.ToUtf8ByteArray(),
                                         record.publicKeyJwkBase64Url.ToUtf8ByteArray());
        }

        /// <summary>
        /// Sets the KeyChainRecrod algorithm to SHA-256 and calculates the SHA-256.
        /// </summary>
        /// <param name="record">Is the new record we want to insert into the chain</param>
        /// <param name="previousHash">is the SHA-256 byte array of the last blockchain entry's hash value</param>
        /// <returns></returns>
        public static byte[] CalculateRecordHash(KeyChainRecord record)
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
        public static bool VerifyBlockChainRecord(KeyChainRecord record, KeyChainRecord? previousRecord, bool checkTimeStamps)
        {
            var publicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(record.publicKeyJwkBase64Url);
            if (publicKey.VerifySignature(ByteArrayUtil.Combine("PublicKeyChain-".ToUtf8ByteArray(), record.previousHash), record.signedPreviousHash) == false)
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
        public static async Task<bool> VerifyEntireBlockChainAsync(KeyChainDatabase db, DatabaseConnection conn)
        {
            using (var sqlcmd = db.CreateCommand())
            {
                sqlcmd.CommandText = "SELECT rowId, previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain ORDER BY rowid ASC;";

                using (var rdr = await conn.ExecuteReaderAsync(sqlcmd, System.Data.CommandBehavior.SingleRow))
                {
                    KeyChainRecord? previousRecord = null;

                    while (await rdr.ReadAsync())
                    {
                        var record = db.tblKeyChain.ReadRecordFromReaderAll(rdr);
                        if (VerifyBlockChainRecord(record, previousRecord, true) == false)
                            return false;
                        previousRecord = record;
                    }
                } // using

                return true;

            }
        }
    }
}
