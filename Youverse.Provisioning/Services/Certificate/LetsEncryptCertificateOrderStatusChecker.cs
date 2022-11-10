using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace Youverse.Provisioning.Services.Certificate
{
    /// <summary>
    /// Background process that checks the status of certificate orders
    /// </summary>
    public class LetsEncryptCertificateOrderStatusChecker : BackgroundService
    {
        private readonly ILogger<LetsEncryptCertificateOrderStatusChecker> _logger;
        private readonly ProvisioningConfig _config;

        private readonly CertificateOrderList _orderList;
        private readonly Dictionary<Guid, IChallengeContext> _openChallenges;

        public LetsEncryptCertificateOrderStatusChecker(ILogger<LetsEncryptCertificateOrderStatusChecker> logger, CertificateOrderList orderList, ProvisioningConfig config)
        {
            _logger = logger;
            _orderList = orderList;
            _config = config;
            _openChallenges = new Dictionary<Guid, IChallengeContext>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //Note: this line is crucial to allow the rest of the startup
            //process to complete before we run this background service
            //https://stackoverflow.com/questions/61866319/start-ihostedservice-after-configure
            await Task.Yield();

            Console.WriteLine("CertificateOrderStatusChecker is starting");

            while (!stoppingToken.IsCancellationRequested)
            {

                await SendOrders();
                Thread.Sleep(500);
                await CheckVerifications();
            }

            Console.WriteLine("CertificateOrderStatusChecker is shutting down.");
            _logger.LogDebug("CertificateOrderStatusChecker is shutting down.");
        }

        private async Task SendOrders()
        {
            var orders = _orderList.GetOrders(CertificateOrderStatus.AwaitingOrderPlacement);
            foreach (var orderEntry in orders)
            {
                var pendingOrder = orderEntry.Value;

                Console.WriteLine($"Configuring order for {pendingOrder.OrderUri}");

                var accountKey = KeyFactory.FromPem(pendingOrder.AccountPem);
                var acme = new AcmeContext(GetServer(), accountKey);
                var acmeOrder = acme.Order(new Uri(pendingOrder.OrderUri));

                var auth = await acmeOrder.Authorization(pendingOrder.CertificateOrder.Domain, IdentifierType.Dns);
                var httpFileChallenge = await auth.Http();

                //make the token available to the certificate challenge endpoint.
                CertificateAuthFile.Write(_config.CertificateChallengeTokenPath, new CertificateAuth()
                {
                    Token = httpFileChallenge.Token,
                    Auth = httpFileChallenge.KeyAuthz
                });

                Console.WriteLine($"Challenge ready for token [{httpFileChallenge.Token}]");

                _orderList.MarkAwaitingVerification(orderEntry.Key);
                _openChallenges.Add(orderEntry.Key, httpFileChallenge);
            }
        }

        private async Task CheckVerifications()
        {
            var orders = _orderList.GetOrders(CertificateOrderStatus.AwaitingVerification);
            foreach (var orderEntry in orders)
            {
                Console.WriteLine($"Checking verification for [{orderEntry.Value.OrderUri}]");

                if (_openChallenges.TryGetValue(orderEntry.Key, out var httpFileChallenge))
                {
                    var result = await httpFileChallenge.Validate();
                    if (result.Status is ChallengeStatus.Valid)
                    {
                        Console.WriteLine($"Order id has been validated: [{orderEntry.Value.OrderUri}]");
                        _orderList.MarkVerified(orderEntry.Key);
                        CertificateAuthFile.Delete(_config.CertificateChallengeTokenPath, httpFileChallenge.Token);
                    }

                    if (result.Status == ChallengeStatus.Invalid)
                    {
                        Console.WriteLine($"Order is invalid: [{orderEntry.Value.OrderUri}]");
                        _orderList.MarkVerificationFailed(orderEntry.Key);

                        //TODO:L handle result.Error here
                        Console.WriteLine($"Identifier: {result.Error.Identifier}");
                        Console.WriteLine($"Type: {result.Error.Type}");
                        Console.WriteLine($"Detail: {result.Error.Detail}");
                        //TODO: render result.Error.Subproblems

                        CertificateAuthFile.Delete(_config.CertificateChallengeTokenPath, httpFileChallenge.Token);
                    }

                    if (result.Status is ChallengeStatus.Pending or ChallengeStatus.Processing)
                    {
                        Console.WriteLine($"Order is still {result.Status} [{orderEntry.Value.OrderUri}]");
                    }
                }
            }
        }

        private Uri GetServer()
        {
            return _config.UseCertificateAuthorityProductionServers
                ? WellKnownServers.LetsEncryptV2
                : WellKnownServers.LetsEncryptStagingV2;
        }
    }
}