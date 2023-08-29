using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Keychain;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using static Odin.Keychain.NotarizeController;

namespace Odin.KeyChain
{
    public static class SimulateFrodo
    {
        private static SensitiveByteArray _pwd;
        private static EccFullKeyData _ecc;
        private static AsciiDomainName _identity;

        static SimulateFrodo()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
            _identity = new AsciiDomainName("frodobaggins.me");
        }

        public static void NewKey(string identity = "frodobaggins.me")
        {
            _identity = new AsciiDomainName(identity);
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
        }

        public static (SensitiveByteArray, EccFullKeyData, AsciiDomainName) GetKey()
        {
            return (_pwd, _ecc, _identity);
        }
        public static void SetKey(SensitiveByteArray pwd, EccFullKeyData ecc, AsciiDomainName identity)
        {
            _pwd = pwd;
            _ecc = ecc;
            _identity = identity;
        }

        // This creates a "Key Registration" instruction
        private static SignedEnvelope DocumentSignedEnvelope()
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

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(new OdinId(_identity), _pwd, _ecc);

            // For michael to look at the JSON
            // string s = signedEnvelope.GetCompactSortedJson();

            return signedEnvelope;
        }

        // Todd, This is how Frodo initiates a request for registering his public key
        // with the Odin Key Chain. Ignore the "web api" parameter, that's just a hack.
        public async static Task<ActionResult> InitiateRequestNotary(NotarizeController webApi)
        {
            // First Frodo generates a smart contract request object
            // This is the function Frodo calls internally to generate a request
            // Here we write all the attributes we want attested

            var signedInstruction = DocumentSignedEnvelope();
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
                var signatureBase64 = SimulateFrodo.SignPreviousHashForPublicKeyChain(previousHashBase64);
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

            var r4 = webApi.GetVerifyNotarizedDocument(signedEnvelope.NotariusPublicus.Signature.ToBase64());

            return r4; 
        }


        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public static string GetPublicKey()
        {
            return _ecc.PublicKeyJwkBase64Url();
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
            var combinedNonce = ByteArrayUtil.Combine("Notarize-".ToUtf8ByteArray(), nonce);

            // We sign the nonce with the signature key
            var signature = _ecc.Sign(_pwd, combinedNonce);

            // We return the signed data to the requestor
            return Convert.ToBase64String(signature);
        }

        // Todd Look in the simulator "Simulate..." for triggering the registration
    }


}
