// Next, make a "simulate" endpoint I can easily call to being the simulation
// Also, make a ledgerVerify


using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Storage.SQLite.AttestationDatabase;
using Odin.Core.Util;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Cryptography.Data;
using System.Text.Json;
using System.Security.Cryptography.Xml;
using System;

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
        /// This simulates that an identity requests it's signature key to be added to the block chain
        /// </summary>
        /// <returns></returns>
       [HttpGet("Simulator")]
        public async Task<IActionResult> GetSimulator()
        {
            await Task.Delay(0);
            return Ok();
        }


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

        private static T GetValueFromJsonElement<T>(SortedDictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is JsonElement element)
                {
                    if (typeof(T) == typeof(string))
                    {
                        return (T)(object)element.GetString();
                    }
                    else if (typeof(T) == typeof(int))
                    {
                        return (T)(object)element.GetInt32();
                    }
                    // Add more types as needed...
                }
            }

            throw new KeyNotFoundException($"Key '{key}' not found in the dictionary or value is not of the expected type.");
        }



        [HttpGet("ApproveRequest")]
        public IActionResult GetApproveRequest(string identity)
        {
            PunyDomainName id;

            try
            {
                id = new PunyDomainName(identity);
            }
            catch (Exception ex) {
                return BadRequest($"Invalid identity {ex.Message}");
            }

            //
            // First get the request from the database
            // 
            var r = _db.tblAttestationRequest.Get(id.DomainName);

            if (r == null)
                return BadRequest("No such request present");

            SignedEnvelope? requestEnvelope;
            try
            {
                requestEnvelope = RequestSignedEnvelope.VerifyRequestEnvelope(r.requestEnvelope);
            }
            catch (Exception ex)
            {
                return BadRequest($"Irrecoverable error, unable to deserialize signed envelope {ex.Message}");
            }

            if (!requestEnvelope.Envelope.AdditionalInfo.ContainsKey("data"))
                return BadRequest("The request doesn't have a data section");


            SortedDictionary<string, object>? dataObject = requestEnvelope.Envelope.AdditionalInfo["data"] as SortedDictionary<string, object>;

            //
            // Parse the additional info in the request and generate a series of attestations
            // 
            var attestationList = new List<SignedEnvelope>();

            // We always verify the fact it's a human
            try
            {
                var attestation = AttestationManagement.AttestHuman(_eccKey, _eccPwd, id);
                attestationList.Add(attestation);
            }
            catch(Exception ex)
            {
                return BadRequest($"Unable to attest Hooman {ex.Message}");
            }

            // Let's check for legal name

            if (dataObject!.TryGetValue("LegalName", out var legalNameObject))
            {
                try
                {
                    string legalName = GetValueFromJsonElement<string>(dataObject, "LegalName");
                    var attestation = AttestationManagement.AttestLegalName(_eccKey, _eccPwd, id, legalName);
                    attestationList.Add(attestation);
                }
                catch (Exception ex)
                {
                    return BadRequest($"Unable to attest LegalName {ex.Message}");
                }
            }

            //
            // This is the JSON array of JSON
            //
            var jsonList = attestationList.Select(item => item.GetCompactSortedJson()).ToList();

            var jsonArray = JsonSerializer.Serialize(jsonList);

            //
            // Now call identity and deliver the attested data
            //


            //
            // Now insert the block chain records
            //

            //
            // Now delete the request from the database
            //

            return Ok();
        }

        /// <summary>
        /// 010. Client calls Server.RegisterKey(identity, tempcode)
        /// 020. Server calls Client.GetPublicKey() to get signature key
        /// 030. Server calls Client.SignNonce(tempcode, previousHash)
        /// 033. Client returns signedNonce
        /// 037. Server verifies signature
        /// 040. Server serializes each request from hereon in semaphore
        /// 050. Server fetches last row
        /// 060. Server calculates new block chain row
        /// 070. Server verifies row data (hash and maybe timestamp)
        /// 080. Server writes row
        /// 090. Server frees semaphore
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="tempCode"></param>
        /// <returns></returns>
        [HttpGet("Register")]
        public async Task<IActionResult> GetRegister(string identity, string tempCode)
        {
            PunyDomainName domain;
            try
            {
                domain = new PunyDomainName(identity);
            }
            catch (Exception)
            {
                return BadRequest("Invalid identity, not a proper puny-code domain name");
            }

            if ((tempCode.Length < 16) || (tempCode.Length > 64))
            {
                return BadRequest("Invalid temp-code, needs to be [16..64] characters");
            }

            await Task.Delay(1);

            return Ok("OK");
        }

    }
}