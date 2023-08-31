using NodaTime;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using Org.BouncyCastle.Asn1;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Odin.Core.Cryptography.Signatures
{
    public class InstructionSignedEnvelope
    {
        public const string ENVELOPE_SUB_TYPE_ATTESTATION = "attestation";
        public const string ENVELOPE_SUB_TYPE_KEY_REGISTRATION = "key registration";

        public static SignedEnvelope VerifyInstructionEnvelope(string instructionSignedEnvelope)
        {
            if (instructionSignedEnvelope.Length < 10)
                throw new ArgumentException("Too small, expecting an ODIN signed envelope JSON");

            SignedEnvelope signedEnvelope;

            try
            {
                signedEnvelope = SignedEnvelope.Deserialize(instructionSignedEnvelope);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Unable to parse JSON {ex.Message}");
            }

            // Verify the embedded signature
            if (signedEnvelope.VerifyEnvelopeSignatures() == false)
                throw new ArgumentException($"Unable to verify signatures.");

            // Verify the contentNonce
            if ((signedEnvelope.Envelope.ContentNonce.Length < 16) || (signedEnvelope.Envelope.ContentNonce.Length > 32))
                throw new ArgumentException($"Envelope.ContentNonce unexpected");

            AsciiDomainName id;
            try
            {
                id = new AsciiDomainName(signedEnvelope.Signatures[0].Identity);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid identity {ex.Message}");
            }

            if (signedEnvelope.Envelope.EnvelopeType != EnvelopeData.EnvelopeTypeInstruction)
                throw new ArgumentException($"ContentType must be 'request'");

            if (signedEnvelope.Signatures[0].TimeStamp < UnixTimeUtc.Now().AddMinutes(-20) || signedEnvelope.Signatures[0].TimeStamp > UnixTimeUtc.Now().AddMinutes(+20))
                throw new ArgumentException($"Your clock is too much out of sync or request too old");


            return signedEnvelope;
        }

        private static SignedEnvelope CreateInstructionEnvelope(EccFullKeyData eccKey, SensitiveByteArray pwd, AsciiDomainName identity, string envelopeSubType, SortedDictionary<string, object> instructionData)
        {
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
                { "data", instructionData }  // Insert data relevant for the request here
            };

            // Make sure it's valid
            if (EnvelopeData.VerifyAdditionalInfoTypes(additionalInfo) == false)
                throw new Exception("Invalid additional data");

            string doc = EnvelopeData.StringifyData(additionalInfo); // The document content is the whole of additionalInfo
            byte[] content = doc.ToUtf8ByteArray();

            // Create an Envelope for this document
            var envelope = new EnvelopeData(EnvelopeData.EnvelopeTypeInstruction, envelopeSubType);
            envelope.SetAdditionalInfo(additionalInfo);
            envelope.CalculateContentHash(content);

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(new OdinId(identity), pwd, eccKey);


            // For michael to look at the JSON
            // string s = signedEnvelope.GetCompactSortedJson();

            return signedEnvelope;
        }

        // Create an instruction to attest the supplied data
        public static SignedEnvelope CreateInstructionAttestation(EccFullKeyData eccKey, SensitiveByteArray pwd, AsciiDomainName identity, SortedDictionary<string, object> dataToAtttest)
        {
            return CreateInstructionEnvelope(eccKey, pwd, identity, ENVELOPE_SUB_TYPE_ATTESTATION, dataToAtttest);
        }

        // Create an instruction to attest the supplied data
        public static SignedEnvelope CreateInstructionKeyRegistration(EccFullKeyData eccKey, SensitiveByteArray pwd, AsciiDomainName identity, SortedDictionary<string, object> data)
        {
            return CreateInstructionEnvelope(eccKey, pwd, identity, ENVELOPE_SUB_TYPE_KEY_REGISTRATION, data);
        }
    }
}
