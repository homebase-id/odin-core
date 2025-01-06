using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Configuration.VersionUpgrade.Version2tov3
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V2ToV3VersionMigrationService(
        ILogger<V2ToV3VersionMigrationService> logger,
        CircleNetworkIntroductionService introductionService)
    {
        private readonly UnixTimeUtc _maxDate = UnixTimeUtc.FromDateTime(new DateTime(2025, 1, 5));

        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            //
            // delete all old introductions for sanity
            //
            logger.LogDebug("Deleting all introductions before {maxDate}", _maxDate.ToDateTime().ToShortDateString());
            await introductionService.DeleteIntroductionsAsync(odinContext, _maxDate);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var introductions = await introductionService.GetReceivedIntroductionsAsync(odinContext);
            if (introductions.Any(intro => intro.Received > _maxDate))
            {
                throw new OdinSystemException($"Validation failed: there is one or more introductions after {_maxDate}");
            }
        }
    }
}