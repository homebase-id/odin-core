using System;
using System.Collections.Generic;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    public class CircleDefinitionService : ICircleDefinitionService
    {
        public Circle GetRootCircle()
        {
            var grants = new List<CircleGrantAccessMap>()
            {
                new() {Appid = SystemAppConstants.ProfileAppId, DriveAlias = new List<Guid>() {SystemAppConstants.ProfileAppStandardProfileDriveAlias}},
                new() {Appid = SystemAppConstants.WebHomeAppId, DriveAlias = new List<Guid>() {SystemAppConstants.WebHomeDefaultDriveAlias}},
            };

            var defaultCircle = new Circle()
            {
                Id = Guid.Parse("11111118-0000-0000-0001-000000008733"),
                Name = "Basic Circle",
                Grants = grants
            };

            return defaultCircle;
        }
    }
}