using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.IO;
using Odin.Core.Identity;
using System.Text.Json.Serialization;
using Odin.Core.Cryptography.Data;

namespace Odin.Core.Cryptography.Signatures
{
    /// <summary>
    /// This envelope is designed to contain information about a document and to be used 
    /// for signature purposes. I.e. the envelope is signed, rather than the raw document 
    /// stream. The envelope contains a SHA-256 of the raw document stream.
    /// </summary>
    public class EnvelopeData
    {
        public const string ContentTypeAttestation = "attestation";
        public const string ContentTypeRequest = "request";
        public const string ContentTypeDocument = "document";


        [JsonPropertyOrder(1)]
        public const int Version = 1;

        [JsonPropertyOrder(2)]
        public byte[] ContentHash { get; set; }

        [JsonPropertyOrder(3)]
        public byte[] Nonce { get; set; }

        [JsonPropertyOrder(4)]
        public string ContentHashAlgorithm { get; set; }

        /// <summary>
        /// ContentType can currently be: Document, Attestation (proving e.g. my name), Request, more to come
        /// </summary>
        [JsonPropertyOrder(5)]
        public string ContentType { get; set; }

        [JsonPropertyOrder(6)]
        public UnixTimeUtc TimeStamp { get; set; }

        [JsonPropertyOrder(7)]
        public long Length { get; set; }

        [JsonPropertyOrder(8)]
        public SortedDictionary<string, object> AdditionalInfo { get; set; } = new SortedDictionary<string, object>();

        public EnvelopeData()
        {
            // Default constructor for restoring via DB
        }

        public string GetCompactSortedJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }

        public static bool VerifyAdditionalInfoTypes(SortedDictionary<string, object> additionalInfo)
        {
            foreach (var kvp in additionalInfo)
            {
                if (!(kvp.Value is string || kvp.Value is SortedDictionary<string, object>))
                {
                    return false;
                }

                if (kvp.Value is SortedDictionary<string, object> nestedDict)
                {
                    if (!VerifyAdditionalInfoTypes(nestedDict))
                    {
                        return false;
                    }
                }
            }

            return true;
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
            var envelopeJson = GetCompactSortedJson();
            var signature = SignatureData.NewSignature(envelopeJson.ToUtf8ByteArray(), identity, keyPwd, eccKey);
            return signature;
        }


        public bool VerifyEnvelopeSignature(SignatureData signature)
        {
            var envelopeJson = GetCompactSortedJson();
            return SignatureData.Verify(signature, envelopeJson.ToUtf8ByteArray());
        }


        /// <summary>
        /// Make an envelope for a small document (do not use large files as a memory byte[])
        /// </summary>
        /// <param name="content"></param>
        /// <param name="additionalInfo">For example, "author", "title", whatever is important for the document</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateContentHash(Stream content, string contentType, SortedDictionary<string, object> additionalInfo)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (additionalInfo != null)
            {
                if (!VerifyAdditionalInfoTypes(AdditionalInfo))
                {
                    throw new ArgumentException("Invalid type in AdditionalInfo.");
                }
            }

            Nonce = ByteArrayUtil.GetRndByteArray(32);
            (ContentHash, Length) = HashUtil.StreamSHA256(content, Nonce);
            ContentHashAlgorithm = HashUtil.SHA256Algorithm;
            ContentType = contentType;
            TimeStamp = UnixTimeUtc.Now();
            AdditionalInfo = additionalInfo;
        }

        /// <summary>
        /// Make an envelope from a file.
        /// </summary>
        /// <param name="documentFilename"></param>
        /// <param name="additionalInfo">For example, "author", "title", whatever is important for the document</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateContetntHash(string contentFileName, string contentType, SortedDictionary<string, object> additionalInfo)
        {
            if (string.IsNullOrEmpty(contentFileName))
                throw new ArgumentNullException(nameof(contentFileName));

            using (var fileStream = File.OpenRead(contentFileName))
            {
                CalculateContentHash(fileStream, contentType, additionalInfo);
            }
        }

        /// <summary>
        /// Make an envelope from a small memory byte[]
        /// </summary>
        /// <param name="content"></param>
        /// <param name="additionalInfo"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateContentHash(byte[] content, string contentType, SortedDictionary<string, object> additionalInfo)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            using (var memoryStream = new MemoryStream(content))
            {
                CalculateContentHash(memoryStream, contentType, additionalInfo);
            }
        }
    }
}
