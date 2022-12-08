using Certes;
using Certes.Acme;
using Dawn;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;

namespace Youverse.Provisioning.Services.Certificate
{
    public class LetsEncryptCertificateService : ICertificateService
    {
        private readonly ProvisioningConfig _config;
        private readonly CertificateOrderList _orderList;
        private const string BuiltinAccountPemPath = "builtin";
        private const string BuiltinAccountPemFilename = "letsencrypt.pem";
        private readonly ILogger<LetsEncryptCertificateService> _logger;

        public LetsEncryptCertificateService(ProvisioningConfig config, CertificateOrderList orderList, ILogger<LetsEncryptCertificateService> logger)
        {
            _config = config;
            _orderList = orderList;
            _logger = logger;
        }

        public async Task<Guid> PlaceOrder(CertificateOrder order)
        {
            _logger.LogInformation($"Placing order using acct:{order.Account.Email}");
            var accountPem = await GetOrCreateAccountPem(order);
            var accountKey = KeyFactory.FromPem(accountPem);
            var acme = new AcmeContext(GetServer(), accountKey);

            var acmeOrder = await acme.NewOrder(new[] {order.Domain});

            var orderId = Guid.NewGuid();

            //placing the order in this list will ensure the background job watches it.
            _orderList.AddCertificateOrder(orderId, new PendingCertificateOrder()
            {
                AccountPem = accountPem,
                OrderUri = acmeOrder.Location.ToString(),
                CertificateOrder = order
            });

            _logger.LogInformation($"order added to orderList");
            return orderId;
        }

        public Task<bool> IsCertificateIsReady(Guid orderId)
        {
            return Task.FromResult(_orderList.IsCertificateReady(orderId));
        }

        public Task<CertificateOrderStatus> GetCertificateOrderStatus(Guid orderId)
        {
            var order = _orderList.Get(orderId);
            if (order == null)
            {
                throw new Exception($"Unknown order {orderId}");
            }
            
            return Task.FromResult(order.Status);
        }

        public async Task<CertificatePemContent> GenerateCertificate(Guid orderId)
        {
            var pendingOrder = _orderList.GetAndRemove(orderId);
            var accountKey = KeyFactory.FromPem(pendingOrder.AccountPem);
            var acme = new AcmeContext(GetServer(), accountKey);
            
            var acmeOrder = acme.Order(new Uri(pendingOrder.OrderUri));
            
            var privateKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
            string domain = pendingOrder.CertificateOrder.Domain;

            var csr = new CsrInfo()
            {
                CountryName = pendingOrder.CertificateOrder.Account.CertificateSigningRequest.CountryName,
                State = pendingOrder.CertificateOrder.Account.CertificateSigningRequest.State,
                Locality = pendingOrder.CertificateOrder.Account.CertificateSigningRequest.Locality,
                Organization = pendingOrder.CertificateOrder.Account.CertificateSigningRequest.Organization,
                OrganizationUnit = pendingOrder.CertificateOrder.Account.CertificateSigningRequest.OrganizationUnit,
                CommonName = domain
            };

            //TODO: we could maybe let the user upload their own private key??
            var cert = await acmeOrder.Generate(csr, privateKey);
            
            CertificatePemContent certPemContent = new CertificatePemContent()
            {
                PublicKeyCertificate = cert.Certificate.ToPem(),
                PrivateKey = privateKey.ToPem(),
                FullChain = cert.ToPem(),
            };

            return certPemContent;
        }

        private async Task<string> GetOrCreateAccountPem(CertificateOrder order)
        {
            if (order.UseBuiltInAccount)
            {
                string path = PathUtil.OsIfy(Path.Combine(_config.CertificateRootPath, BuiltinAccountPemPath));
                string filePath = PathUtil.OsIfy(Path.Combine(path, BuiltinAccountPemFilename));

                Directory.CreateDirectory(path);

                string pem = await File.ReadAllTextAsync(filePath);
                if (!string.IsNullOrEmpty(pem) && !string.IsNullOrWhiteSpace(pem))
                {
                    pem = await CreateNewAccount(_config.CertificateAuthorityAssociatedEmail);
                    await File.WriteAllTextAsync(filePath, pem);
                }

                _logger.LogInformation($"Using built-in account");
                
                return pem;
            }

            return await CreateNewAccount(order.Account.Email);
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

        private Uri GetServer()
        {
            return _config.UseCertificateAuthorityProductionServers
                ? WellKnownServers.LetsEncryptV2
                : WellKnownServers.LetsEncryptStagingV2;
        }
    }
}