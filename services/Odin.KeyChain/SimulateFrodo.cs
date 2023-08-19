using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Util;
using Odin.Keychain;
using static Odin.Keychain.RegisterKeyController;

namespace Odin.KeyChain
{
    public static class SimulateFrodo
    {
        private static SensitiveByteArray _pwd;
        private static EccFullKeyData _ecc;
        private static PunyDomainName _identity;

        static SimulateFrodo()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
            _identity = new PunyDomainName("frodobaggins.me");
        }

        public static void NewKey(string identity = "frodobaggins.me")
        {
            _identity = new PunyDomainName(identity);
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
        }

        public static (SensitiveByteArray, EccFullKeyData, PunyDomainName) GetKey()
        {
            return (_pwd, _ecc, _identity);
        }
        public static void SetKey(SensitiveByteArray pwd, EccFullKeyData ecc, PunyDomainName identity)
        {
            _pwd = pwd;
            _ecc = ecc;
            _identity = identity;
        }

        // This creates a "Key Registration" instruction
        public static SignedEnvelope InstructionEnvelope()
        {
            return InstructionSignedEnvelope.CreateInstructionKeyRegistration(_ecc, _pwd, _identity, null);
        }

        // Todd, This is how Frodo initiates a request for registering his public key
        // with the Odin Key Chain. Ignore the "web api" parameter, that's just a hack.
        public async static Task<ActionResult> InitiateRequestForKeyRegistration(RegisterKeyController webApi)
        {
            // First Frodo generates a smart contract request object
            // This is the function Frodo calls internally to generate a request
            // Here we write all the attributes we want attested

            var signedInstruction = InstructionEnvelope();
            var signedInstructionJson = signedInstruction.GetCompactSortedJson();

            // POST crap
            var registrationModel = new RegistrationBeginModel() { SignedRegistrationInstructionEnvelopeJson = signedInstructionJson };

            // @Todd then you call out over HTTPS to request it
            var r1 = await webApi.PostPublicKeyRegistrationBegin(registrationModel);

            // Check it got received OK
            var objectResult = r1 as ObjectResult;
            if (objectResult == null)
                throw new Exception("No result back");

            int statusCode = objectResult.StatusCode ?? 0;
            if (statusCode != StatusCodes.Status200OK)
                throw new Exception("Unable to begin register request");

            if (objectResult.Value == null)
                throw new Exception("No value result back");

            string? previousHashBase64 = objectResult.Value.ToString();

            if (previousHashBase64 == null)
                throw new Exception("No previousHashBase64");

            //
            // We are Done with the Begin request. Now let's try to finalize it
            // 

            ActionResult r2 = new ObjectResult(null) { StatusCode = StatusCodes.Status500InternalServerError };

            for (int i = 0; i < 10 ; i++)
            {
                var signatureBase64 = SimulateFrodo.SignPreviousHashForPublicKeyChain(previousHashBase64);
                var model = new RegistrationFinalizeModel() { EnvelopeIdBase64 = signedInstruction.Envelope.ContentNonce.ToBase64(), SignedPreviousHashBase64 = signatureBase64 };
                r2 = await webApi.PostPublicKeyRegistrationFinalize(model);

                // Check it got received OK
                objectResult = r2 as ObjectResult;
                if (objectResult == null)
                    throw new Exception("No result back");

                statusCode = objectResult.StatusCode ?? 0;
                if (statusCode == StatusCodes.Status200OK)
                    return r2; // Yay, done.
                else if (statusCode == StatusCodes.Status429TooManyRequests)
                    continue; // try again
                else
                    throw new Exception($"Unexpected HTTP status code: {statusCode}");
            }

            return r2; 
        }


        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public static string GetPublicKey()
        {
            return _ecc.publicDerBase64();
        }


        // Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        //  _ecc would be the identity's signature key
        public static string SignPreviousHashForPublicKeyChain(string previousHashBase64)
        {
            // tempCode was OK, we continue
            var nonce = Convert.FromBase64String(previousHashBase64);

            // Todd need to check this JIC 
            if ((nonce.Length < 16) || (nonce.Length > 32))
                throw new Exception("invalid nonce size");

            // We shouldn't sign an arbitrary incoming value, so agreement here is to prepend constant
            var combinedNonce = ByteArrayUtil.Combine("PublicKeyChain-".ToUtf8ByteArray(), nonce);

            // We sign the nonce with the signature key
            var signature = _ecc.Sign(_pwd, combinedNonce);

            // We return the signed data to the requestor
            return Convert.ToBase64String(signature);
        }

        // Todd Look in the simulator "Simulate..." for triggering the registration
    }


}
