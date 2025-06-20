using System.Net;
using System.Net.Sockets;

namespace Odin.Core.Dns;

public static class IpAddressExtensions
{
    public static bool IsLocalhost(this IPAddress address)
    {
        return IPAddress.IsLoopback(address);
    }

    //

    public static bool IsPrivateNetwork(this IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork) // IPv4
        {
            var bytes = address.GetAddressBytes();

            // 10.0.0.0/8 (10.0.0.0 - 10.255.255.255)
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16 (192.168.0.0 - 192.168.255.255)
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 169.254.0.0/16 (Link-local addresses)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6) // IPv6
        {
            // fc00::/7 (Unique local addresses)
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }

            // fe80::/10 (Link-local addresses)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
            {
                return true;
            }
        }

        return false;
    }

    //

    public static bool IsLocalOrPrivate(this IPAddress address)
    {
        return address.IsLocalhost() || address.IsPrivateNetwork();
    }

    //

}


