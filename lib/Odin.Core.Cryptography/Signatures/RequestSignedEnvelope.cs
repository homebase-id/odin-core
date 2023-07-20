using NodaTime;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Odin.Core.Cryptography.Signatures
{
    public class RequestSignedEnvelope
    {
        public static SignedEnvelope VerifyRequestEnvelope(string requestSignedEnvelope)
        {
            if (requestSignedEnvelope.Length < 10)
                throw new ArgumentException("Too small, expecting an ODIN signed envelope JSON");

            SignedEnvelope? signedEnvelope;
            try
            {
                signedEnvelope = JsonSerializer.Deserialize<SignedEnvelope>(requestSignedEnvelope);

                if (signedEnvelope == null)
                    throw new ArgumentException($"Unable to parse JSON.");
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Unable to parse JSON {ex.Message}");
            }

            // Verify the embedded signature

            if (signedEnvelope.VerifyEnvelopeSignatures() == false)
                throw new ArgumentException($"Unable to verify signatures.");

            PunyDomainName id;
            try
            {
                id = new PunyDomainName(signedEnvelope.Signatures[0].Identity);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid identity {ex.Message}");
            }

            if (signedEnvelope.Envelope.ContentType != EnvelopeData.ContentTypeRequest)
                throw new ArgumentException($"ContentType must be 'request'");

            if (signedEnvelope.Signatures[0].TimeStamp < UnixTimeUtc.Now().AddMinutes(-20) || signedEnvelope.Signatures[0].TimeStamp > UnixTimeUtc.Now().AddMinutes(+20))
                throw new ArgumentException($"Your clock is too much out of sync or request too old");


            return signedEnvelope;
        }

        private static SignedEnvelope CreateRequestEnvelope(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, SortedDictionary<string, object> requestData)
        {
            // Verify dataToAttest is not null and contains data
            if (requestData == null || requestData.Count == 0)
                throw new ArgumentException("Invalid data for request. Please ensure some data.");

            // There's something to sort out here
            string USAGEPOLICY_URL = $"https://{identity.DomainName}/policies/request-usage-policy";

            // Let's say we have a document (possibly a file)
            // We want some additional information in the envelope
            var additionalInfo = new SortedDictionary<string, object>
            {
                // { "identity", identity.DomainName },
                { "requestTimestampSeconds", UnixTimeUtc.Now().seconds },
                { "expirationTimestampSeconds", UnixTimeUtc.Now().AddSeconds(3600*24*14).seconds },
                { "usagePolicyUrl", USAGEPOLICY_URL },
                { "data", requestData }  // Insert data relevant for the request here
            };

            // Make sure it's valid
            EnvelopeData.VerifyAdditionalInfoTypes(additionalInfo);

            string doc = EnvelopeData.StringifyData(additionalInfo); // The document content is the whole of additionalInfo
            byte[] content = doc.ToUtf8ByteArray();

            // Create an Envelope for this document
            var envelope = new EnvelopeData();
            envelope.CalculateContentHash(content, EnvelopeData.ContentTypeRequest);
            envelope.SetAdditionalInfo(additionalInfo);

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(new OdinId(identity), pwd, eccKey);


            // For michael to look at the JSON
            // string s = signedEnvelope.GetCompactSortedJson();

            return signedEnvelope;
        }

        // This function attests that the OdinId is associated with a human.
        public static SignedEnvelope CreateRequestAttestation(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity)
        {
            var requestData = new SortedDictionary<string, object>
            {
                { "Request", "ServiceRequest" },
                { "RequestType", "Attestation" }
            };

            return CreateRequestEnvelope(eccKey, pwd, identity, requestData);
        }
    }
}
