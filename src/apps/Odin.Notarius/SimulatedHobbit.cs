using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using System.Text.Json;
using static Odin.Notarius.NotarizeController;

namespace Odin.Notarius
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
        public static SimulatedHobbit GetSimulatedHobbit(string hobbit)
        {
            return GetSimulatedHobbit(new AsciiDomainName(hobbit));
        }
    }


    public class SimulatedHobbit
    {
        public SensitiveByteArray _pwd;
        public EccFullKeyData _ecc;
        public AsciiDomainName _identity;
        public string DomainName { get { return _identity.DomainName; } }

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

        public void NewKey(string identity = "frodobaggins.me")
        {
            _identity = new AsciiDomainName(identity);
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, EccKeySize.P384, 1);
        }

        // This creates a "Key Registration" instruction
        public static EnvelopeData DocumentEnvelope()
        {
            // Let's say we have a document (possibly a file)
            // We want some additional information in the envelope
            var additionalInfo = new SortedDictionary<string, object>
            {
                // { "identity", identity.DomainName },
                { "filename", "legal_contractv11.docx" },
                { "somedata", UnixTimeUtc.Now().seconds },
                { "otherdata", UnixTimeUtc.Now().AddSeconds(3600*24*14).seconds }
            };

            // Make sure it's valid
            if (EnvelopeData.VerifyAdditionalInfoTypes(additionalInfo) == false)
                throw new Exception("Invalid additional data");

            string doc = EnvelopeData.StringifyData(additionalInfo); // The document content is the whole of additionalInfo
            byte[] content = doc.ToUtf8ByteArray(); // This would really more likely be a file

            // Create an Envelope for this document
            var envelope = new EnvelopeData(EnvelopeData.EnvelopeTypeDocument, "test");
            envelope.SetAdditionalInfo(additionalInfo);
            envelope.CalculateContentHash(content);

            // TODO: Envelope.Verify()

            return envelope;
        }


        // This creates a "Key Registration" instruction
        public static SignedEnvelope DocumentSignedEnvelope(EnvelopeData envelope, List<SimulatedHobbit> signatories)
        {
            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            foreach (var hobbit in signatories)
            {
                //  Now let's sign the envelope.
                signedEnvelope.CreateEnvelopeSignature(new OdinId(hobbit.DomainName), hobbit._pwd, hobbit._ecc);

                // string s = signedEnvelope.GetCompactSortedJson(); // For michael to look at the JSON
            }

            return signedEnvelope;
        }

        // This creates a "Key Registration" instruction
        public static SignedEnvelope DocumentSignedEnvelope(List<SimulatedHobbit> signatories)
        {
            var envelope = DocumentEnvelope();
            return DocumentSignedEnvelope(envelope, signatories);
        }

        // Todd, This is how Frodo initiates a request for registering his public key
        // with the Odin Key Chain. Ignore the "web api" parameter, that's just a hack.
        public async Task<ActionResult> InitiateRequestNotary(NotarizeController webApi)
        {
            // First Frodo generates a smart contract request object
            // This is the function Frodo calls internally to generate a request
            // Here we write all the attributes we want attested

            var envelope = DocumentEnvelope();
            var signedInstruction = DocumentSignedEnvelope(envelope, new List<SimulatedHobbit> { HobbitSimulator.GetSimulatedHobbit("frodobaggins.me") });
            var signedInstructionJson = signedInstruction.GetCompactSortedJson();

            // POST crap
            var registrationModel = new NotarizeBeginModel() { SignedEnvelopeJson = signedInstructionJson, RequestorIdentity = _identity.DomainName };

            // @Todd then you call out over HTTPS to request it
            var r1 = await webApi.PostNotaryRegistrationBegin(registrationModel);

            // Check it got received OK
            var objectResult = r1 as ObjectResult;
            if (objectResult == null)
                throw new Exception("No result back");

            int statusCode = objectResult.StatusCode ?? 0;
            if (statusCode != StatusCodes.Status200OK)
                throw new Exception($"Unable to begin register request: {objectResult.Value?.ToString()}");

            if (objectResult.Value == null)
                throw new Exception("No value result back");

            string? previousHashBase64 = objectResult.Value.ToString();

            if (previousHashBase64 == null)
                throw new Exception("No previousHashBase64");

            //
            // We are Done with the Begin request. Now let's try to finalize it
            // 

            ActionResult r2 = new ObjectResult(null) { StatusCode = StatusCodes.Status500InternalServerError };
            string json = "";

            for (int i = 0; i < 10 ; i++)
            {
                var hobbit = HobbitSimulator.GetSimulatedHobbit(new AsciiDomainName(signedInstruction.Signatures[0].Identity)); // TODO FIGURE OUT WHICH ONE
                var signatureBase64 = hobbit.SignPreviousHashForPublicKeyChainBase64(previousHashBase64);
                var model = new NotarizeCompleteModel() { EnvelopeIdBase64 = signedInstruction.Envelope.ContentNonce.ToBase64(), SignedPreviousHashBase64 = signatureBase64 };
                r2 = await webApi.PostNotaryRegistrationComplete(model);

                // Check it got received OK
                objectResult = r2 as ObjectResult;
                if (objectResult == null)
                    throw new Exception("No result back");

                statusCode = objectResult.StatusCode ?? 0;
                if (statusCode == StatusCodes.Status200OK)
                {
                    if (objectResult.Value == null)
                        throw new Exception("null value");

                    if (objectResult.Value.ToString() == null)
                        throw new Exception("null value 2");

                    json = objectResult.Value.ToString()!;
                    break; // Yay, done.
                }
                else if (statusCode == StatusCodes.Status429TooManyRequests)
                    continue; // try again
                else
                    throw new Exception($"Unexpected HTTP status code: {statusCode} {objectResult.Value?.ToString()}");
            }

            //
            // We're done. All good. Just for fun (test), deserialize and verify the registered notary signature
            //

            ActionResult r3 = new ObjectResult(null) { StatusCode = StatusCodes.Status500InternalServerError };
            var signedEnvelope = JsonSerializer.Deserialize<SignedEnvelope>(json);
            if (signedEnvelope == null)
                throw new Exception("can't deserialize result");

            var r4 = await webApi.GetVerifyNotarizedDocument(signedEnvelope.NotariusPublicus.Signature.ToBase64());

            return r4; 
        }


        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public string GetPublicKey()
        {
            return _ecc.PublicKeyJwkBase64Url();
        }


        // Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        //  _ecc would be the identity's signature key
        public string SignPreviousHashForPublicKeyChainBase64(string previousHashBase64)
        {
            // tempCode was OK, we continue
            var nonce = Convert.FromBase64String(previousHashBase64);

            // Todd need to check this JIC 
            if ((nonce.Length < 16) || (nonce.Length > 32))
                throw new Exception("invalid nonce size");

            // We shouldn't sign an arbitrary incoming value, so agreement here is to prepend constant
            var combinedNonce = ByteArrayUtil.Combine("Notarize-".ToUtf8ByteArray(), nonce);

            // We sign the nonce with the signature key
            var signature = _ecc.Sign(_pwd, combinedNonce);

            // We return the signed data to the requestor
            return Convert.ToBase64String(signature);
        }

        // Todd Look in the simulator "Simulate..." for triggering the registration
    }


}
