﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Registry
{
    public interface IIdentityRegistry
    {
        void Initialize();

        Guid ResolveId(string domainName);

        
        /// <summary>
        /// Checks if a domain is used/registered.
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<bool> IsIdentityRegistered(string domain);

        /// <summary>
        /// Adds an identity to this host
        /// </summary>
        /// <param name="reg"></param>
        Task Add(IdentityRegistrationRequest reg);

        /// <summary>
        /// Gets a list of <see cref="IdentityRegistration"/>s based on the paging options sorted by domain name ascending
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions);

        /// <summary>
        /// Gets an <see cref="IdentityRegistration"/> by domain name
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        Task<IdentityRegistration> Get(string domainName);
    }
}