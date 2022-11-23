using Dawn;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Provisioning.Controllers;
using Youverse.Provisioning.Services.Certificate;


namespace Youverse.Provisioning.Services.Registration
{
    public class RegistrationService : IRegistrationService
    {
        private readonly ILogger<RegistrationService> _logger;
        private readonly IIdentityRegistry _registry;
        private readonly ProvisioningConfig _config;

        private readonly ReservationStorage _reservationStorage;
        private readonly PendingRegistrationStorage _pendingRegistrationStorage;
        private readonly ICertificateService _certificateService;

        public RegistrationService(ILogger<RegistrationService> logger, IIdentityRegistry registry, ProvisioningConfig config, ICertificateService certificateService)
        {
            _logger = logger;
            _registry = registry;
            _config = config;
            _certificateService = certificateService;

            _reservationStorage = new ReservationStorage(config.DataRootPath);
            _pendingRegistrationStorage = new PendingRegistrationStorage(config.DataRootPath);
        }

        public async Task<Guid> StartRegistration(RegistrationInfo registrationInfo)
        {
            _logger.LogInformation($"Starting Registration:{registrationInfo.ReservationId}");

            Guard.Argument(registrationInfo, nameof(registrationInfo)).NotNull();
            Guard.Argument(registrationInfo.ReservationId, nameof(registrationInfo.ReservationId)).NotEqual(Guid.Empty);

            var reservation = _reservationStorage.Get(registrationInfo.ReservationId);
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

            _pendingRegistrationStorage.Save(registration);
            //delete the reservation because we copied it to the pending registration and rule like reservation expiration no longer apply
            _reservationStorage.Delete(registrationInfo.ReservationId);

            _logger.LogInformation($"Registration record saved:{registrationInfo.ReservationId}");
            return registration.Id;
        }

        public async Task<RegistrationStatus> GetRegistrationStatus(Guid pendingRegistrationId)
        {
            var reg = _pendingRegistrationStorage.Get(pendingRegistrationId);

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

        public async Task FinalizeRegistration(Guid pendingRegistrationId)
        {
            var reg = _pendingRegistrationStorage.Get(pendingRegistrationId);

            if (reg == null)
            {
                throw new YouverseClientException("Invalid Registration Id", YouverseClientErrorCode.UnknownId);
            }

            var isReady = await _certificateService.IsCertificateIsReady(reg!.OrderId);
            if (!isReady)
            {
                throw new YouverseClientException("Certificate is not ready.  See GetRegistrationStatus if you want to create a polling mechanism", YouverseClientErrorCode.Todo);
            }

            var certContent = await _certificateService.GenerateCertificate(reg.OrderId);
            var request = new IdentityRegistrationRequest()
            {
                DotYouId = (DotYouIdentity)reg.Domain,
                CertificateContent = certContent,
                IsCertificateManaged = false, //TODO
                CertificateSigningRequest = reg.Order.Account.CertificateSigningRequest
            };

            await _registry.AddRegistration(request);

            //clean up
            _pendingRegistrationStorage.Delete(reg.Id);
            _reservationStorage.Delete(reg.Reservation.Id);
        }

        public async Task<Reservation> Reserve(ReservationRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.DomainName, nameof(request.DomainName)).NotNull().NotEmpty();

            if (request.PreviousReservationId.HasValue)
            {
                await CancelReservation(request.PreviousReservationId.GetValueOrDefault());
            }

            if (!await IsAvailable(request.DomainName))
            {
                throw new ReservationFailedException("Already Reserved");
            }

            //TODO: need a background clean up job to remove old reservations; for now we will overwrite it
            var key = HashUtil.ReduceSHA256Hash(request.DomainName);
            var record = _reservationStorage.Get(key);

            var result = new Reservation()
            {
                Id = record?.Id ?? Guid.NewGuid(),
                Domain = request.DomainName,
                CreatedTime = UnixTimeUtc.Now(),
                ExpiresTime = UnixTimeUtc.Now().AddSeconds(Configuration.ReservationLength),
            };

            _reservationStorage.Save(result);

            return result;
        }

        public async Task<bool> IsAvailable(string domain)
        {
            Guard.Argument(domain, nameof(domain)).NotNull().NotEmpty();

            //check reserved domains
            var id = HashUtil.ReduceSHA256Hash(domain);
            var reservation = _reservationStorage.Get(id);

            if (IsReservationValid(reservation))
            {
                return false;
            }

            var pendingReg = _pendingRegistrationStorage.Get(id);
            if (null != pendingReg)
            {
                return false;
            }

            return await _registry.IsIdentityRegistered(domain) == false;
        }

        public async Task CancelReservation(Guid reservationId)
        {
            _reservationStorage.Delete(reservationId);
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