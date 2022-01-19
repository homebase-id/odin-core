using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request from another DI using the transit protocol
    /// </summary>
    public class TransitContext
    {
        private readonly Guid _appId;
        private readonly Guid _driveId;
        private bool _canManageConnections;

        public TransitContext(Guid appId, Guid driveId, bool canManageConnections)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();

            this._appId = appId;
            this._driveId = driveId;
            _canManageConnections = canManageConnections;
        }

        public Guid AppId => this._appId;

        /// <summary>
        /// Specifies the drive associated with this app
        /// </summary>
        public Guid DriveId => this._driveId;
        
        public void AssertCanManageConnections()
        {
            if (!_canManageConnections)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }
        }
    }
}