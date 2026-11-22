using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ServiceLib.Services;

internal static partial class Program
{
    [System.Runtime.InteropServices.DllImport("dnsapi.dll", EntryPoint="DnsFlushResolverCache")]
    private static extern bool FlushWindowsDnsCache();
    private const string PublicHost = "raw.githubusercontent.com";
    private static IReadOnlyList<string> _networkDns = [];
    private static bool _ipv6Available;
    private static IPAddress _directAddress = IPAddress.None;
    private static int _proxyDnsRequests, _proxiedHttps;

    private static async Task PrepareDns()
    {
        _networkDns = MoskomNetworkDns.Capture();
        _ipv6Available = MoskomNetworkDns.HasIpv6Route();
        Console.WriteLine("Physical IPv6 route available: " + _ipv6Available);
        _directAddress = (await Dns.GetHostAddressesAsync(PublicHost)).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        Check(_networkDns.Count > 0, "Missing baseline network DNS");
    }

    private static async Task CheckPublicDirect(string executable, bool serviceMode)
    {
        var dnsRequests = _proxyDnsRequests; var proxyHttps = _proxiedHttps;
        var address = await QueryDns(PublicHost);
        if (serviceMode) Check(_proxyDnsRequests == dnsRequests, "An unselected site's DNS was sent through VPN");
        var start = new ProcessStartInfo(Mac ? "/usr/bin/sudo" : executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Mac) foreach (var arg in new[] { "-n", "-u", Environment.GetEnvironmentVariable("SUDO_USER")!, "--", executable }) start.ArgumentList.Add(arg);
        // Schannel's external CRL lookups are outside this DNS fixture. Keep CA
        // and hostname verification enabled; omit only online CRL retrieval here.
        if (!Mac) start.ArgumentList.Add("--ssl-no-revoke");
        // Exercise the real OS resolver. An explicit --resolve previously hid
        // failures between Windows/macOS DNS and the configured TUN DNS server.
        if(!Mac) Check(FlushWindowsDnsCache(),"Could not clear the OS DNS cache for the routing regression");
        else { using var flush=Process.Start(new ProcessStartInfo("/usr/bin/dscacheutil","-flushcache"){UseShellExecute=false})!;await flush.WaitForExitAsync();Check(flush.ExitCode==0,"Could not flush Mac DNS cache"); }
        foreach (var arg in new[] { "--ipv4", "--silent", "--show-error", "--fail", "--noproxy", "*", "--proxy", "", "--connect-timeout", "8", "--max-time", "25",
            "https://" + PublicHost + "/stisvladnext3-creator/moskompremont/main/shared/service-catalog.json" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); var body = await output; var error = await errors;
        Check(process.ExitCode == 0 && body.Contains("google-meet") && body.Contains("facetime"), "Unselected HTTPS must actually open over direct connection: " + error);
        Check(_proxiedHttps == proxyHttps && !body.Contains(Marker), "Ordinary HTTPS traversed the VPN fixture");
        Console.WriteLine("PASS ordinary HTTPS and working DNS alongside " + (serviceMode ? "selected services" : "selected apps"));
    }

    private static byte[] Question(string host)
    {
        using var result = new MemoryStream();
        result.Write(new byte[] { 0x39, 0x27, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (var label in host.Split('.')) { result.WriteByte((byte)label.Length); result.Write(Encoding.ASCII.GetBytes(label)); }
        result.Write(new byte[] { 0, 0, 1, 0, 1 }); return result.ToArray();
    }

    private static int SkipName(byte[] data, int offset)
    {
        while (offset < data.Length) { var n = data[offset++]; if (n == 0) return offset; if ((n & 0xc0) == 0xc0) return offset + 1; offset += n; }
        throw new IOException("Invalid DNS name");
    }

    private static async Task<IPAddress> QueryDns(string host)
    {
        var response = await AskDns(host, 1);
        var offset = 12; var questions = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(4));
        for (var i = 0; i < questions; i++) offset = SkipName(response, offset) + 4;
        var answers = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6));
        for (var i = 0; i < answers; i++)
        {
            offset = SkipName(response, offset); var type = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset));
            var size = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset + 8)); offset += 10;
            if (type == 1 && size == 4) return new IPAddress(response.AsSpan(offset, 4)); offset += size;
        }
        throw new IOException("No IPv4 DNS answer for " + host);
    }

    private static async Task<byte[]> AskDns(string host, ushort type)
    {
        using var socket = new UdpClient(AddressFamily.InterNetwork); socket.Connect("172.19.0.2", 53);
        var query = Question(host); BinaryPrimitives.WriteUInt16BigEndian(query.AsSpan(query.Length - 4), type);
        await socket.SendAsync(query, query.Length);
        var response = (await socket.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(12))).Buffer;
        Check(response.Length >= 12 && response[0] == query[0] && response[1] == query[1] && (response[3] & 15) == 0, "DNS resolution failed for " + host);
        return response;
    }

    private static async Task AnswerProxyDns(NetworkStream stream, CancellationToken ct)
    {
        var length = new byte[2]; await stream.ReadExactlyAsync(length, ct);
        var query = new byte[BinaryPrimitives.ReadUInt16BigEndian(length)]; await stream.ReadExactlyAsync(query, ct);
        Check(query.Length > 16, "Invalid DNS fixture query");
        var labels = new List<string>(); var offset = 12;
        while (query[offset] != 0) { var size = query[offset++]; labels.Add(Encoding.ASCII.GetString(query, offset, size)); offset += size; }
        var type = BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(offset + 1));
        var address = type == 28 ? IPAddress.Parse("2001:db8::77") : string.Join('.', labels) == PublicHost ? _directAddress : IPAddress.Parse("198.51.100.77");
        var addressBytes = address.GetAddressBytes(); var questionEnd = offset + 5;
        var response = new byte[questionEnd + 12 + addressBytes.Length]; query.AsSpan(0, questionEnd).CopyTo(response);
        response[2] = 0x81; response[3] = 0x80; response[6] = 0; response[7] = 1; response[10] = 0; response[11] = 0;
        new byte[] { 0xc0, 0x0c, 0, 0, 0, 1, 0, 0, 0, 30, 0, 0 }.CopyTo(response, questionEnd);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(questionEnd + 2), type);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(questionEnd + 10), (ushort)addressBytes.Length);
        addressBytes.CopyTo(response, questionEnd + 12); BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)response.Length);
        Interlocked.Increment(ref _proxyDnsRequests); await stream.WriteAsync(length, ct); await stream.WriteAsync(response, ct);
    }
}
