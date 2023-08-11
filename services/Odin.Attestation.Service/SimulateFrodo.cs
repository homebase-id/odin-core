﻿using Odin.Core;
using Odin.Core.Util;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Cryptography.Data;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OdinsAttestation.Controllers;

namespace OdinsAttestation
{
    public static class SimulateFrodo
    {
        public static string Identity = "frodobaggins.me";
        private static SensitiveByteArray _pwd;
        private static EccFullKeyData _ecc;

        static SimulateFrodo()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
        }

        private static void GenerateNewKeys()
        {
            _pwd = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            _ecc = new EccFullKeyData(_pwd, 1);
        }

        // Todd this is the function on an identity that should return Frodo's public (signature) key (ECC)
        // For example https://frodo.baggins.me/api/v1/signature/publickey
        public static string GetPublicKey()
        {
            if (ByteArrayUtil.EquiByteArrayCompare(_ecc.publicKey, Convert.FromBase64String(_ecc.publicDerBase64())) == false)
                throw new Exception("kaboom");
            return _ecc.publicDerBase64();
        }

        public async static Task<IActionResult> InitiateRequestForAttestation(AttestationRequestController webApi)
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

            // Todd, call the attestation server via HttpClient(Factory)
            // Here I had to hack it
            var r1 = await webApi.GetRequestAttestation(signedEnvelope.GetCompactSortedJson());

            return r1;
        }

        // Todd this is the endpoint on an identity that receives a jsonArray of attestations (signedEnvelopes)
        public static void DeliverAttestations(string attestationListJsonArray)
        {
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
                // Store it
            }
        }


        // This is the function Frodo calls internally to generate a request
        public static SignedEnvelope RequestEnvelope(SortedDictionary<string, object> dataToAtttest)
        {
            // We create an empty envelope with a contentType of "request"
            //
            var signedEnvelope = InstructionSignedEnvelope.CreateInstructionAttestation(_ecc, _pwd, new PunyDomainName(Identity), dataToAtttest);

            return signedEnvelope;
        }


        /// <summary>
        /// Todd this is a function on an identity that responds to Odin's key chain service and signs a nonce
        ///  _ecc would be the identity's signature key.
        /// </summary>
        /// <param name="nonceBase64">The nonce to sign</param>
        /// <param name="tempCodeBase64">A code proving we're going to sign it</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static string SignNonceForKeyChain(string nonceBase64, string tempCodeBase64)
        {
            // @Todd First sanity check the tempCode
            var tempCode = Convert.FromBase64String(tempCodeBase64);
            if ((tempCode.Length < 16) || (tempCode.Length > 32))
                throw new Exception("invalid nonce size");

            // @Todd then load the tempCode from the DB
            // var tempCode = identityDb.tblKeyValue.Get(CONST_..._ID);
            // If the tempCode is more than 10 seconds old, fail
            // DELETE the tempCode from the DB
            // identityDb.tblKeyValue.Delete(CONST_..._ID);

            // tempCode was OK, we continue
            var nonce = Convert.FromBase64String(nonceBase64);

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
