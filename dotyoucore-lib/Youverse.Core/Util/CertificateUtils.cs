namespace Youverse.Core.Util
{
    public static class CertificateUtils
    {
        public static string GetDomainFromCommonName(string cn)
        {
            return cn.Split("=")[1];
        }
    }
}