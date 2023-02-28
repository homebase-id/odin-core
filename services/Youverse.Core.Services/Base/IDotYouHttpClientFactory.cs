﻿using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base
{
    public interface IDotYouHttpClientFactory
    {
        T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken);

        T CreateClient<T>(OdinId odinId);
    }
}