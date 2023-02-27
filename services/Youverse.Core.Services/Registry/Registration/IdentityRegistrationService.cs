using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Configuration;

namespace Youverse.Core.Services.Registry.Registration
{
    public class IdentityRegistrationService : IIdentityRegistrationService
    {
        private readonly ILogger<IdentityRegistrationService> _logger;
        private readonly IIdentityRegistry _registry;
        private readonly ReservationStorage _reservationStorage;
        private readonly YouverseConfiguration _configuration;

        public IdentityRegistrationService(ILogger<IdentityRegistrationService> logger, IHttpContextAccessor accessor, YouverseConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _registry = accessor!.HttpContext!.RequestServices!.GetRequiredService<IIdentityRegistry>();

            //TODO: configure this location correctly 
            string storagePath = "/tmp/dotyou/system";
            _reservationStorage = new ReservationStorage(storagePath);
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
                _reservationStorage.Delete(registrationInfo.ReservationId);
                throw new Exception("Reservation not valid");
            }

            var request = new IdentityRegistrationRequest()
            {
                DotYouId = (OdinId)reservation.Domain,
                IsCertificateManaged = false, //TODO
            };

            var firstRunToken = await _registry.AddRegistration(request);
            
            _logger.LogInformation($"Pending registration record saved:{registrationInfo.ReservationId}");
            return firstRunToken;
        }

        public async Task FinalizeRegistration(Guid firstRunToken)
        {
            await _registry.MarkRegistrationComplete(firstRunToken);
        }

        public async Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken)
        {
            var status = await _registry.GetRegistrationStatus(firstRunToken);
            return status;
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
                throw new YouverseClientException("Already Reserved", YouverseClientErrorCode.IdAlreadyExists);
            }

            //TODO: need a background clean up job to remove old reservations; for now we will overwrite it
            var record = _reservationStorage.GetByDomain(request.DomainName);

            var result = new Reservation()
            {
                Id = record?.Id ?? Guid.NewGuid(),
                Domain = request.DomainName,
                CreatedTime = UnixTimeUtc.Now(),
                ExpiresTime = UnixTimeUtc.Now().AddSeconds(60 * 60) //TODO: add to config
            };

            _reservationStorage.Save(result);

            return result;
        }

        public async Task<bool> IsAvailable(string domain)
        {
            Guard.Argument(domain, nameof(domain)).NotNull().NotEmpty();

            var reservation = _reservationStorage.GetByDomain(domain);

            if (IsReservationValid(reservation))
            {
                return false;
            }

            // var pendingReg = _pendingRegistrationStorage.Get(id);
            // if (null != pendingReg)
            // {
            //     return false;
            // }

            return await _registry.IsIdentityRegistered(domain) == false;
        }

        public async Task CancelReservation(Guid reservationId)
        {
            _reservationStorage.Delete(reservationId);
        }

        public Task<List<string>> GetManagedDomains()
        {
            return Task.FromResult(_configuration.Registry.ManagedDomains);
        }

        public Task<DnsConfigurationSet> GetDnsConfiguration(string domain)
        {
            string record = _configuration.Registry.DnsTargetRecordType;
            string target = _configuration.Registry.DnsTargetAddress;
            var cfg = new DnsConfigurationSet()
            {
                ApiServer = new DnsConfig()
                {
                    Domains = new List<string>() { $"api.{domain}" },
                    RecordType = record,
                    TargetServer = target
                },

                CdnServer = new DnsConfig()
                {
                    Domains = new List<string>()
                    {
                        domain,
                        $"www.{domain}",
                    },
                    RecordType = record,
                    TargetServer = target
                }
            };

            return Task.FromResult(cfg);
        }

        private bool IsReservationValid(Reservation reservation)
        {
            var now = UnixTimeUtc.Now();
            return null != reservation && now < reservation.ExpiresTime;
        }
    }

    public class DnsConfigurationSet
    {
        public DnsConfig CdnServer { get; set; }
        public DnsConfig ApiServer { get; set; }
    }

    public class DnsConfig
    {
        public List<string> Domains { get; set; }
        public string RecordType { get; set; }
        public string TargetServer { get; set; }
    }
}