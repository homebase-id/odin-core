using System;

namespace Youverse.Core.Services.Base
{
    public static class SystemAppConstants
    {
        public static readonly Guid ProfileAppId = Guid.Parse("99999789-4444-4444-4444-000000004444");

        public static readonly Guid ChatAppId = Guid.Parse("99999789-5555-5555-5555-000000002222");
        public static readonly Guid ChatAppDefaultDriveId = Guid.Parse("99999789-5555-5555-6666-000000005555");

        public static readonly Guid ProfileAppStandardProfileDriveId = Guid.Parse("99999789-4444-4444-4444-000000006666");
        public static readonly Guid ProfileAppFinancialProfileDriveId = Guid.Parse("99999789-4444-4444-4444-000000007777");

        public static readonly Guid WebHomeAppId = Guid.Parse("99999789-6666-6666-6666-000000001111");
        public static readonly Guid WebHomeDefaultDriveId = Guid.Parse("99999789-6666-6666-6666-000000005555");
    }
}