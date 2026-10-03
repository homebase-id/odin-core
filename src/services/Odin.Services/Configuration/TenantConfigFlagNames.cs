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
    /// Dark-launch switch for hiding circles that belong to no app from app callers.  Off (the default)
    /// lists every circle to an app exactly as before; on lists only app-owned circles.
    /// See <see cref="TenantSettings.HideOwnerCirclesFromApps"/>.
    /// </summary>
    HideOwnerCirclesFromApps,

    /// <summary>
    /// When on, nobody may introduce the owner to anyone.  See <see cref="TenantSettings.DisableAllowIntroductions"/>.
    /// </summary>
    DisableAllowIntroductions
}