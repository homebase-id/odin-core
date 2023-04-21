using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Dawn;
using Microsoft.Extensions.Logging;
using Serilog;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage;
using Youverse.Core.Util;
using Directory = System.IO.Directory;

namespace Youverse.Core.Services.Certificate.Renewal
{
    public class LetsEncryptTenantCertificateRenewalService : ITenantCertificateRenewalService
    {
        private static readonly GuidId PendingCertOrderKeyForCdnDomain = GuidId.FromString("cdn_ssl_pending_cert_order");
        private static readonly GuidId PendingCertOrderKeyForApiDomain = GuidId.FromString("api_ssl_pending_cert_order");

        private const string BuiltinAccountPemPath = "ca_acct";
        private const string BuiltinAccountPemFilename = "letsencrypt.pem";

        private readonly ILogger<LetsEncryptTenantCertificateRenewalService> _logger;
        private readonly TenantContext _tenantContext;
        private readonly ITenantCertificateService _tenantCertificateService;
        private readonly PendingCertificateOrderListService _certificateOrderList;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public LetsEncryptTenantCertificateRenewalService(ILogger<LetsEncryptTenantCertificateRenewalService> logger, TenantContext tenantContext,
            ITenantCertificateService tenantCertificateService, PendingCertificateOrderListService certificateOrderList, TenantSystemStorage tenantSystemStorage)
        {
            _logger = logger;
            _tenantContext = tenantContext;
            _tenantCertificateService = tenantCertificateService;
            _certificateOrderList = certificateOrderList;
            _tenantSystemStorage = tenantSystemStorage;
        }

        public async Task<CertificateOrderStatus> GenerateCertificateIfReady()
        {
            string domain = _tenantContext.HostOdinId.DomainName;

            var record = _tenantSystemStorage.SingleKeyValueStorage.Get<PendingCertificateOrder>(GuidId.FromString(domain));
            var acme = new AcmeContext(GetServer(), KeyFactory.FromPem(record.AccountPem));

            var acmeOrder = acme.Order(new Uri(record.LocationUri));
            var auth = await acmeOrder.Authorization(record.Domain, IdentifierType.Dns);
            var httpFileChallenge = await auth.Http();

            var result = await httpFileChallenge.Validate();
            
            if (result.Status is ChallengeStatus.Valid)
            {
                _logger.LogInformation($"Order Id has been validated: [{record.LocationUri}]");
                var pemContent = await GenerateCertificate(domain, acmeOrder);
                await _tenantCertificateService.SaveSslCertificate(record.RegistryId, domain, pemContent);
                DeleteAuthFile(_tenantContext.TempDataRoot, httpFileChallenge.Token);
                return CertificateOrderStatus.CertificateUpdateComplete;
            }

            if (result.Status == ChallengeStatus.Invalid)
            {
                _logger.LogInformation($"Order is invalid: [{record.LocationUri}]");

                //TODO: remove record and ??? what do do here?
                //TODO:L handle result.Error here
                _logger.LogWarning($"Identifier: {result.Error.Identifier}");
                _logger.LogWarning($"Type: {result.Error.Type}");
                _logger.LogWarning($"Detail: {result.Error.Detail}");
                //TODO: render result.Error.Subproblems

                DeleteAuthFile(_tenantContext.TempDataRoot, httpFileChallenge.Token);
                return CertificateOrderStatus.VerificationFailed;
            }

            if (result.Status is ChallengeStatus.Pending or ChallengeStatus.Processing)
            {
                //return to caller a message?
                _logger.LogWarning($"Order is still {result.Status} [{record.LocationUri}]");
                return CertificateOrderStatus.AwaitingVerification;
            }

            throw new YouverseSystemException("Unhandled certificate ChallengeStatus");
        }

        public async Task EnsureCertificatesAreValid(bool force = false)
        {
            var domainsCerts = await _tenantCertificateService.GetIdentitiesRequiringNewCertificate(force);
            foreach (var domainCert in domainsCerts)
            {
                await this.PlaceOrder(domainCert);
            }
        }

        public string GetAuthResponse(string token)
        {
            var certificateAuth = ReadAuthFile(_tenantContext.TempDataRoot, token);
            if (null == certificateAuth || (string.IsNullOrEmpty(certificateAuth.Auth) || string.IsNullOrWhiteSpace(certificateAuth.Auth)))
            {
                return null;
            }

            return certificateAuth.Auth;
        }

        private async Task PlaceOrder(IdentityCertificateDefinition identityCert)
        {
            string domain = identityCert.Domain;
            var allDomains = new List<string> { domain };
            // allDomains.AddRange(identityCert.AlternativeNames ?? new List<string>());

            var accountPem = await GetOrCreateAccountPem();
            var accountKey = KeyFactory.FromPem(accountPem);
            var acme = new AcmeContext(GetServer(), accountKey);
            
            _logger.LogInformation($"Placing order for certificate for domain: [{identityCert.Domain}]");

            var acmeOrder = await acme.NewOrder(allDomains);

            var auth = await acmeOrder.Authorization(domain, IdentifierType.Dns);
            var httpFileChallenge = await auth.Http();

            WriteAuthFile(_tenantContext.TempDataRoot, new CertificateAuth()
            {
                Token = httpFileChallenge.Token,
                Auth = httpFileChallenge.KeyAuthz
            });
            
            _logger.LogInformation($"Check: http://{identityCert.Domain}/.well-known/acme-challenge/{httpFileChallenge.Token}");
            
            var pco = new PendingCertificateOrder()
            {
                RegistryId = _tenantContext.DotYouRegistryId,
                AccountPem = accountPem,
                Domain = (OdinId)domain,
                LocationUri = acmeOrder.Location.ToString()
            };
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(GuidId.FromString(domain), pco);
            
            //there's a background job that will do this as well but let's kick
            //off a validate just in case we can knock this out quickly
            System.Threading.Thread.Sleep(1000);
            var status = await GenerateCertificateIfReady();
            if (status == CertificateOrderStatus.CertificateUpdateComplete)
            {
                _logger.LogInformation($"Certificate completed during initial call to place order");
                return;
            }
            
            //add to list so the background job can call it.
            _certificateOrderList.Add((OdinId)domain);
            _logger.LogInformation($"Certificate order placed and background job will check for validation then continue creation process");
        }

        private async Task<CertificatePemContent> GenerateCertificate(string domain, IOrderContext acmeOrder)
        {
            var privateKey = this.GeneratePrivateKey();

            var cfg = _tenantContext.CertificateRenewalConfig.CertificateSigningRequest;

            var csr = new CsrInfo()
            {
                CountryName = cfg.CountryName,
                State = cfg.State,
                Locality = cfg.Locality,
                Organization = cfg.Organization,
                OrganizationUnit = cfg.OrganizationUnit,
                CommonName = domain
            };

            //TODO: we could maybe let the user upload their own private key??
            _logger.LogInformation($"Calling generate {csr.CommonName}");
            // var cert = await acmeOrder.Generate(csr, privateKey);

            var context = acmeOrder;
            var order = await context.Resource();
            _logger.LogInformation($"Reported status: {order.Status}");
            if (order.Status != OrderStatus.Ready && // draft-11
                order.Status != OrderStatus.Pending) // pre draft-11
            {
                throw new AcmeException($"ErrorInvalidOrderStatusForFinalize: {order.Status}");
            }

            int retryCount = 3;
            _logger.LogInformation($"Calling finalize");

            order = await context.Finalize(csr, privateKey);
            
            while (order.Status == OrderStatus.Processing && retryCount-- > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(context.RetryAfter, 1)));
                _logger.LogInformation($"finalize attempt #: {retryCount}");

                order = await context.Resource();
                _logger.LogInformation($"Reported status: {order.Status}");
            }

            if (order.Status != OrderStatus.Valid)
            {
                throw new AcmeException("ErrorFinalizeFailed");
            }
            
            _logger.LogInformation($"calling download {csr.CommonName}");
            var cert = await context.Download(null);
            
            _logger.LogInformation($"Generate complete {csr.CommonName}");

            CertificatePemContent certPemContent = new CertificatePemContent()
            {
                PublicKeyCertificate = cert.Certificate.ToPem(),
                PrivateKey = privateKey.ToPem(),
                FullChain = cert.ToPem(),
            };

            return certPemContent;
        }

        private async Task<string> GetOrCreateAccountPem()
        {
            string path = PathUtil.OsIfy(Path.Combine(_tenantContext.SslRoot, BuiltinAccountPemPath));
            string filePath = PathUtil.OsIfy(Path.Combine(path, BuiltinAccountPemFilename));

            Directory.CreateDirectory(path);

            string pem = "";
            if (File.Exists(filePath))
            {
                pem = await File.ReadAllTextAsync(filePath);
                if (!string.IsNullOrEmpty(pem) && !string.IsNullOrWhiteSpace(pem))
                {
                    _logger.LogInformation($"Using built-in account");
                    return pem;
                }
            }

            string email = _tenantContext.CertificateRenewalConfig.CertificateAuthorityAssociatedEmail;
            pem = await CreateNewAccount(email);
            await File.WriteAllTextAsync(filePath, pem);

            return pem;
        }

        private async Task<string> CreateNewAccount(string email)
        {
            string errorMessage = "Email address required when creating a LetsEncrypt account";
            Guard.Argument(email, nameof(email)).NotNull(errorMessage).NotEmpty(errorMessage);

            _logger.LogInformation($"Creating new account");

            const bool agreeTos = true;
            var server = GetServer();
            var acme = new AcmeContext(server);
            var acmeAccount = await acme.NewAccount(email, agreeTos);
            var pemKey = acme.AccountKey.ToPem();
            return pemKey;
        }

        private IKey GeneratePrivateKey()
        {
            return KeyFactory.NewKey(KeyAlgorithm.RS256);
        }

        public static void WriteAuthFile(string path, CertificateAuth certificateAuth)
        {
            var finalPath = PathUtil.OsIfy(Path.Combine(path, certificateAuth.Token));
            File.WriteAllText(finalPath, certificateAuth.Auth);
        }

        public static CertificateAuth ReadAuthFile(string path, string token)
        {
            string fullPath = PathUtil.OsIfy(Path.Combine(path, token));
            Log.Information($"CertificateAuth->Reading token at path: [{fullPath}]");
            if (File.Exists(fullPath))
            {
                return new CertificateAuth()
                {
                    Token = token,
                    Auth = File.ReadAllText(fullPath)
                };
            }

            Log.Error($"CertificateAuth->File not found or inaccessible.");
            return null;
        }

        public static void DeleteAuthFile(string path, string token, bool ignoreErrors = true)
        {
            try
            {
                string fullPath = PathUtil.OsIfy(Path.Combine(path, token));
                File.Delete(fullPath);
            }
            catch
            {
                if (!ignoreErrors)
                {
                    throw;
                }
            }
        }

        private Uri GetServer()
        {
            Log.Information($"UseCertificateAuthorityProductionServers: {_tenantContext.CertificateRenewalConfig.UseCertificateAuthorityProductionServers}");
            return _tenantContext.CertificateRenewalConfig.UseCertificateAuthorityProductionServers
                ? WellKnownServers.LetsEncryptV2
                : WellKnownServers.LetsEncryptStagingV2;
        }
    }
}