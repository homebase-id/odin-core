﻿using DotYou.Kernel.Storage;
using DotYou.Types;
using DotYou.Types.Certificate;
using DotYou.Types.TrustNetwork;
using Identity.Web.Services.Contacts;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.TrustNetwork
{
    public class TrustNetworkService : DotYouServiceBase, ITrustNetworkService
    {
        const string PENDING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        private readonly IContactService _contactService;

        public TrustNetworkService(DotYouContext context, IContactService contactService, ILogger<TrustNetworkService> logger) : base(context, logger)
        {
            _contactService = contactService;
        }

        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageOptions)
        {
            var results = await WithTenantStorageReturnList<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageOptions)
        {
            var results = await WithTenantStorageReturnList<ConnectionRequest>(SENT_CONNECTION_REQUESTS, storage => storage.GetList(pageOptions));
            return results;
        }

        public async Task SendConnectionRequest(ConnectionRequest request)
        {
            this.Logger.LogInformation($"[{request.Sender}] is sending a request to the server of  [{request.Recipient}]");
            await base.HttpProxy.PostJson<ConnectionRequest>(request.Recipient, "/api/incoming/invitations/connect", request);

            WithTenantStorage<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Save(request));
        }

        public Task ReceiveConnectionRequest(ConnectionRequest request)
        {
            request.Validate();

            this.Logger.LogInformation($"[{request.Recipient}] is receiving a connection request from [{request.Sender}]");

            WithTenantStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Save(request));

            /*
            try
            {
                //Note: since we're hosting both frodo and sam on the same server
                //we MUST specify the user, otherwise all people connected to this server 
                //will get the notification

                //the ci.RecpientIdentifier must also be authenticated
                _notificationHub.Clients.User(_tenantContext.Identifier).NotifyOfConnectionRequest(request);
            }
            catch (Exception)
            {
                //_logger.LogWarning("Failed to send notification to clients");
            }
             */
            return Task.CompletedTask;
        }

        public async Task<ConnectionRequest> GetPendingRequest(Guid id)
        {

            var result = await WithTenantStorageReturnSingle<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Get(id));
            return result;
        }

        public Task DeletePendingRequest(Guid id)
        {
            WithTenantStorage<ConnectionRequest>(PENDING_CONNECTION_REQUESTS, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<ConnectionRequest> GetSentRequest(Guid id)
        {
            var result = await WithTenantStorageReturnSingle<ConnectionRequest>(SENT_CONNECTION_REQUESTS, s => s.Get(id));
            return result;
        }

        public async Task EstablishConnection(EstablishConnectionRequest request)
        {

            var originalRequest = await this.GetSentRequest(request.ConnectionRequestId);

            if(null == originalRequest)
            {
                throw new InvalidOperationException("The original request no longer exists in Sent Requests");
            }

            string recipientDomain = GetCertificateDomain(request.RecipientPublicKey);
            var ec = await _contactService.GetByDomainName(recipientDomain);

            //TODO: address how this contact merge should really happen
            var contact = new Contact()
            {
                GivenName = request.RecipientGivenName,
                Surname = request.RecipientSurname,
                DotYouId = (DotYouIdentity)recipientDomain,
                SystemCircle = SystemCircle.Connected,
                PrimaryEmail = null == ec ? "" : ec.PrimaryEmail,
                PublicKeyCertificate = request.RecipientPublicKey,
                Tag = null == ec ? "" : ec.Tag
            };

            await _contactService.Save(contact);



        }

        public async Task AcceptConnectionRequest(Guid requestId)
        {
            //get the requst
            var request = await GetPendingRequest(requestId);
            if (null == request)
            {
                throw new InvalidOperationException($"No pending request was found with id [{requestId}]");
            }

            request.Validate();

            this.Logger.LogInformation($"Accept Connection request called for sender {request.Sender} to {request.Recipient}");

            string domain = GetCertificateDomain(request.SenderPublicKey);

            var ec = await _contactService.GetByDomainName(domain);

            //TODO: address how this contact merge should really happen
            var contact = new Contact()
            {
                GivenName = request.SenderGivenName,
                Surname = request.SenderSurname,
                DotYouId = (DotYouIdentity)domain,
                SystemCircle = SystemCircle.Connected,
                PrimaryEmail = null == ec ? "" : ec.PrimaryEmail,
                PublicKeyCertificate = request.SenderPublicKey,
                Tag = null == ec ? "" : ec.Tag
            };

            //Upsert
            await _contactService.Save(contact);

            //call to request.Sender's agent to establish connection.
            EstablishConnectionRequest acceptedReq = new()
            {
                ConnectionRequestId = requestId,
                RecipientPublicKey = "",
                RecipientGivenName = "",
                RecipientSurname = ""
            };

            var response = await this.HttpProxy.PostJson(request.Sender, "api/incoming/invitations/establishconnection", acceptedReq);
            
            if(!response)
            {
                //TODO: add more info
                throw new Exception("Failed to establish connection request");
            }

            await this.DeletePendingRequest(request.Id);
        }

        private string GetCertificateDomain(string publicKeyCertificate)
        {
            string domain = "";
            using (var certificate = CertificateLoader.LoadPublicKeyCertificate(publicKeyCertificate))
            {
                //HACK - need to put this sort of parsing into a class OR accept the subject as the domain?
                domain = certificate.Subject.Split("=")[0];
            }
            return domain;

        }
    }
}
