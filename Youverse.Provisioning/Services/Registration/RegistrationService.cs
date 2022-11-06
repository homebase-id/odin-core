using Dawn;
using Youverse.Core;
using Youverse.Core.Util;
using Youverse.Provisioning.Controllers;
using Youverse.Provisioning.Services.Certificate;
using Youverse.Provisioning.Services.Registry;

namespace Youverse.Provisioning.Services.Registration
{
    public class RegistrationService : IRegistrationService, IDisposable
    {
        private readonly ILogger<RegistrationService> _logger;
        private readonly IIdentityRegistry _registry;
        private readonly ProvisioningConfig _config;
        private readonly LiteDBSingleCollectionStorage<Reservation> _reservationStorage;
        private readonly LiteDBSingleCollectionStorage<PendingRegistration> _pendingRegistrationStorage;
        private readonly ICertificateService _certificateService;

        public RegistrationService(ILogger<RegistrationService> logger, IIdentityRegistry registry, ProvisioningConfig config, ICertificateService certificateService)
        {
            _logger = logger;
            _registry = registry;
            _config = config;
            _certificateService = certificateService;

            const string reservationsCollection = "Reservations";
            _reservationStorage = new LiteDBSingleCollectionStorage<Reservation>(
                _logger,
                Path.Combine(_config.DataRootPath, reservationsCollection),
                reservationsCollection);

            _reservationStorage.EnsureIndex(key => key.DomainKey, true);

            const string pendingRegistrationsCollection = "PendingRegistrations";
            _pendingRegistrationStorage = new LiteDBSingleCollectionStorage<PendingRegistration>(
                _logger,
                Path.Combine(config.DataRootPath, pendingRegistrationsCollection),
                pendingRegistrationsCollection);
        }

        public async Task<Guid> StartRegistration(RegistrationInfo registrationInfo)
        {
            _logger.LogInformation($"Starting Registration:{registrationInfo.ReservationId}");

            Guard.Argument(registrationInfo, nameof(registrationInfo)).NotNull();
            Guard.Argument(registrationInfo.ReservationId, nameof(registrationInfo.ReservationId)).NotEqual(Guid.Empty);

            var reservation = await _reservationStorage.Get(registrationInfo.ReservationId);
            if (IsReservationValid(reservation) == false)
            {
                _logger.LogInformation($"Invalid Reservation:{registrationInfo.ReservationId}");
                throw new Exception("Reservation not valid");
            }

            //Note: its crucial the common name is the domain name
            var order = PrepareOrder(registrationInfo, reservation);
            order.Account.CertificateSigningRequest.CommonName = reservation.Domain;

            var orderId = await _certificateService.PlaceOrder(order);

            var registration = new PendingRegistration()
            {
                Id = Guid.NewGuid(),
                Domain = reservation.Domain,
                CreatedTimestamp = UnixTimeUtc.Now(),
                IsCertificateManaged = registrationInfo.RequestedManagedCertificate,
                Status = RegistrationStatus.AwaitingCertificate,
                Order = order,
                OrderId = orderId,
                Reservation = reservation
            };

            await _pendingRegistrationStorage.Save(registration);
            //delete the reservation because we copied it to the pending registration and rule like reservation expiration no longer apply
            await _reservationStorage.Delete(registrationInfo.ReservationId);

            _logger.LogInformation($"Registration record saved:{registrationInfo.ReservationId}");
            return registration.Id;
        }

        public async Task<RegistrationStatus> GetRegistrationStatus(Guid pendingRegistrationId)
        {
            var reg = await _pendingRegistrationStorage.Get(pendingRegistrationId);

            if (null == reg)
            {
                throw new Exception($"Cannot find pending registration with id {pendingRegistrationId}");
            }

            //map to a registration status since registration can have more steps than just a certificate
            var status = await _certificateService.GetCertificateOrderStatus(reg.OrderId);

            switch (status)
            {
                case CertificateOrderStatus.Verified:
                    return RegistrationStatus.ReadyToFinalize;

                case CertificateOrderStatus.VerificationFailed:
                    return RegistrationStatus.CertificateFailed;

                case CertificateOrderStatus.AwaitingVerification:
                case CertificateOrderStatus.AwaitingOrderPlacement:
                default:
                    return RegistrationStatus.AwaitingCertificate;
            }
        }

        public async Task<object> FinalizeRegistration(Guid pendingRegistrationId)
        {
            var reg = await _pendingRegistrationStorage.Get(pendingRegistrationId);
            var isReady = await _certificateService.IsCertificateIsReady(reg.OrderId);
            if (!isReady)
            {
                throw new Exception("Certificate is not ready.  See GetRegistrationStatus if you want to create a polling mechanism");
            }

            var certContent = await _certificateService.GenerateCertificate(reg.OrderId);

            string envRoot = _config.CertificateRootPath;
            string root = Path.Combine(envRoot, reg.Domain);

            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            Console.WriteLine($"Writing certificates to path [{root}]");
            var identReg = new IdentityRegistration()
            {
                Id = Guid.NewGuid(),
                DomainName = reg.Domain,
                IsCertificateManaged = reg.IsCertificateManaged,
                CertificateRenewalInfo = new CertificateRenewalInfo()
                {
                    CreatedTimestamp = UnixTimeUtc.Now(),
                    CertificateSigningRequest = reg.Order.Account.CertificateSigningRequest
                },

                PrivateKeyRelativePath = Path.Combine(root, "private.key"),
                PublicKeyCertificateRelativePath = Path.Combine(root, "certificate.cer"),
                FullChainCertificateRelativePath = Path.Combine(root, "fullchain.cer")
            };

            await File.WriteAllTextAsync(identReg.PublicKeyCertificateRelativePath, certContent.PublicKeyCertificate);
            await File.WriteAllTextAsync(identReg.PrivateKeyRelativePath, certContent.PrivateKey);
            await File.WriteAllTextAsync(identReg.FullChainCertificateRelativePath, certContent.FullChain);

            await _registry.Add(identReg);

            //clean up
            await _pendingRegistrationStorage.Delete(reg.Id);
            await _reservationStorage.Delete(reg.Reservation.Id);

            //TODO: what do i return here?
            return null;
        }

        public async Task<Reservation> Reserve(ReservationRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.DomainName, nameof(request.DomainName)).NotNull().NotEmpty();

            if (request.PreviousReservationId.HasValue)
            {
                await CancelReservation(request.PreviousReservationId.GetValueOrDefault());
            }

            try
            {
                if (!await IsAvailable(request.DomainName))
                {
                    throw new ReservationFailedException("Already Reserved");
                }

                //TODO: need a background clean up job to remove old reservations; for now we will overwrite it
                var key = HashUtil.ReduceSHA256Hash(request.DomainName);
                var record = await _reservationStorage.FindOne(r => r.DomainKey == key);

                var result = new Reservation()
                {
                    Id = record?.Id ?? Guid.NewGuid(),
                    Domain = request.DomainName,
                    CreatedTime = UnixTimeUtc.Now(),
                    ExpiresTime =  UnixTimeUtc.Now().AddSeconds(Configuration.ReservationLength),
                };

                await _reservationStorage.Save(result);

                return result;
            }
            catch (DuplicateKeyException)
            {
                throw new ReservationFailedException("Already Reserved");
            }
        }

        public async Task<bool> IsAvailable(string domain)
        {
            Guard.Argument(domain, nameof(domain)).NotNull().NotEmpty();

            //check reserved domains
            var id = HashUtil.ReduceSHA256Hash(domain);
            var reservation = await _reservationStorage.FindOne(r => r.DomainKey == id);

            if (IsReservationValid(reservation))
            {
                return false;
            }

            var pendingReg = await _pendingRegistrationStorage.FindOne(pr => pr.DomainKey == id);
            if (null != pendingReg)
            {
                return false;
            }

            return await _registry.IsDomainRegistered(domain) == false;
        }

        public async Task CancelReservation(Guid reservationId)
        {
            await _reservationStorage.Delete(reservationId);
        }

        public void Dispose()
        {
            _reservationStorage?.Dispose();
            _pendingRegistrationStorage?.Dispose();
        }

        private CertificateOrder PrepareOrder(RegistrationInfo registrationInfo, Reservation reservation)
        {
            if (registrationInfo.RequestedManagedCertificate)
            {
                return new CertificateOrder()
                {
                    UseBuiltInAccount = registrationInfo.RequestedManagedCertificate,
                    Domain = reservation.Domain,
                    Account = new CertificateAccount()
                    {
                        Email = _config.CertificateAuthorityAssociatedEmail,
                        CertificateSigningRequest = _config.CertificateSigningRequest
                    }
                };
            }
            else
            {
                Guard.Argument(registrationInfo.EmailAddress, nameof(registrationInfo.EmailAddress)).NotNull().NotEmpty();
                Guard.Argument(registrationInfo.CertificateSigningRequest, nameof(registrationInfo.CertificateSigningRequest)).NotNull();
                Guard.Argument(registrationInfo.CertificateSigningRequest.Locality, nameof(registrationInfo.CertificateSigningRequest.Locality)).NotNull().NotEmpty();
                Guard.Argument(registrationInfo.CertificateSigningRequest.State, nameof(registrationInfo.CertificateSigningRequest.State)).NotNull().NotEmpty();
                Guard.Argument(registrationInfo.CertificateSigningRequest.CountryName, nameof(registrationInfo.CertificateSigningRequest.CountryName)).NotNull().NotEmpty();
                Guard.Argument(registrationInfo.CertificateSigningRequest.Organization, nameof(registrationInfo.CertificateSigningRequest.Organization)).NotNull().NotEmpty();
                Guard.Argument(registrationInfo.CertificateSigningRequest.OrganizationUnit, nameof(registrationInfo.CertificateSigningRequest.OrganizationUnit)).NotNull().NotEmpty();

                return new CertificateOrder()
                {
                    UseBuiltInAccount = registrationInfo.RequestedManagedCertificate,
                    Domain = reservation.Domain,
                    Account = new CertificateAccount()
                    {
                        Email = registrationInfo.EmailAddress,
                        CertificateSigningRequest = registrationInfo.CertificateSigningRequest
                    }
                };
            }
        }

        private bool IsReservationValid(Reservation reservation)
        {
            var now = UnixTimeUtc.Now();
            return null != reservation && now < reservation.ExpiresTime;
        }
    }
}