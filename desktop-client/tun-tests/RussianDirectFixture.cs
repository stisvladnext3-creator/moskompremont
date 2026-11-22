using System.Diagnostics;

internal static partial class Program
{
    private static async Task CheckRussianDirect(string executable)
    {
        var dnsRequests = _proxyDnsRequests;
        var proxyHttps = _proxiedHttps;
        var address = await QueryDns("ya.ru");
        Check(!address.Equals(System.Net.IPAddress.Parse("198.51.100.77")), "Yandex received the proxy fixture DNS answer");
        Check(_proxyDnsRequests == dnsRequests, "Unselected Yandex DNS traversed the VPN");
        if (!_ipv6Available)
        {
            var directV6 = await AskDns("ya.ru", 28);
            Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(directV6.AsSpan(6)) == 0, "Direct sites must not receive unusable IPv6 addresses");
            var proxyV6 = await AskDns("youtube.com", 28);
            Check(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(proxyV6.AsSpan(6)) > 0, "Selected services must retain proxy IPv6 DNS");
            dnsRequests = _proxyDnsRequests;
        }

        var start = new ProcessStartInfo(Mac ? "/usr/bin/sudo" : executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Mac)
            foreach (var argument in new[] { "-n", "-u", Environment.GetEnvironmentVariable("SUDO_USER")!, "--", executable })
                start.ArgumentList.Add(argument);
        // Allow either address family, as browsers do. HTTP errors/captchas from
        // this origin still prove a connection; TLS/network/DNS errors do not.
        foreach (var argument in new[] { "--silent", "--show-error", "--head", "--noproxy", "*", "--proxy", "", "--connect-timeout", "8", "--max-time", "25", "https://ya.ru/" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var headers = await output; var error = await errors;
        Check(process.ExitCode == 0 && headers.StartsWith("HTTP/", StringComparison.Ordinal), "Unselected Yandex HTTPS must open over the direct connection: " + error);
        Check(_proxiedHttps == proxyHttps && _proxyDnsRequests == dnsRequests, "Unselected Yandex traffic entered the VPN fixture");
        Console.WriteLine("PASS Yandex system DNS and direct HTTPS alongside selected services and personal domains");
    }
}
