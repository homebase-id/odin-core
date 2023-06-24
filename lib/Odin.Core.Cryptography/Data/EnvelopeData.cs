using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.IO;
using Odin.Core.Identity;
using System.Text.Json.Serialization;

namespace Odin.Core.Cryptography.Data
{
    /// <summary>
    /// This envelope is designed to contain information about a document and to be used 
    /// for signature purposes. I.e. the envelope is signed, rather than the raw document 
    /// stream. The envelope contains a SHA-256 of the raw document stream.
    /// </summary>
    public class EnvelopeData
    {
        [JsonPropertyOrder(1)]
        public const int Version = 1;

        [JsonPropertyOrder(2)]
        public byte[] DocumentHash { get; set; }

        [JsonPropertyOrder(3)]
        public byte[] Nonce { get; set; }

        [JsonPropertyOrder(4)]
        public string DocumentHashAlgorithm { get; set; }

        [JsonPropertyOrder(5)]
        public UnixTimeUtc TimeStamp { get; set; }

        [JsonPropertyOrder(6)]
        public Int64 Length { get; set; }

        [JsonPropertyOrder(7)]
        public SortedDictionary<string, object> AdditionalInfo { get; set; } = new SortedDictionary<string, object>();

        public EnvelopeData()
        {
            // Default constructor for restoring via DB
        }

        public string GetCompactSortedJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }


        /// <summary>
        /// Use to create a new signature for this envelope
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="keyPwd"></param>
        /// <param name="eccKey"></param>
        /// <returns></returns>
        public SignatureData SignEnvelope(OdinId identity, SensitiveByteArray keyPwd, EccFullKeyData eccKey)
        {
            var envelopeJson = this.GetCompactSortedJson();
            var signature = SignatureData.Sign(envelopeJson.ToUtf8ByteArray(), identity, keyPwd, eccKey);
            return signature;
        }


        public bool VerifyEnvelopeSignature(SignatureData signature)
        {
            var envelopeJson = this.GetCompactSortedJson();
            return SignatureData.Verify(signature, envelopeJson.ToUtf8ByteArray());
        }


        /// <summary>
        /// Make an envelope for a small document (do not use large files as a memory byte[])
        /// </summary>
        /// <param name="document"></param>
        /// <param name="additionalInfo">For example, "author", "title", whatever is important for the document</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateDocumentHash(Stream document, SortedDictionary<string, object> additionalInfo)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            Nonce = ByteArrayUtil.GetRndByteArray(32);
            (DocumentHash, Length) = HashUtil.StreamSHA256(document, Nonce);
            DocumentHashAlgorithm = HashUtil.SHA256Algorithm;
            TimeStamp = UnixTimeUtc.Now();
            AdditionalInfo = additionalInfo;
        }

        /// <summary>
        /// Make an envelope from a file.
        /// </summary>
        /// <param name="documentFilename"></param>
        /// <param name="additionalInfo">For example, "author", "title", whatever is important for the document</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateDocumentHash(string documentFileName, SortedDictionary<string, object> additionalInfo)
        {
            if (string.IsNullOrEmpty(documentFileName))
                throw new ArgumentNullException(nameof(documentFileName));

            using (var fileStream = File.OpenRead(documentFileName))
            {
                CalculateDocumentHash(fileStream, additionalInfo);
            }
        }

        /// <summary>
        /// Make an envelope from a small memory byte[]
        /// </summary>
        /// <param name="document"></param>
        /// <param name="additionalInfo"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateDocumentHash(byte[] document, SortedDictionary<string, object> additionalInfo)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            using (var memoryStream = new MemoryStream(document))
            {
                CalculateDocumentHash(memoryStream, additionalInfo);
            }
        }
    }
}
