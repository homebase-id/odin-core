// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using Microsoft.AspNetCore.Mvc;
using System.Text;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.BlockChainDatabase;
using Odin.Core.Util;
using Odin.Core.Time;
using Microsoft.Data.Sqlite;

namespace OdinsAttestation.Controllers
{
    public static class SimulateFrodo
    {
        private static SensitiveByteArray _frodoPwd;
        private static EccFullKeyData _frodoEcc;

        static SimulateFrodo()
        {
            _frodoPwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _frodoEcc = new EccFullKeyData(_frodoPwd, 1);
        }

        public static void GenerateNewKeys()
        {
            _frodoPwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _frodoEcc = new EccFullKeyData(_frodoPwd, 1);
        }

        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public static string GetPublicKey()
        {
            return _frodoEcc.publicDerBase64();
        }

        // Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        // 
        public static string SignNonce(string nonceBase64, string tempCodeBase64)
        {
            // @Todd First sanity check the tempCode
            var tempCode = Convert.FromBase64String(tempCodeBase64);
            if ((tempCode.Length < 16) || (tempCode.Length > 32))
                throw new Exception("invalid nonce size");

            // @Todd then load the tempCode from the DB
            // var tempCode = identityDb.tblKeyValue.Get(CONST_..._ID);
            // If the tempCode is more than 10 seconds old, fail
            // DELETE the tempCode from the DB
            // identityDb.tblKeyValue.Delete(CONST_..._ID);

            // tempCode was OK, we continue
            var nonce = Convert.FromBase64String(nonceBase64);

            // Todd need to check this JIC 
            if ((nonce.Length < 16) || (nonce.Length > 32))
                throw new Exception("invalid nonce size");

            // We sign the nonce with the signature key
            var signature = _frodoEcc.Sign(_frodoPwd, nonce);

            // We return the signed data to the requestor
            return Convert.ToBase64String(signature);
        }
    }

    [ApiController]
    [Route("[controller]")]
    public class RegisterKeyController : ControllerBase
    {
        private readonly ILogger<RegisterKeyController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BlockChainDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly bool _simulate = true;
        private readonly EccFullKeyData _eccKey;
        private readonly SensitiveByteArray _eccPwd;

        public RegisterKeyController(ILogger<RegisterKeyController> logger, IHttpClientFactory httpClientFactory, BlockChainDatabase db, SensitiveByteArray pwdEcc, EccFullKeyData eccKey)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _db = db;
            _eccKey = eccKey;
            _eccPwd = pwdEcc;
        }


        /// <summary>
        /// Called once from the controller to make sure database is setup
        /// Need to set drop to false in production
        /// </summary>
        /// <param name="_db"></param>
        public static void InitializeDatabase(BlockChainDatabase _db)
        {
            // OBSOLETE

            _db.CreateDatabase(dropExistingTables: true);


            var r = _db.tblBlockChain.GetLastLink();

            // If the database is empty then we need to create the genesis record
            if (r == null)
            {
                // Genesis ECC key
                // 
                var password = Guid.Empty.ToByteArray().ToSensitiveByteArray();
                var eccGenesis = new EccFullKeyData(password, 1);

                // Create the genesis block
                //
                var genesis = NewBlockChainRecord();

                genesis.identity = "id.odin.earth";
                genesis.publicKey = eccGenesis.publicKey;
                genesis.nonce = "May Odin's chain safeguard the identities of the many. Skål!".ToUtf8ByteArray();
                var signature = eccGenesis.Sign(password, genesis.nonce);
                genesis.signedNonce = signature;
                genesis.previousHash = ByteArrayUtil.CalculateSHA256Hash(Guid.Empty.ToByteArray());
                genesis.recordHash = CalculateRecordHash(genesis);
                VerifyBlockChainRecord(genesis, null);
                _db.tblBlockChain.Insert(genesis);
            }
        }

        public static BlockChainRecord NewBlockChainRecord()
        { 
            var r = new BlockChainRecord();

            r.nonce = ByteArrayUtil.GetRndByteArray(32);
            r.timestamp = UnixTimeUtcUnique.Now();
            r.algorithm = EccFullKeyData.eccSignatureAlgorithm;

            return r;
        }

        private static byte[] CombineRecordBytes(BlockChainRecord record)
        {
            // Combine all columns, except ofc the recordHash, into a single byte array
            return ByteArrayUtil.Combine(record.previousHash,
                                         Encoding.UTF8.GetBytes(record.identity),
                                         ByteArrayUtil.Int64ToBytes(record.timestamp.uniqueTime),
                                         record.nonce,
                                         record.signedNonce,
                                         record.algorithm.ToUtf8ByteArray(),
                                         record.publicKey);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="record">Is the new record we want to insert into the chain</param>
        /// <param name="previousHash">is the SHA-256 byte array of the last blockchain entry's hash value</param>
        /// <returns></returns>
        public static byte[] CalculateRecordHash(BlockChainRecord record)
        {
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
        public static bool VerifyBlockChainRecord(BlockChainRecord record, BlockChainRecord? previousRecord)
        {
            var publicKey = EccPublicKeyData.FromDerEncodedPublicKey(record.publicKey);
            if (publicKey.VerifySignature(record.nonce, record.signedNonce) == false)
                return false;

            if (previousRecord != null)
            {
                if (ByteArrayUtil.EquiByteArrayCompare(previousRecord.recordHash, record.previousHash) == false)
                    return false;

                var hash = CalculateRecordHash(record);
                if (ByteArrayUtil.EquiByteArrayCompare(hash, record.recordHash) == false)
                    return false;

                // Maybe we shouldn't do this. IDK.
                if (record.timestamp.uniqueTime < previousRecord.timestamp.uniqueTime)
                    return false;
            }

            return true;
        }


        // Verifies the entire chain
        public static bool VerifyEntireBlockChain(BlockChainDatabase _db)
        {
            var _sqlcmd = _db.CreateCommand();
            _sqlcmd.CommandText = "SELECT previousHash,identity,timestamp,nonce,signedNonce,algorithm,publicKey,recordHash FROM blockChain ORDER BY rowid ASC;";

            using (SqliteDataReader rdr = _db.ExecuteReader(_sqlcmd, System.Data.CommandBehavior.SingleRow))
            {
                BlockChainRecord? previousRecord = null;

                while (rdr.Read())
                {
                    var record = _db.tblBlockChain.ReadRecordFromReaderAll(rdr);
                    if (VerifyBlockChainRecord(record, previousRecord) == false) 
                        return false;
                    previousRecord = record;
                }
            } // using

            return true;

        }

        /// <summary>
        /// This simulates that an identity requests it's signature key to be added to the block chain
        /// </summary>
        /// <returns></returns>
        [HttpGet("Simulator")]
        public async Task<IActionResult> GetSimulator()
        {
            // @Todd first you generate a random temp code
            var tempCode = ByteArrayUtil.GetRndByteArray(32);

            // @Todd then you save it in the IdentityDatabase
            // identityDb.tblKkeyValue.Upsert(CONST_SIGNATURE_TEMPCODE_ID, tempCode);

            // @Todd then you call out over HTTPS to request it
            var r1 = await GetRegister("frodo.baggins.me", Convert.ToBase64String(tempCode));

            // If it's OK 200, then you're done.

            // Do another hacky one for testing
            SimulateFrodo.GenerateNewKeys();
            var r2 = await GetRegister("samwise.gamgee.me", Convert.ToBase64String("some temp code from Sam's server".ToUtf8ByteArray()));

            if (VerifyEntireBlockChain(_db) == false)
            {
                return Problem("Fenris broke the chain");
            }

            return r2;
        }

        [HttpGet("AttestHuman")]
        public IActionResult GetAttestHuman(string identity)
        {
            PunyDomainName id;

            try
            {
                id = new PunyDomainName(identity);
            }
            catch (Exception ex) {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            var attestation = AttestationManagement.AttestHuman(_eccKey, _eccPwd, id);


            return Ok(attestation.GetCompactSortedJson());
        }

        [HttpGet("VerifyKey")]
        public IActionResult GetVerifyKey(string identity, string publicKeyDerBase64)
        {
            try
            {
                var id = new PunyDomainName(identity);
            }
            catch (Exception ex)
            {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            EccPublicKeyData publicKey;

            try
            {
                var publicKeyDerBytes = Convert.FromBase64String(publicKeyDerBase64);
                publicKey = EccPublicKeyData.FromDerEncodedPublicKey(publicKeyDerBytes);
            }
            catch(Exception)
            {
                return BadRequest("Invalid public key");
            }

            var r = _db.tblBlockChain.Get(identity, publicKey.publicKey);
            if (r == null)
            {
                return NotFound("No such identity,key found.");
            }

            var msg = $"{r.timestamp.ToUnixTimeUtc().milliseconds / 1000} key registration";

            return Ok(msg);
        }


        /// <summary>
        /// 010. Client calls Server.RegisterKey(identity, tempcode)
        /// 020. Server calls Client.GetPublicKey() to get signature key
        /// 030. Server calls Client.SignNonce(tempcode, previousHash)
        /// 033. Client returns signedNonce
        /// 037. Server verifies signature
        /// 040. Server serializes each request from hereon in semaphore
        /// 050. Server fetches last row
        /// 060. Server calculates new block chain row
        /// 070. Server verifies row data (hash and maybe timestamp)
        /// 080. Server writes row
        /// 090. Server frees semaphore
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="tempCode"></param>
        /// <returns></returns>
        [HttpGet("Register")]
        public async Task<IActionResult> GetRegister(string identity, string tempCode)
        {
            PunyDomainName domain;
            try
            {
                domain = new PunyDomainName(identity);
            }
            catch (Exception)
            {
                return BadRequest("Invalid identity, not a proper puny-code domain name");
            }

            if ((tempCode.Length < 16) || (tempCode.Length > 64))
            {
                return BadRequest("Invalid temp-code, needs to be [16..64] characters");
            }


            var newRecordToInsert = NewBlockChainRecord();

            newRecordToInsert.identity = identity;

            // First be sure we can get the caller's public key so we
            // don't block the semaphore needlessly
            //
            var _httpClient = _httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri("https://api." + domain.DomainName);

            EccPublicKeyData publicKey;

            try
            {
                // 020. Get the public ECC key for signing
                // /api/v1/PublicKey/SignatureValidation
                string publicKeyBase64;

                if (_simulate)
                {
                    publicKeyBase64 = SimulateFrodo.GetPublicKey();
                }
                else
                {
                    var response = await _httpClient.GetAsync("/api/v1/PublicKey/SignatureValidation");
                    publicKeyBase64 = await response.Content.ReadAsStringAsync();
                }
                var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
                publicKey = EccPublicKeyData.FromDerEncodedPublicKey(publicKeyBytes);
            }
            catch (Exception e)
            {
                return BadRequest($"Getting the public key of [api.{domain.DomainName}] failed: {e.Message}");
            }

            // 1a. Got a request to register a key, get the previous hash and get it signed
            //     We need to lock here, so that each request is serialized

            newRecordToInsert.publicKey = publicKey.publicKey; // DER encoded


            // 030. Sign nonce

            newRecordToInsert.nonce = ByteArrayUtil.GetRndByteArray(32);

            try
            {
                string signedNonceBase64;

                if (_simulate)
                {
                    signedNonceBase64 = SimulateFrodo.SignNonce(newRecordToInsert.nonce.ToBase64(), tempCode);
                }
                else
                {
                    var response = await _httpClient.GetAsync("/api/v1/PublicKey/SignatureValidation");
                    signedNonceBase64 = await response.Content.ReadAsStringAsync();
                }

                newRecordToInsert.signedNonce = Convert.FromBase64String(signedNonceBase64);

                // 037
                if (publicKey.VerifySignature(newRecordToInsert.nonce, newRecordToInsert.signedNonce) == false)
                {
                    return BadRequest("Signature invalid.");
                }
            }
            catch (Exception e)
            {
                return BadRequest($"Error getting the signed nonce of [api.{domain.DomainName}] failed: {e.Message}");
            }

            // 040 semaphore
            await _semaphore.WaitAsync();


            try
            {
                // 50 Retrieve the previous row and it's hash
                BlockChainRecord previousRowRecord;

                try
                {
                    previousRowRecord = _db.tblBlockChain.GetLastLink();
                    if (previousRowRecord == null)
                        return Problem("Database is broken");
                }
                catch (Exception)
                {
                    return Problem("Database is broken");
                }

                newRecordToInsert.previousHash = previousRowRecord.recordHash;

                // 060 calculate new hash
                newRecordToInsert.recordHash = CalculateRecordHash(newRecordToInsert);

                // 070 verify record
                if (VerifyBlockChainRecord(newRecordToInsert, previousRowRecord) == false)
                {
                    return Problem("Cannot verify");
                }

                // 080 write row
                try
                {
                    _db.tblBlockChain.Insert(newRecordToInsert);
                }
                catch (Exception e)
                {
                    return Problem($"Did you try to register a duplicate? {e.Message}");
                }
                _db.Commit(); // Flush immediately
            }
            finally
            {
                // 090 free semaphore
                _semaphore.Release();
            }

            return Ok("OK");
        }

    }
}