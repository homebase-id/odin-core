using Odin.Core;
using Odin.Core.Cryptography.Data;

namespace Odin.KeyChain
{
    public static class SimulateFrodo
    {
        private static SensitiveByteArray _pwd;
        private static EccFullKeyData _ecc;

        static SimulateFrodo()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
        }

        public static void GenerateNewKeys()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
        }

        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public static string GetPublicKey()
        {
            return _ecc.publicDerBase64();
        }

        // Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        //  _ecc would be the identity's signature key
        public static string SignNonceForKeyChain(string nonceBase64, string tempCodeBase64)
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
            var signature = _ecc.Sign(_pwd, nonce);

            // We return the signed data to the requestor
            return Convert.ToBase64String(signature);
        }

        // Todd Look in the simulator "Simulate..." for triggering the registration
    }


}
