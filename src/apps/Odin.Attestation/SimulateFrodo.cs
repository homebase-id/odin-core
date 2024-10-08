﻿using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Odin.Attestation.Controllers;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Attestation
{
    public static class SimulateFrodo
    {
        public static string Identity = "frodobaggins.me";
        private static SensitiveByteArray _pwd;
        private static EccFullKeyData _ecc;
        private static Dictionary<string, Int64> _database;

        static SimulateFrodo()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, EccKeySize.P384, 1);
            _database = new Dictionary<string, Int64> { };
        }

        private static void GenerateNewKeys()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, EccKeySize.P384, 1);
        }

        private static void SaveLocally(string nonceBase64)
        {
            // Save nonce in Frodo's database, the nonce could easily be the key
            // Dont think we need to store the whole signed doc
            // Todd this would easily go into the key-two-value database
            _database.Add(nonceBase64, UnixTimeUtc.Now().seconds);
        }

        private static void DeleteLocally(string nonceBase64)
        {
            // Save nonce in Frodo's database, the nonce could easily be the key
            // Dont think we need to store the whole signed doc
            // Todd this would easily go into the key-two-value database
            _database.Remove(nonceBase64);
        }

        private static bool LoadLocally(string nonceBase64)
        {
            // Save nonce in Frodo's database, the nonce could easily be the key
            // Dont think we need to store the whole signed doc
            // Todd this would easily go into the key-two-value database
            if (!_database.ContainsKey(nonceBase64))
                return false;

            var r = _database[nonceBase64];

            if (UnixTimeUtc.Now().seconds - r > 60)
                return false;

            _database.Remove(nonceBase64);

            return true;
        }



        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public static string GetPublicKey()
        {
            return _ecc.PublicKeyJwkBase64Url();
        }
        
        public async static Task<ActionResult> InitiateRequestForAttestation(AttestationRequestController webApi)
        {
            var address = new SortedDictionary<string, object>
            {
                { "street", "Bag End" },
                { "city", "Hobbiton" },
                { "region", "The Shire" },
                { "postalCode", "4242" },
                { "country", "Middle Earth" }
            };

            // Here we write all the attributes we want attested
            var dataToAttest = new SortedDictionary<string, object>()
                    { { AttestationManagement.JsonKeySubsetLegalName, "F. Baggins" },
                      { AttestationManagement.JsonKeyLegalName, "Frodo Baggins" },
                      { AttestationManagement.JsonKeyNationality, "Middle Earth" },
                      { AttestationManagement.JsonKeyPhoneNumber, "+45 26 44 70 33"},
                      { AttestationManagement.JsonKeyBirthdate, "1073-10-29" },
                      { AttestationManagement.JsonKeyEmailAddress, "f@baggins.me" },
                      { AttestationManagement.JsonKeyResidentialAddress, address } };

            // Let's build the envelope that Frodo will send
            var signedEnvelope = SimulateFrodo.RequestEnvelope(dataToAttest);

            SaveLocally(signedEnvelope.Envelope.ContentNonce.ToBase64());

            // Todd, call the attestation server via HttpClient(Factory)
            // Here I had to hack it
            var r1 = await webApi.GetRequestAttestation(signedEnvelope.GetCompactSortedJson());

            return r1;
        }

        // Todd this is the endpoint on an identity that receives a jsonArray of attestations (signedEnvelopes)
        // Returns signed attestationId
        public static void DeliverAttestations(string attestationIdBase64, string attestationListJsonArray)
        {
            //
            // Verify this is in response to my own request
            //
            if (LoadLocally(attestationIdBase64) == false)
                throw new Exception("Frodo unable to find request ID");

            if (attestationListJsonArray == null)
                throw new ArgumentNullException(nameof(attestationListJsonArray));

            List<string>? jsonList;

            try
            {
                jsonList = JsonSerializer.Deserialize<List<string>>(attestationListJsonArray);
            }
            catch (Exception ex) 
            {
                throw new Exception($"Unable to deserialize attestation list {ex.Message}");
            }

            if (jsonList == null)
                throw new Exception("The resulting deserialized json array is null");

            List<SignedEnvelope> attestationList;
            try
            {
                attestationList = jsonList.Select(json => JsonSerializer.Deserialize<SignedEnvelope>(json)!).ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"Unable to deserialize the array {ex.Message}");
            }


            // Verify the validitiy of the signatures and the signatory
            //
            for (int i = 0; i < attestationList.Count; i++)
            {
                if (attestationList[i].VerifyEnvelopeSignatures() == false)
                    throw new Exception($"Unable to verify signature for element {i}");

                if (attestationList[i].Signatures[0].Identity != AttestationManagement.AUTHORITY_IDENTITY)
                    throw new Exception($"Incorrect signer {attestationList[i]!.Signatures[0].Identity} expected {AttestationManagement.AUTHORITY_IDENTITY}");
            }


            // Now we store these as N data profile attestation properties
            // 
            for (int i = 0; i < attestationList.Count; i++)
            {
                // Todd TODO: Store it in Frodo's profile data
            }

            DeleteLocally(attestationIdBase64);
        }


        // This is the function Frodo calls internally to generate a request
        public static SignedEnvelope RequestEnvelope(SortedDictionary<string, object> dataToAtttest)
        {
            // We create an empty envelope with a contentType of "request"
            //
            var signedEnvelope = InstructionSignedEnvelope.CreateInstructionAttestation(_ecc, _pwd, new AsciiDomainName(Identity), dataToAtttest);

            return signedEnvelope;
        }


        /// <summary>
        /// Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        ///  _ecc would be the identity's signature key.
        /// </summary>
        /// <param name="previousHashBase64">The nonce to sign</param>
        /// <param name="previousHashBase64">A code proving we're going to sign it</param>
        /// <returns>Base64 signature</returns>
        /// <exception cref="Exception"></exception>
        public static string SignPreviousHashForAttestationChain(string previousHashBase64)
        {
            // tempCode was OK, we continue
            var nonce = Convert.FromBase64String(previousHashBase64);

            // Todd need to check this JIC 
            if ((nonce.Length < 16) || (nonce.Length > 32))
                throw new Exception("invalid nonce size");

            // We sign the nonce with the signature key
            var signature = _ecc.Sign(_pwd, nonce);

            // We return the signed data to the requestor
            return Convert.ToBase64String(signature);
        }

        // Todd Look in the simulator "Simulate..." for triggering the registration
    }


}
