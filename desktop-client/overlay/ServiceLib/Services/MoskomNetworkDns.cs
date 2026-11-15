using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ServiceLib.Services;

// Capture before creating Wintun/utun: reading DNS afterwards may select the
// tunnel's own resolver and send direct-site queries back into that resolver.
public static class MoskomNetworkDns
{
    public static IReadOnlyList<string> Capture()
    {
        IPAddress? preferred = null;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53)); // Route lookup only; sends no packet.
            preferred = (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException) { }
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Select(n => n.GetIPProperties())
            .OrderByDescending(p => preferred != null && p.UnicastAddresses.Any(a => a.Address.Equals(preferred)))
            .ThenByDescending(p => p.GatewayAddresses.Any(a => !a.Address.Equals(IPAddress.Any) && !a.Address.Equals(IPAddress.IPv6Any)));
        foreach (var adapter in adapters)
        {
            var servers = adapter.DnsAddresses.Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)
                && !a.IsIPv6SiteLocal && !a.IsIPv6LinkLocal && a.ToString() is not "172.19.0.2" and not "fdfe:dcba:9876::2")
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .Select(a => a.ToString()).Distinct().ToArray();
            if (servers.Length > 0) return servers;
        }
        throw new InvalidOperationException("Не найдены DNS-серверы текущего подключения. Проверьте Wi-Fi или кабель и повторите подключение.");
    }
}
