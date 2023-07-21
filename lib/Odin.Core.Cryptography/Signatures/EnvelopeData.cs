using Odin.Core.Cryptography.Crypto;
using Odin.Core.Time;
using System;
using System.Collections.Generic;
using System.IO;
using Odin.Core.Identity;
using System.Text.Json.Serialization;
using Odin.Core.Cryptography.Data;
using System.Text;

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
        public long ContentLength { get; set; }

        [JsonPropertyOrder(4)]
        public byte[] ContentNonce { get; set; }

        [JsonPropertyOrder(5)]
        public string ContentHashAlgorithm { get; set; }

        /// <summary>
        /// ContentType should be one of the constants ContentType... above. More to come.
        /// </summary>
        [JsonPropertyOrder(6)]
        public string ContentType { get; set; }

        [JsonPropertyOrder(7)]
        public UnixTimeUtc TimeStamp { get; set; }

        [JsonPropertyOrder(8)]
        public SortedDictionary<string, object> AdditionalInfo { get; set; } // I had to drop checking validity here, too much trouble


        public EnvelopeData()
        {
            // Default constructor for restoring via DB
        }


        public string GetCompactSortedJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }


        /// <summary>
        /// Converts a SortedDictionary into a string in a predictable and consistent manner.
        /// The function iteratively appends keys and values from the SortedDictionary to a StringBuilder.
        /// If a value is another SortedDictionary, the function calls itself recursively.
        /// </summary>
        /// <param name="data">The SortedDictionary to be converted into a string.</param>
        /// <returns>A string representation of the SortedDictionary. Each key-value pair is represented as 'key:value'.
        /// Pairs are separated by commas. If a value is a SortedDictionary, it is represented as 'key:{nested key:nested value,...}'.
        /// The string does not have a trailing comma. The keys and values are sorted as per their natural ordering in the SortedDictionary.</returns>
        public static string StringifyData(SortedDictionary<string, object> data)
        {
            StringBuilder sb = new StringBuilder();

            foreach (KeyValuePair<string, object> entry in data)
            {
                if (sb.Length > 0)
                {
                    sb.Append(",");
                }

                sb.Append(entry.Key);
                sb.Append(":");

                if (entry.Value is SortedDictionary<string, object> nestedDict)
                {
                    sb.Append("{");
                    sb.Append(StringifyData(nestedDict));
                    sb.Append("}");
                }
                else
                {
                    sb.Append(entry.Value.ToString());
                }
            }

            return sb.ToString();
        }


        private static bool IsAtomicJsonType(object obj)
        {
            return obj == null ||
                   obj is string ||
                   obj is bool ||
                   obj is int ||
                   obj is long ||
                   obj is double ||
                   obj is float;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="additionalInfo"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static bool VerifyAdditionalInfoTypes(SortedDictionary<string, object> additionalInfo)
        {
            if (additionalInfo == null)
                return true;

            foreach (var kvp in additionalInfo)
            {
                if (kvp.Value is SortedDictionary<string, object> nestedDict)
                {
                    if (!VerifyAdditionalInfoTypes(nestedDict))  // Use recursion here
                        return false;
                }
                else if (!IsAtomicJsonType(kvp.Value))
                    return false;
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
            if (ContentNonce == null)
                throw new Exception("You must call CalculateContentHash before signing");
            var envelopeJson = GetCompactSortedJson();
            var signature = SignatureData.NewSignature(envelopeJson.ToUtf8ByteArray(), identity, keyPwd, eccKey);
            return signature;
        }


        public bool VerifyEnvelopeSignature(SignatureData signature)
        {
            var envelopeJson = GetCompactSortedJson();
            return SignatureData.Verify(signature, envelopeJson.ToUtf8ByteArray());
        }


        public void SetAdditionalInfo(SortedDictionary<string, object> additionalInfo)
        {
            if (AdditionalInfo != null)
                throw new ArgumentException($"Trying to overwrite {nameof(AdditionalInfo)}");

            if (!VerifyAdditionalInfoTypes(AdditionalInfo))
                throw new ArgumentException($"Invalid type in {nameof(AdditionalInfo)}");

            AdditionalInfo = additionalInfo; 
        }


        /// <summary>
        /// Calculates the SHA256 hash of the provided content stream and a randomly generated nonce value.
        /// The hash serves as a unique representation (digest) of the content, and is embedded in the envelope
        /// rather than having to insert or save the whole file. 
        /// The function also sets various properties of the envelope object based on the input and computed hash.
        /// </summary>
        /// <param name="content">The content stream to be hashed.</param>
        /// <param name="contentType">The type of content that is being hashed, e.g. attestation, document.</param>
        /// <param name="additionalInfo">Additional relevant information for the content. This could be metadata like 'author', 'title', etc.</param>
        /// <exception cref="ArgumentNullException">Thrown when the content stream is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an invalid type is present in AdditionalInfo.</exception>
        public void CalculateContentHash(Stream content, string contentType)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (ContentNonce != null)
                throw new Exception("You already calculated the content hash");

            ContentNonce = ByteArrayUtil.GetRndByteArray(32);
            (ContentHash, ContentLength) = HashUtil.StreamSHA256(content, ContentNonce);
            ContentHashAlgorithm = HashUtil.SHA256Algorithm;
            ContentType = contentType;
            TimeStamp = UnixTimeUtc.Now();
        }

        /// <summary>
        /// Make an envelope from a file.
        /// </summary>
        /// <param name="documentFilename"></param>
        /// <param name="additionalInfo">For example, "author", "title", whatever is important for the document</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateContentHash(string contentFileName, string contentType)
        {
            if (string.IsNullOrEmpty(contentFileName))
                throw new ArgumentNullException(nameof(contentFileName));

            using (var fileStream = File.OpenRead(contentFileName))
            {
                CalculateContentHash(fileStream, contentType);
            }
        }

        /// <summary>
        /// Make an envelope from a small memory byte[]
        /// </summary>
        /// <param name="content"></param>
        /// <param name="additionalInfo"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CalculateContentHash(byte[] content, string contentType)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            using (var memoryStream = new MemoryStream(content))
            {
                CalculateContentHash(memoryStream, contentType);
            }
        }
    }
}
