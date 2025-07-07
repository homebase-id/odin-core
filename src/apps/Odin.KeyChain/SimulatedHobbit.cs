using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Util;
using Odin.KeyChain.Controllers;
using static Odin.KeyChain.Controllers.RegisterKeyController;

namespace Odin.KeyChain
{
    public static class HobbitSimulator
    {
        private static object _lock = new object();
        private static Dictionary<string, SimulatedHobbit> _hobbits = new Dictionary<string, SimulatedHobbit>();

        public static SimulatedHobbit GetSimulatedHobbit(AsciiDomainName hobbit)
        {
            lock (_lock)
            {
                _hobbits.TryGetValue(hobbit.DomainName, out var simulatedHobbit);

                if (simulatedHobbit == null)
                {
                    simulatedHobbit = new SimulatedHobbit(hobbit);
                    _hobbits[hobbit.DomainName] = simulatedHobbit;
                }

                return simulatedHobbit;
            }
        }
    }

    public class SimulatedHobbit
    {
        private SensitiveByteArray _pwd;
        private EccFullKeyData _ecc;
        private AsciiDomainName _identity;


        public SimulatedHobbit(AsciiDomainName hobbit)
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, EccKeySize.P384, 1);
            _identity = hobbit;
        }

        public (SensitiveByteArray, EccFullKeyData) GetKey()
        {
            return (_pwd, _ecc);
        }

        public void OverwriteKey(SensitiveByteArray pwd, EccFullKeyData ecc)
        {
            _pwd = pwd;
            _ecc = ecc;
        }

        // This creates a "Key Registration" instruction
        public SignedEnvelope InstructionEnvelope()
        {
            return InstructionSignedEnvelope.CreateInstructionKeyRegistration(_ecc, _pwd, _identity, null);
        }

        // Todd, This is how Frodo initiates a request for registering his public key
        // with the Odin Key Chain. Ignore the "web api" parameter, that's just a hack.
        public async Task<ActionResult> InitiateRequestForKeyRegistration(RegisterKeyController webApi)
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
                return r1;

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

                var signatureBase64 = SignPreviousHashForPublicKeyChain(previousHashBase64);
                var model = new RegistrationCompleteModel() { EnvelopeIdBase64 = signedInstruction.Envelope.ContentNonce.ToBase64(), SignedPreviousHashBase64 = signatureBase64 };
                r2 = await webApi.PostPublicKeyRegistrationComplete(model);

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
        public string GetPublicKey()
        {
            return _ecc.PublicKeyJwkBase64Url();
        }


        // Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        //  _ecc would be the identity's signature key
        public string SignPreviousHashForPublicKeyChain(string previousHashBase64)
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
