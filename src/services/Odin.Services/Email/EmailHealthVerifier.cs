using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Http;
using Odin.Services.Base;
using Odin.Services.Email.Dkim;

namespace Odin.Services.Email;

#nullable enable

/// <summary>
/// Unattended per-tenant email health checks (docs/email-keys-plan.md "Verification
/// hooks"): may touch public material and server-operational keys only, NEVER the
/// owner-locked E2E private keyring. Runs monthly (SecurityHealthCheckJob ride-along)
/// and on demand from the owner console's Email tab.
///
/// - DKIM pair proof: sign a test vector with the stored source-of-truth private key
///   and verify against the public key in the LIVE DNS TXT - actual crypto proof that
///   "the published DKIM key works", stronger than string-comparing the record.
/// - Public-key drift: the published E2E PUBLIC certificate must be identical at
///   every publication surface (WKD binary, DID keyAgreement) - drift here is the
///   precursor to silent-data-loss (mail encrypted to a key the owner cannot decrypt).
///
/// Generic public DNS + plain HTTPS only; never the PowerDNS API.
/// </summary>
public class EmailHealthVerifier(
    ILogger<EmailHealthVerifier> logger,
    TenantContext tenantContext,
    IDkimStore dkimStore,
    EmailPublicKeyService emailPublicKeyService,
    ILookupClient dnsClient,
    IDynamicHttpClientFactory httpClientFactory)
{
    private static readonly byte[] PairProofVector = "odin dkim live pair proof"u8.ToArray();

    public sealed class Result
    {
        public bool Activated { get; init; }
        public List<string> Errors { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
        public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
    }

    public async Task<Result> VerifyAsync(CancellationToken cancellationToken)
    {
        var domain = tenantContext.HostOdinId.DomainName;

        var publishedKey = await emailPublicKeyService.GetPublishedKeyAsync();
        if (publishedKey == null)
        {
            // Not activated: nothing is published, nothing to verify
            return new Result { Activated = false };
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        await VerifyDkimAsync(domain, errors, warnings, cancellationToken);
        await VerifyPublicationSurfacesAsync(domain, publishedKey, errors, warnings, cancellationToken);

        return new Result { Activated = true, Errors = errors, Warnings = warnings };
    }

    //

    private async Task VerifyDkimAsync(string domain, List<string> errors, List<string> warnings, CancellationToken cancellationToken)
    {
        if (!dkimStore.IsConfigured)
        {
            warnings.Add("Email:DkimStorageKey is not configured; DKIM pair proof skipped");
            return;
        }

        var keys = await dkimStore.GetKeysAsync(domain);
        if (keys.Count == 0)
        {
            errors.Add("Email is activated but no DKIM keys are stored");
            return;
        }

        foreach (var key in keys)
        {
            var recordName = $"{key.DnsRecordName}.{domain}";

            List<string> txtValues;
            try
            {
                var response = await dnsClient.QueryAsync(recordName, QueryType.TXT, cancellationToken: cancellationToken);
                // Long values come back 255-byte chunked; receivers concatenate
                txtValues = response.Answers.TxtRecords().Select(x => string.Concat(x.Text)).ToList();
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "DKIM TXT lookup failed for {record}", recordName);
                warnings.Add($"DKIM TXT lookup failed for '{recordName}'; cannot verify");
                continue;
            }

            var dkimValues = txtValues
                .Where(v => DkimTxtRecord.TryParse(v, out _, out _))
                .ToList();
            if (dkimValues.Count == 0)
            {
                errors.Add($"DKIM TXT record '{recordName}' is missing; outbound mail will fail DMARC once live");
                continue;
            }

            var anyPass = dkimValues.Any(value =>
            {
                DkimTxtRecord.TryParse(value, out var kTag, out var publishedPublicKey);
                if (kTag != key.KTag)
                {
                    return false;
                }

                var signature = DkimKeyGenerator.Sign(key, PairProofVector);
                return DkimKeyGenerator.Verify(key.Algorithm, publishedPublicKey, PairProofVector, signature);
            });

            if (!anyPass)
            {
                errors.Add($"DKIM pair proof FAILED for '{recordName}': the published key does not verify signatures made with the stored key");
            }
        }
    }

    private async Task VerifyPublicationSurfacesAsync(
        string domain,
        PublishedEmailPublicKey publishedKey,
        List<string> errors,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        // WKD: the served binary certificate must be the stored one
        var wkdBytes = await TryGetBytesAsync(domain, $"https://{domain}/.well-known/openpgpkey/hu/drift-check", warnings, "WKD", cancellationToken);
        if (wkdBytes != null)
        {
            try
            {
                var expected = OpenPgpKeyManagement.GetPublicCertificateBinary(publishedKey.PublicCertificateArmored);
                if (!wkdBytes.SequenceEqual(expected))
                {
                    errors.Add("Public-key drift: WKD serves a different certificate than the published one");
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "WKD drift comparison failed for {domain}", domain);
                errors.Add("Public-key drift: WKD response is not a parseable certificate");
            }
        }

        // DID: the keyAgreement JWK must derive from the stored certificate's subkey
        var didJson = await TryGetStringAsync(domain, $"https://{domain}/.well-known/did.json", warnings, "DID document", cancellationToken);
        if (didJson != null)
        {
            var expectedJwk = ExpectedKeyAgreementJwkFragment(publishedKey.PublicCertificateArmored);
            if (!didJson.Contains("keyAgreement"))
            {
                errors.Add("Public-key drift: DID document has no keyAgreement entry");
            }
            else if (expectedJwk != null && !didJson.Contains(expectedJwk))
            {
                errors.Add("Public-key drift: DID keyAgreement key does not match the published certificate");
            }
        }
    }

    // The x coordinate is unique per key and appears verbatim in the JWK - a robust
    // containment probe without coupling to the DID document's JSON layout
    private static string? ExpectedKeyAgreementJwkFragment(string publicCertificateArmored)
    {
        try
        {
            var spkiDer = OpenPgpKeyManagement.GetEncryptionSubkeySpkiDer(publicCertificateArmored);
            var jwk = new Odin.Core.Cryptography.Data.EccPublicKeyData { publicKey = spkiDer }.PublicKeyJwk();
            var parsed = System.Text.Json.JsonDocument.Parse(jwk);
            return parsed.RootElement.GetProperty("x").GetString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]?> TryGetBytesAsync(string domain, string url, List<string> warnings, string label, CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(domain);
            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"{label} responded {(int)response.StatusCode} during drift check");
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "{label} drift check request failed for {domain}", label, domain);
            warnings.Add($"{label} unreachable during drift check");
            return null;
        }
    }

    private async Task<string?> TryGetStringAsync(string domain, string url, List<string> warnings, string label, CancellationToken cancellationToken)
    {
        var bytes = await TryGetBytesAsync(domain, url, warnings, label, cancellationToken);
        return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }
}
