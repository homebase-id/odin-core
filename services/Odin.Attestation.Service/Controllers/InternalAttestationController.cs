// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Storage.SQLite.AttestationDatabase;
using Odin.Core.Util;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Cryptography.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Routing;

namespace OdinsAttestation.Controllers
{
    [ApiController]
    [Route("internal")]
    public class InternalAttestationController : ControllerBase
    {
        private readonly ILogger<AttestationRequestController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AttestationDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        // private readonly bool _simulate = true;
        private readonly EccFullKeyData _eccKey;
        private readonly SensitiveByteArray _eccPwd;

        public InternalAttestationController(ILogger<AttestationRequestController> logger, IHttpClientFactory httpClientFactory, AttestationDatabase db, SensitiveByteArray pwdEcc, EccFullKeyData eccKey)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _db = db;
            _eccKey = eccKey;
            _eccPwd = pwdEcc;
        }


        /// <summary>
        /// Internal: Delete the pending request from the database
        /// </summary>
        /// <param name="identity"></param>
        /// <returns></returns>
        private IActionResult DeleteRequest(PunyDomainName identity)
        {
            try
            {
                var n = _db.tblAttestationRequest.Delete(identity.DomainName);

                if (n < 1)
                    return BadRequest($"No such identity found deleted");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error deleting for identity {ex.Message}");
            }

            return Ok();
        }


        /// <summary>
        /// For administrative staff only. Call this function to delete a pending request from an identity.
        /// Considering if it should be the nonce rather than the identity (IDK yet what we want to do about multiple requests,
        /// retracting requests etc)
        /// </summary>
        /// <param name="identity"></param>
        /// <returns></returns>
        [HttpGet("DeleteRequest")]
        public IActionResult GetDeleteRequest(string identity)
        {
            PunyDomainName id;

            try
            {
                id = new PunyDomainName(identity);
            }
            catch (Exception ex)
            {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            return DeleteRequest(id);
        }


        /// <summary>
        /// Based on a verified request contract from an identity, generate a list of attestations.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="requestEnvelope"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        private List<SignedEnvelope> GenerateAttestationsFromRequest(PunyDomainName identity, SignedEnvelope requestEnvelope)
        {
            if (identity.DomainName != requestEnvelope.Signatures[0].Identity.DomainName)
                throw new ArgumentException("identity and envelope mismatch, impossible");

            if (!requestEnvelope.Envelope.AdditionalInfo.ContainsKey("data"))
                throw new ArgumentException("The requestEnvelope doesn't have a data section");

            SortedDictionary<string, object>? dataObject = requestEnvelope.Envelope.AdditionalInfo["data"] as SortedDictionary<string, object>;

            //
            // Parse the additional info in the request and generate a series of attestations
            // 
            var attestationList = new List<SignedEnvelope>();

            // We always verify the fact it's a human
            try
            {
                var attestation = AttestationManagement.AttestHuman(_eccKey, _eccPwd, identity);
                attestationList.Add(attestation);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unable to attest Hooman {ex.Message}");
            }

            // Let's check for legal name
            if (dataObject!.TryGetValue(AttestationManagement.JsonKeyLegalName, out var legalNameObject))
            {
                try
                {
                    string legalName = EnvelopeData.GetValueFromJsonObject<string>(legalNameObject);
                    var attestation = AttestationManagement.AttestLegalName(_eccKey, _eccPwd, identity, legalName);
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attest LegalName {ex.Message}");
                }
            }

            // Let's check for subset legal name
            if (dataObject!.TryGetValue(AttestationManagement.JsonKeySubsetLegalName, out var subsetLegalNameObject))
            {
                try
                {
                    string subsetLegalName = EnvelopeData.GetValueFromJsonObject<string>(subsetLegalNameObject);
                    var attestation = AttestationManagement.AttestSubsetLegalName(_eccKey, _eccPwd, identity, subsetLegalName);
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attest SubsetLegalName {ex.Message}");
                }
            }

            // Let's check for nationality
            if (dataObject!.TryGetValue(AttestationManagement.JsonKeyNationality, out var nationalityObject))
            {
                try
                {
                    string nationality = EnvelopeData.GetValueFromJsonObject<string>(nationalityObject);
                    var attestation = AttestationManagement.AttestNationality(_eccKey, _eccPwd, identity, nationality);
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attest nationality {ex.Message}");
                }
            }

            // Let's check for phone
            if (dataObject!.TryGetValue(AttestationManagement.JsonKeyPhoneNumber, out var phoneObject))
            {
                try
                {
                    string phoneNumber = EnvelopeData.GetValueFromJsonObject<string>(phoneObject);
                    var attestation = AttestationManagement.AttestPhoneNumber(_eccKey, _eccPwd, identity, phoneNumber);
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attest phone {ex.Message}");
                }
            }

            // Let's check for email
            if (dataObject!.TryGetValue(AttestationManagement.JsonKeyEmailAddress, out var emailObject))
            {
                try
                {
                    string email = EnvelopeData.GetValueFromJsonObject<string>(emailObject);
                    var attestation = AttestationManagement.AttestEmailAddress(_eccKey, _eccPwd, identity, email);
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attest email {ex.Message}");
                }
            }

            // Let's check for birthdate
            if (dataObject!.TryGetValue(AttestationManagement.JsonKeyBirthdate, out var birthDateObject))
            {
                try
                {
                    string bday = EnvelopeData.GetValueFromJsonObject<string>(birthDateObject);

                    var attestation = AttestationManagement.AttestBirthdate(_eccKey, _eccPwd, identity, DateOnly.FromDateTime(DateTime.Parse(bday)));
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attest birth date {ex.Message}");
                }
            }

            // Let's check for residential address
            if (dataObject!.TryGetValue(AttestationManagement.JsonKeyResidentialAddress, out var addressObject))
            {
                try
                {
                    var addressDict = SignedEnvelope.ConvertJsonObjectToSortedDict(addressObject);
                    if (addressDict == null)
                        throw new Exception("address is null");
                    var attestation = AttestationManagement.AttestResidentialAddress(_eccKey, _eccPwd, identity, addressDict);
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attest birth date {ex.Message}");
                }
            }

            return attestationList;
        }


        /// <summary>
        /// Adminstrative staff use only.
        /// After carefully reviewing the data in an attestation request, the admininstrative staff calls this function
        /// if approved. This function will generate the attestations requested, return the to the requestor, and store
        /// records in the block chain database.
        /// Considering if identity should instead be the nonce of the request.
        /// </summary>
        /// <param name="nonce"></param>
        /// <returns></returns>
        [HttpGet("ApproveRequest")]
        public IActionResult GetApproveRequest(string nonce)
        {
            //
            // First get the request from the database
            // 
            var r = _db.tblAttestationRequest.Get(nonce);

            if (r == null)
                return BadRequest("No such request present");

            SignedEnvelope? requestEnvelope;
            try
            {
                requestEnvelope = InstructionSignedEnvelope.VerifyInstructionEnvelope(r.requestEnvelope);
            }
            catch (Exception ex)
            {
                return BadRequest($"Irrecoverable error, unable to deserialize signed envelope {ex.Message}");
            }

            List<SignedEnvelope> attestationList;

            try
            {
                attestationList = GenerateAttestationsFromRequest(requestEnvelope.Signatures[0].Identity.PunyDomain, requestEnvelope);
            }
            catch (Exception ex)
            {
                return BadRequest($"Unable to generate attestation list: {ex.Message}");
            }

            //
            // This is the JSON array of JSON
            //
            var jsonList = attestationList.Select(item => item.GetCompactSortedJson()).ToList();

            var jsonArray = JsonSerializer.Serialize(jsonList);

            //
            // Now call an identity endpoint to deliver the attested data (json array)
            //
            SimulateFrodo.DeliverAttestations(jsonArray);


            // Block chain will contain
            //
            // Option 1 - one record per attestation
            //
            // previousHash
            // identity
            // timestamp
            // attestationNonce
            // signedAttestationNonce
            // algorithm
            // publicKey
            // recordHash
            //
            // Option 2 - one record per request, attestations contain the request nonce in additionalInfo
            //
            // previousHash
            // identity
            // timestamp
            // requestNonce
            // signedRequestNonce
            // algorithm
            // publicKey
            // recordHash
            //

            using (_db.CreateCommitUnitOfWork())
            {
                //
                // Now insert the block chain records
                //


                //
                // Finally, delete the pending request
                //
                GetDeleteRequest(nonce);
            }
            return Ok();
        }

        [HttpGet("ListPendingRequests")]
        public async Task<IActionResult> GetListPendingRequests()
        {
            var r = _db.tblAttestationRequest.PagingByNonce(100, null, out var nextCursor);

            // Using LINQ to convert the list of requests to a list of identities
            var identities = r.Select(request => request.nonce).ToList();

            await Task.Delay(1);  // You might not need this delay unless you have a specific reason for it.

            return Ok(identities);
        }

    }
}