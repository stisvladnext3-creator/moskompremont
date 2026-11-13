using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ServiceLib.Services;
using Xunit;

public sealed class SettingsTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private static object Release(string tag, bool draft = false, bool wrongHost = false) => new
    {
        tag_name = tag, draft, prerelease = true,
        assets = new[] { new { name = "MoskomVPN-Setup-" + tag.Replace("moskom-vpn-v", "") + ".exe", browser_download_url = (wrongHost ? "https://evil.example" : "https://github.com") + "/" + MoskomBuildInfo.Repository + "/releases/download/" + tag + "/MoskomVPN-Setup-" + tag.Replace("moskom-vpn-v", "") + ".exe", size = 100 }, new { name = "SHA256SUMS.txt", browser_download_url = "https://github.com/" + MoskomBuildInfo.Repository + "/releases/download/" + tag + "/SHA256SUMS.txt", size = 100 } }
    };
    [Fact] public void PicksNumericAppVersionAndIgnoresWebsiteReleasesDraftsAndForeignAssets()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { Release("moskom-vpn-v0.3.9"), Release("moskom-vpn-v0.3.10"), Release("moskom-vpn-v0.3.30", draft:true), Release("moskom-vpn-v0.3.40", wrongHost:true), Release("v19.0") }));
        var selected = MoskomAppUpdates.SelectRelease(json.RootElement, new Version("0.3.8")); Assert.Equal(new Version("0.3.10"), selected!.Version);
        Assert.Null(MoskomAppUpdates.SelectRelease(json.RootElement, new Version("0.3.10")));
    }
    [Fact] public void ChecksumParserSupportsBomCrLfAndRequiresExactFilename()
    {
        var hash = new string('a',64); Assert.Equal(hash, MoskomAppUpdates.ParseChecksum("\ufeff" + hash + "  app.exe\r\n", "app.exe"));
        Assert.Throws<InvalidOperationException>(() => MoskomAppUpdates.ParseChecksum(hash + "  other.exe", "app.exe"));
    }
    [Theory]
    [InlineData("http://github.com/a")]
    [InlineData("https://evil.example/a")]
    [InlineData("https://github.com.evil.example/a")]
    [InlineData("https://user:pass@github.com/a")]
    public void UnsafeUpdateDestinationsAreRejected(string value) => Assert.False(MoskomTransfers.Allowed(new Uri(value)));
    [Fact] public async Task RedirectToForeignHostIsNeverFollowed()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/file") } }; }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MoskomTransfers(http).BytesAsync(new Uri("https://github.com/test"), 100, CancellationToken.None)); Assert.Equal(1,calls);
    }
    [Fact] public async Task TamperedDownloadDoesNotReplaceExistingFileOrLeavePartial()
    {
        var dir = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); var file = Path.Combine(dir,"app.exe"); await File.WriteAllTextAsync(file,"old");
        try
        {
            using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("tampered")) }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new MoskomTransfers(http).DownloadAsync(new Uri("https://github.com/file"),file,100,new string('a',64),CancellationToken.None));
            Assert.Equal("old",await File.ReadAllTextAsync(file)); Assert.False(File.Exists(file+".part"));
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact] public async Task VerifiedInstallerUsesManifestHashAndPreservesBytes()
    {
        var bytes = Encoding.ASCII.GetBytes("MZ-fixture-installer"); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); var name = "MoskomVPN-Setup-0.3.99.exe";
        using var http = new HttpClient(new Handler(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri!.AbsolutePath.EndsWith(".txt") ? Encoding.UTF8.GetBytes(hash + "  " + name + "\n") : bytes) }));
        var folder = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var release = new MoskomAppRelease(new("0.3.99"),"moskom-vpn-v0.3.99",new("https://github.com/installer"),new("https://github.com/sums.txt"),name,bytes.Length);
            var file = await new MoskomAppUpdates(new(http)).DownloadAsync(release,folder,CancellationToken.None,null); Assert.Equal(bytes,await File.ReadAllBytesAsync(file));
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
    [Fact] public async Task UnchangedRussiaBundleDoesNotDownloadGeoData()
    {
        var a = new string('a',40); var b = new string('b',40); var calls = new List<string>();
        using var http = new HttpClient(new Handler(r =>
        {
            var path = r.RequestUri!.AbsolutePath; calls.Add(path);
            var text = path.Contains("commits/main") ? "{\"sha\":\""+a+"\"}" : path.Contains("commits/release") ? "{\"sha\":\""+b+"\"}" : "{\"tag_name\":\"202611132130\"}";
            return new(HttpStatusCode.OK) { Content = new StringContent(text) };
        }));
        var result = await new MoskomRouteDownloads(new(http)).DownloadAsync("unused",CancellationToken.None,null,new(a,b,"202611132130",DateTimeOffset.UtcNow));
        Assert.Null(result); Assert.Equal(3,calls.Count); Assert.DoesNotContain(calls,p=>p.EndsWith(".dat"));
    }
    [Fact] public void DnsPresetsUseDohAndAutoDoesNotOverrideRussia()
    {
        Assert.Null(MoskomDnsChoices.RemoteAddress("auto"));
        Assert.Equal("https://1.1.1.1/dns-query",MoskomDnsChoices.RemoteAddress("cloudflare"));
        Assert.Equal("https://dns.google/dns-query",MoskomDnsChoices.RemoteAddress("google"));
        Assert.Equal("https://dns.quad9.net/dns-query",MoskomDnsChoices.RemoteAddress("quad9"));
    }
    [Theory] [InlineData("garbage")] [InlineData("http://resolver.test/dns-query")] [InlineData("https://user:pass@resolver.test/dns-query")]
    public void InvalidCustomDnsIsRejected(string value) => Assert.Throws<InvalidOperationException>(()=>MoskomDnsChoices.RemoteAddress("custom",value));
    [Fact] public void CustomIpv6AndHttpsAreAccepted()
    {
        Assert.Equal("2606:4700:4700::1111",MoskomDnsChoices.RemoteAddress("custom","2606:4700:4700::1111"));
        Assert.Equal("https://resolver.test/dns-query",MoskomDnsChoices.RemoteAddress("custom","https://resolver.test/dns-query"));
    }
}
