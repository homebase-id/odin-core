using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Time;
using Odin.Core.Util;
using OdinsChains.Controllers;
using System.Security.Principal;

namespace Odin.KeyChain
{
    public static class SimulateFrodo
    {
        private static SensitiveByteArray _pwd;
        private static EccFullKeyData _ecc;
        private static PunyDomainName _identity;
        private static Dictionary<string, Int64> _database;

        static SimulateFrodo()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
            _identity = new PunyDomainName("frodobaggins.me");
            _database = new Dictionary<string, Int64> { };
        }

        private static void SaveLocally(string nonceBase64)
        {
            // Save nonce in Frodo's database, the nonce could easily be the key
            // Dont think we need to store the whole signed doc
            // Todd this would easily go into the key-two-value database
            _database.Add(nonceBase64, UnixTimeUtc.Now().seconds);
        }

        private static bool LoadLocally(string nonceBase64)
        {
            // Save nonce in Frodo's database, the nonce could easily be the key
            // Dont think we need to store the whole signed doc
            // Todd this would easily go into the key-two-value database
            if (!_database.ContainsKey(nonceBase64))
                return false;

            var r = _database[nonceBase64];

            if (UnixTimeUtc.Now().seconds - r > 60)
                return false;

            _database.Remove(nonceBase64);

            return true;
        }


        // This creates a "Key Registration" instruction
        private static SignedEnvelope InstructionEnvelope()
        {
            return InstructionSignedEnvelope.CreateInstructionKeyRegistration(_ecc, _pwd, _identity, null);
        }

        // Todd, This is how Frodo initiates a request for registering his public key
        // with the Odin Key Chain. Ignore the "web api" parameter, that's just a hack.
        public async static Task<IActionResult> InitiateRequestForKeyRegistration(RegisterKeyController webApi)
        {
            // First Frodo generates a smart contract request object
            // This is the function Frodo calls internally to generate a request
            // Here we write all the attributes we want attested

            var signedInstruction = InstructionEnvelope();
            var signedInstructionJson = signedInstruction.GetCompactSortedJson();

            SaveLocally(signedInstruction.Envelope.ContentNonce.ToBase64());

            // @Todd then you save it in the IdentityDatabase
            // identityDb.tblKkeyValue.Upsert(CONST_SIGNATURE_TEMPCODE_ID, tempCode);

            // @Todd then you call out over HTTPS to request it
            var r1 = await webApi.GetRegister("frodo.baggins.me", signedInstructionJson);

            // Check it got received OK
            var objectResult = r1 as ObjectResult;
            if (objectResult != null)
            {
                int statusCode = objectResult.StatusCode ?? 0;
                if (statusCode != StatusCodes.Status200OK)
                    throw new Exception("Unable to register request");
            }


            // Frodo is Done sending his request

            return r1;
        }


        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public static string GetPublicKey()
        {
            return _ecc.publicDerBase64();
        }


        // Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        //  _ecc would be the identity's signature key
        public static string SignNonceForKeyChain(string nonceToSignBase64, string contentNonceFromEnvelope)
        {
            // @Todd First sanity check the tempCode
            var tempCode = Convert.FromBase64String(contentNonceFromEnvelope);
            if ((tempCode.Length < 16) || (tempCode.Length > 32))
                throw new Exception("invalid envelope nonce size");

            // @Todd then load the tempCode from the DB
            // var tempCode = identityDb.tblKeyValue.Get(CONST_..._ID);
            // If the tempCode is more than 10 seconds old, fail
            // DELETE the tempCode from the DB
            // identityDb.tblKeyValue.Delete(CONST_..._ID);
            if (!LoadLocally(contentNonceFromEnvelope))
                throw new Exception($"No such envelope nonce request made");

            // tempCode was OK, we continue
            var nonce = Convert.FromBase64String(nonceToSignBase64);

            // Todd need to check this JIC 
            if ((nonce.Length < 16) || (nonce.Length > 32))
                throw new Exception("invalid nonce size");

            // We sign the nonce with the signature key
            var signature = _ecc.Sign(_pwd, nonce);

            // We return the signed data to the requestor
            return Convert.ToBase64String(signature);
        }

        // Todd Look in the simulator "Simulate..." for triggering the registration
    }


}
