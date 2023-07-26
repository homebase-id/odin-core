using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;

/* JSON outline of a signed envelope
        {
            "Envelope": {
                "ContentHash": "FrEU5YYYjwVSGGNUpVwmnsiZOuTuXlpHHulHrCVrMxE=",
                "Nonce": "Yg9LSbBLFCKGivA+KOzxQEyjbj2wbx2Nucr2ElhDFoI=",
                "ContentHashAlgorithm": "SHA-256",
                "ContentType": "test",
                "TimeStamp": 1689613008423,
                "Length": 5,
                "AdditionalInfo": {
                    "serialno": 42,
                    "title": "test document"
                }
            },
            "Signatures": [
                {
                    "DataHash": "JX4cQYJKbHhDJBM9AsYoLq/m+36kuDRjuE4xZ4SPrME=",
                    "DataHashAlgorithm": "SHA-256",
                    "Identity": "odin.valhalla.com",
                    "PublicKeyDer": "<public key in DER format>",
                    "TimeStamp": 1689613008535,
                    "SignatureAlgorithm": "SHA-384withECDSA",
                    "Signature": "<signature>"
                },
                {
                    "DataHash": "JX4cQYJKbHhDJBM9AsYoLq/m+36kuDRjuE4xZ4SPrME=",
                    "DataHashAlgorithm": "SHA-256",
                    "Identity": "thor.valhalla.com",
                    "PublicKeyDer": "<public key in DER format>",
                    "TimeStamp": 1689613008574,
                    "SignatureAlgorithm": "SHA-384withECDSA",
                    "Signature": "<signature>"
                }
            ],
            "NotariusPublicus": {
                "DataHash": "L1q/x5APRQyEPq+X3OdqOEWH8yWfuPig3WGxztAOVAI=",
                "DataHashAlgorithm": "SHA-256",
                "Identity": "notarius.publicus.com",
                "PublicKeyDer": "<public key in DER format>",
                "TimeStamp": 1689613008613,
                "SignatureAlgorithm": "SHA-384withECDSA",
                "Signature": "<signature>"
            }
        }
*/

namespace Odin.Core.Cryptography.Signatures
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
            if (Envelope == null)
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
                Envelope = Envelope,
                Signatures = Signatures,
                NotariusPublicus = null  // I forgot what's the whole point of this copyForSigning?
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
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            return JsonSerializer.Serialize(this, options);
        }

        public static SortedDictionary<string, string> ConvertJsonObjectToSortedDict(object jsonObject)
        {
            var result = new SortedDictionary<string, string>();

            if (jsonObject is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in jsonElement.EnumerateObject())
                {
                    result[property.Name] = property.Value.ValueKind == JsonValueKind.Object ?
                                            (object)property.Value as string :
                                            property.Value.ToString();
                }
            }

            return result;
        }

        public static SortedDictionary<string, object> ConvertNestedObjectsToDicts(SortedDictionary<string, object> original)
        {
            if (original == null)
                return null;

            var result = new SortedDictionary<string, object>();
            foreach (var kvp in original)
            {
                if (kvp.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                {
                    var jsonString = jsonElement.GetRawText();
                    var nestedDict = JsonSerializer.Deserialize<SortedDictionary<string, object>>(jsonString);
                    result[kvp.Key] = ConvertNestedObjectsToDicts(nestedDict);
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        public static SignedEnvelope Deserialize(string json)
        {
            SignedEnvelope signedEnvelope = JsonSerializer.Deserialize<SignedEnvelope>(json);

            // Replaced nested sortedDicts with SortedDictionary
            signedEnvelope.Envelope.AdditionalInfo = ConvertNestedObjectsToDicts(signedEnvelope.Envelope.AdditionalInfo);

            return signedEnvelope;
        }

    }
}
