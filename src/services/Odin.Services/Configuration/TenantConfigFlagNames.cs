namespace Odin.Services.Configuration;

public enum TenantConfigFlagNames
{
    /// <summary/>
    AnonymousVisitorsCanViewWhoIFollow,
    
    /// <summary/>
    AuthenticatedIdentitiesCanViewWhoIFollow,

    /// <summary/>
    ConnectedIdentitiesCanViewWhoIFollow,
    
    /// <summary/>
    AnonymousVisitorsCanViewConnections,

    /// <summary/>
    AuthenticatedIdentitiesCanViewConnections,

    /// <summary/>
    ConnectedIdentitiesCanViewConnections,
    
    /// <summary/>
    AuthenticatedIdentitiesCanReactOnAnonymousDrives,

    /// <summary/>
    AuthenticatedIdentitiesCanCommentOnAnonymousDrives,
    
    /// <summary/>
    ConnectedIdentitiesCanReactOnAnonymousDrives,
    
    /// <summary/>
    ConnectedIdentitiesCanCommentOnAnonymousDrives,
    
    /// <summary/>
    DisableAutoAcceptIntroductionsForTests,

    /// <summary/>
    DisableAutoAcceptConnectionRequests,

    /// <summary/>
    SendMonthlySecurityHealthReport,

    /// <summary>
    /// Dark-launch switch for the reviewed security tier.  Off (the default) keeps every connected
    /// caller at <c>Connected</c> exactly as before; on assigns the tier from whether the owner has
    /// reviewed them.  See <see cref="TenantSettings.UseReviewedSecurityTier"/>.
    /// </summary>
    UseReviewedSecurityTier
}