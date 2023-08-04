using NodaTime;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Odin.Core.Cryptography.Signatures
{
    public class AttestationManagement
    {
        public const string AUTHORITY_IDENTITY = "heimdallr.odin.earth";
        public const string JsonKeyIsHuman = "IsHuman";
        public const string JsonKeyLegalName = "LegalName";
        public const string JsonKeySubsetLegalName = "SubsetLegalName";
        public const string JsonKeyResidentialAddress = "ResidentialAddress";
        public const string JsonKeyEmailAddress = "EmailAddress";
        public const string JsonKeyPhoneNumber = "PhoneNumber";
        public const string JsonKeyBirthdate = "Birthdate";
        public const string JsonKeyNationality = "Nationality";

        public static bool VerifyAttestation(SignedEnvelope attestation)
        {
            // Don't know if we'll need more checks...
            //
            return attestation.VerifyEnvelopeSignatures();
        }

        private static SignedEnvelope Attestation(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, SortedDictionary<string, object> dataToAttest)
        {
            // There's something to sort out here
            const string VERIFYURL = "https://heimdallr.odin.earth/api/v1/verify?prpt=$signature"; // Replace $signature with the signatureBase64 when calling
            const string ATTESTATIONTYPE_PERSONALINFO = "personalInfo";
            string USAGEPOLICY_URL = $"https://{identity.DomainName}/policies/attestation-usage-policy";

            const string CONTENTTYPE_ATTESTATION = "attestation";

            // Verify dataToAttest is not null and contains data
            if (dataToAttest == null || dataToAttest.Count == 0)
            {
                throw new ArgumentException("Invalid attestation data. Please ensure that dataToAttest contains data.");
            }

            // Let's say we have a document (possibly a file)
            // We want some additional information in the envelope
            var additionalInfo = new SortedDictionary<string, object>
            {
                { "identity", identity.DomainName },
                { "issued", ((Instant) UnixTimeUtc.Now()).InUtc().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "expiration", ((Instant) UnixTimeUtc.Now().AddSeconds(3600*24*365*5)).InUtc().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "authority", AUTHORITY_IDENTITY },
                { "URL", VERIFYURL },
                { "attestationFormat", ATTESTATIONTYPE_PERSONALINFO },
                { "usagePolicyUrl", USAGEPOLICY_URL },
                { "data", dataToAttest }  // Insert dataToAttest here
            };

            // Make sure it's valid
            EnvelopeData.VerifyAdditionalInfoTypes(additionalInfo);

            SortedDictionary<string, object> data = (SortedDictionary<string, object>)additionalInfo["data"];
            string doc = EnvelopeData.StringifyData(data);
            byte[] content = doc.ToUtf8ByteArray();

            // Create an Envelope for this document, we calculate the HASH based on the data attested
            var envelope = new EnvelopeData(CONTENTTYPE_ATTESTATION, "");
            envelope.SetAdditionalInfo(additionalInfo);
            envelope.CalculateContentHash(content);

            // Create an identity and keys needed
            OdinId authorityIdentity = new OdinId(AUTHORITY_IDENTITY);

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(authorityIdentity, pwd, eccKey);


            // For michael to look at the JSON
            // string s = signedEnvelope.GetCompactSortedJson();

            return signedEnvelope;
        }

        // This function attests that the OdinId is associated with a human.
        public static SignedEnvelope AttestHuman(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeyIsHuman, true }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }

        // This function attests to the legal name of the owner of the OdinId.
        public static SignedEnvelope AttestLegalName(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, string legalName)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeyLegalName, legalName }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }

        /// <summary>
        /// A SubsetLegalName for personal names must include the full last name (surname) from the legal name 
        /// and at least one other element (first name, middle name(s)) either in full or as an initial, excluding
        /// trivial parts such as honorifics, prefixes, and suffixes.
        /// Example, given a fulle name of "Sir Winston Leonard Spencer Churchill":
        /// Valid: W. Churchill, Winston L. Churchill, W.L.S. Churchill, Leonard S. Churchill, Spencer Churchill, 
        /// Leonard Spencer Churchill, Winston Leonard Spencer Churchill
        /// </summary>
        /// <param name="eccKey"></param>
        /// <param name="pwd"></param>
        /// <param name="identity"></param>
        /// <param name="subsetLegalName"></param>
        /// <returns></returns>
        public static SignedEnvelope AttestSubsetLegalName(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, string subsetLegalName)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeySubsetLegalName, subsetLegalName }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }

        // This function attests to the residential address of the owner of the OdinId.
        public static SignedEnvelope AttestResidentialAddress(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, SortedDictionary<string, string> address)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeyResidentialAddress, address }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }

        // This function attests to the email address of the owner of the OdinId.
        public static SignedEnvelope AttestEmailAddress(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, string emailAddress)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeyEmailAddress, emailAddress }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }

        // This function attests to the phone number of the owner of the OdinId.
        public static SignedEnvelope AttestPhoneNumber(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, string phoneNumber)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeyPhoneNumber, phoneNumber }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }

        // This function attests to the birthdate of the owner of the OdinId.
        public static SignedEnvelope AttestBirthdate(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, DateOnly birthdate)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeyBirthdate, birthdate.ToString("yyyy-MM-dd") }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }

        // This function attests to the nationality of the owner of the OdinId.
        public static SignedEnvelope AttestNationality(EccFullKeyData eccKey, SensitiveByteArray pwd, PunyDomainName identity, string nationality)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { JsonKeyNationality, nationality }
            };

            return Attestation(eccKey, pwd, identity, dataToAttest);
        }
    }
}
