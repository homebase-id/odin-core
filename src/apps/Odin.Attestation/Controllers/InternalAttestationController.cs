// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Storage.SQLite.AttestationDatabase;
using Odin.Core.Util;

namespace Odin.Attestation.Controllers
{
    [ApiController]
    [Route("internal")]
    public class InternalAttestationController : ControllerBase
    {
        private readonly ILogger<AttestationRequestController> _logger;
        private readonly AttestationDatabase _db;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        // private readonly bool _simulate = true;
        private readonly EccFullKeyData _eccKey;
        private readonly SensitiveByteArray _eccPwd;

        public InternalAttestationController(ILogger<AttestationRequestController> logger, AttestationDatabase db, SensitiveByteArray pwdEcc, EccFullKeyData eccKey)
        {
            _logger = logger;
            _db = db;
            _eccKey = eccKey;
            _eccPwd = pwdEcc;
        }


        /// <summary>
        /// Internal: Delete the pending request from the database
        /// </summary>
        /// <param name="identity"></param>
        /// <returns></returns>
        private async Task<ActionResult> DeleteRequest(string nonceBase64)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                try
                {
                    var n = await _db.tblAttestationRequest.DeleteAsync(conn, nonceBase64);

                    if (n < 1)
                        return BadRequest($"No such record found");
                }
                catch (Exception ex)
                {
                    return BadRequest($"Error deleting for identity {ex.Message}");
                }

                return Ok();
            }
        }

        /// <summary>
        /// For administrative staff only. Call this function to delete a pending request from an identity.
        /// Considering if it should be the nonce rather than the identity (IDK yet what we want to do about multiple requests,
        /// retracting requests etc)
        /// </summary>
        /// <param name="identity"></param>
        /// <returns></returns>
        [HttpGet("DeleteRequest")]
        public async Task<ActionResult> GetDeleteRequest(string attestationIdBase64)
        {
            return await DeleteRequest(attestationIdBase64);
        }


        [HttpGet("InvalidateAttestation")]
        public async Task<ActionResult> GetInvalidateAttestation(string attestationIdBase64)
        {
            byte[] attestationId;

            try
            {
                attestationId = Convert.FromBase64String(attestationIdBase64);
            }
            catch (Exception)
            {
                return BadRequest("Invalid attestationIdBase64");
            }

            using (var conn = _db.CreateDisposableConnection())
            {
                var r = await _db.tblAttestationStatus.GetAsync(conn, attestationId);

                if (r == null)
                    return NotFound();

                if (r.status != 1)
                    return Conflict();

                r.status = 0;

                await _db.tblAttestationStatus.UpdateAsync(conn, r);

                await Task.Delay(1);

                return Ok();
            }
        }




        /// <summary>
        /// Based on a verified request contract from an identity, generate a list of attestations.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="requestEnvelope"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        private List<SignedEnvelope> GenerateAttestationsFromRequest(AsciiDomainName identity, SignedEnvelope requestEnvelope)
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
                var attestation = AttestationManagement.AttestHuman(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce);
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
                    var attestation = AttestationManagement.AttestLegalName(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce, legalName);
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
                    var attestation = AttestationManagement.AttestSubsetLegalName(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce, subsetLegalName);
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
                    var attestation = AttestationManagement.AttestNationality(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce, nationality);
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
                    var attestation = AttestationManagement.AttestPhoneNumber(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce, phoneNumber);
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
                    var attestation = AttestationManagement.AttestEmailAddress(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce, email);
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

                    var attestation = AttestationManagement.AttestBirthdate(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce, DateOnly.FromDateTime(DateTime.Parse(bday)));
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
                    var attestation = AttestationManagement.AttestResidentialAddress(_eccKey, _eccPwd, identity, requestEnvelope.Envelope.ContentNonce, addressDict);
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
        /// if it is approved. This function will generate the attestations requested, return the to the requestor, and store
        /// records in the block chain database.
        /// Considering if identity should instead be the nonce of the request.
        /// </summary>
        /// <param name="attestationIdBase64"></param>
        /// <returns></returns>
        [HttpGet("ApproveRequest")]
        public async Task<ActionResult> GetApproveRequest(string attestationIdBase64)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                //
                // First get the request from the database
                // 
                var r = await _db.tblAttestationRequest.GetAsync(conn, attestationIdBase64);

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
                    attestationList = GenerateAttestationsFromRequest(requestEnvelope.Signatures[0].Identity.AsciiDomain, requestEnvelope);
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
                // In return we get a signature of the Envelope.contentNonce for each attestation provided
                //
                if (attestationList[0].Envelope.AdditionalInfo.TryGetValue("attestationId", out var valueObject) == false)
                    throw new Exception("attestationId not present in additionalInfo");

                if (valueObject == null)
                    throw new Exception("attestationId null in additionalInfo");

                string? attestationIdCopyBase64 = valueObject.ToString();

                if (attestationIdCopyBase64 == null)
                    throw new Exception("attestationId conversion null in additionalInfo");

                if (attestationIdCopyBase64 != attestationIdBase64)
                    throw new Exception("Impossible attestation id mismatch");

                //
                // Now we deliver the attestation records to the requestor.
                //
                // The goal here must be to have as much on the client code as possible, as little on the server
                // as possible. So perhaps the owner client fetches the attested data. In that case all we need 
                // do here is somehow tell the server that we have some data it can fetch. 
                // It opens the question if we have a generic owner API for stuff like this, e.g. 
                //    RaiseEvent(id, message)
                // so in this example, it might be RaiseEvent(attestationId, "Your attestations are ready to be delivered")
                // Alternately, I suppose we could deliver them:
                //    RaiseEvent(id, message, data), i.e. RaiseEvent(attestationId, jsonList, "Here are your attestations.")
                // (That's probably less of an event)

                SimulateFrodo.DeliverAttestations(attestationIdBase64, jsonArray);

                // 
                // Now store it in the database
                //

                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    //
                    // Now we are fully ready to insert the block chain records, we have all the data needed
                    //

                    var record = new AttestationStatusRecord() { attestationId = Convert.FromBase64String(attestationIdBase64), status = 1 };
                    await _db.tblAttestationStatus.InsertAsync(conn, record);

                    //
                    // Finally, delete the pending request
                    //
                    await GetDeleteRequest(attestationIdBase64);
                });

                return Ok();
            }
        }

        [HttpGet("ListPendingRequests")]
        public async Task<ActionResult> GetListPendingRequests()
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                var (r, nextCursor) = await _db.tblAttestationRequest.PagingByAttestationIdAsync(conn, 100, null);

                // Using LINQ to convert the list of requests to a list of identities
                var identities = r.Select(request => request.attestationId).ToList();

                await Task.Delay(1);  // You might not need this delay unless you have a specific reason for it.

                return Ok(identities);
            }
        }
    }
}