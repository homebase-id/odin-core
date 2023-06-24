using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Odin.Core.Identity;

namespace Odin.Core.Cryptography.Data
{
    /// <summary>
    /// This envelope is designed to contain information about a document and to be used 
    /// for signature purposes. I.e. the envelope is signed, rather than the raw document 
    /// stream. The envelope contains a SHA-256 of the raw document stream.
    /// </summary>
    public class SignedEnvelope
    {
        // The original envelope
        [JsonPropertyOrder(1)]
        public const int Version = 1;

        // The original envelope
        [JsonPropertyOrder(2)]
        public EnvelopeData Envelope { get; set; }

        // List of signatures on this envelope
        [JsonPropertyOrder(3)]
        public List<SignatureData> Signatures { get; set; } = new List<SignatureData>();

        // Optional Notarius Publicus signature
        [JsonPropertyOrder(4)]
        public SignatureData NotariusPublicus { get; set; }



        public SignedEnvelope() 
        {
            // Sort the list by the timestamp in the SignatureData when loaded from the DB
            Signatures = Signatures.OrderBy(s => s.TimeStamp.milliseconds).ToList();
        }


        public void CreateEnvelopeSignature(OdinId identity, SensitiveByteArray keyPwd, EccFullKeyData eccKey)
        {
            if (Envelope== null)
                throw new ArgumentNullException(nameof(Envelope));

            var signature = Envelope.SignEnvelope(identity, keyPwd, eccKey);
            Signatures.Add(signature);
            // Sort the list by the timestamp in the SignatureData
            Signatures = Signatures.OrderBy(s => s.TimeStamp.milliseconds).ToList();
        }

        public bool VerifyEnvelopeSignatures()
        {
            foreach (var signature in Signatures)
            {
                if (Envelope.VerifyEnvelopeSignature(signature) == false)
                    return false;
            }

            if (NotariusPublicus != null)
            {
                if (VerifyNotariusPublicus() == false) 
                    return false;

                // TODO: Call out to Odin's key chain to verify the public keys

            }

            return true;
        }

        private string GetJsonForNotariusPublicusSignature()
        {
            var copyForSigning = new SignedEnvelope
            {
                Envelope = this.Envelope,
                Signatures = this.Signatures,
                NotariusPublicus = null
            };
            return copyForSigning.GetCompactSortedJson();
        }

        /// <summary>
        /// Consider adding an argument for the original data to verify the correctness of the envelope.
        /// Maybe. Maybe not.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="keyPwd"></param>
        /// <param name="eccKey"></param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        public void SignNotariusPublicus(OdinId identity, SensitiveByteArray keyPwd, EccFullKeyData eccKey)
        {
            if (NotariusPublicus != null)
                throw new ArgumentException("Notary Public has already signed");

            // Validate all the signatures on the envelope
            foreach (var signature in Signatures)
            {
                if (Envelope.VerifyEnvelopeSignature(signature) == false)
                    throw new Exception($"Invalid signature by {signature.Identity.DomainName}");
            }

            var forSigningJson = GetJsonForNotariusPublicusSignature();
            NotariusPublicus = SignatureData.NewSignature(forSigningJson.ToUtf8ByteArray(), identity, keyPwd, eccKey);
        }

        public bool VerifyNotariusPublicus()
        {
            if (NotariusPublicus == null)
                throw new ArgumentNullException("Notary Public missing");

            var forSigningJson = GetJsonForNotariusPublicusSignature();
            return SignatureData.Verify(NotariusPublicus, forSigningJson.ToUtf8ByteArray());
        }

        public string GetCompactSortedJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }
    }
}
