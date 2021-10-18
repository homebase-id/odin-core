namespace DotYou.Kernel.Services.Identity
{
    public static class CertificateUtils
    {
        public static string GetDomainFromCommonName(string cn)
        {
            return cn.Split("=")[1];
        }
    }
}