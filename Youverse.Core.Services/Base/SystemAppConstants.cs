using System;

namespace Youverse.Core.Services.Base
{
    public static class SystemAppConstants
    {
        public static readonly Guid ProfileAppId = Guid.Parse("99999789-4444-4444-4444-000000004444");
        public static readonly Guid ProfileAppConfigDriveAlias = Guid.Parse("99999789-5555-5555-4444-000000005555");
        public static readonly Guid ProfileAppStandardProfileDriveAlias = Guid.Parse("99999789-4444-4444-4444-000000006666");
        public static readonly Guid ProfileAppFinancialProfileDriveAlias = Guid.Parse("99999789-4444-4444-4444-000000007777");

        public static readonly Guid ProfileDriveType = Guid.Parse("11112222-0000-0000-0000-000000001111");
        
        public static readonly Guid ChatAppId = Guid.Parse("99999789-5555-5555-5555-000000002222");
        public static readonly Guid ChatAppDefaultDriveAlias = Guid.Parse("99999789-5555-5555-6666-000000005555");
        public static readonly Guid ChatDriveType = Guid.Parse("11113333-0000-0000-0000-000000003333");

        public static readonly Guid WebHomeAppId = Guid.Parse("99999789-6666-6666-6666-000000001111");
        public static readonly Guid WebHomeDefaultDriveAlias = Guid.Parse("99999789-6666-6666-6666-000000005555");
        public static readonly Guid WebHomeDriveType = Guid.Parse("11116666-0000-0000-0000-000000006666");

    }
}