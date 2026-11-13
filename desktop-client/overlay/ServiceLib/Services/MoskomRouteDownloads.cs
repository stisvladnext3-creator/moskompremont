using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Services;

public sealed record MoskomRouteManifest(string RoutingCommit, string RulesCommit, string GeoRelease, DateTimeOffset FetchedAt);

public sealed class MoskomRouteDownloads(MoskomTransfers downloads)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Sha(JsonElement element)
    {
        var value = element.GetProperty("sha").GetString() ?? "";
        if (!Regex.IsMatch(value, @"\A[a-f0-9]{40}\z")) throw new InvalidOperationException("Не удалось определить версию правил.");
        return value;
    }
    public static string Digest(JsonElement asset)
    {
        var value = asset.GetProperty("digest").GetString() ?? "";
        if (!Regex.IsMatch(value, @"\Asha256:[a-f0-9]{64}\z")) throw new InvalidOperationException("Для геоданных отсутствует контрольная сумма.");
        return value[7..];
    }
    public async Task<MoskomRouteManifest?> DownloadAsync(string directory, CancellationToken ct, IProgress<MoskomTransferProgress>? progress, MoskomRouteManifest? current = null)
    {
        using var routeCommit = await downloads.JsonAsync(new Uri("https://api.github.com/repos/runetfreedom/russia-v2ray-custom-routing-list/commits/main"), ct);
        using var rulesCommit = await downloads.JsonAsync(new Uri("https://api.github.com/repos/runetfreedom/russia-v2ray-rules-dat/commits/release"), ct);
        using var geo = await downloads.JsonAsync(new Uri("https://api.github.com/repos/runetfreedom/russia-v2ray-rules-dat/releases/latest"), ct);
        var routeSha = Sha(routeCommit.RootElement); var rulesSha = Sha(rulesCommit.RootElement);
        var release = geo.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(release, @"\A\d{12}\z")) throw new InvalidOperationException("Некорректная версия геоданных.");
        if (current != null && current.RoutingCommit == routeSha && current.RulesCommit == rulesSha && current.GeoRelease == release) return null;
        Directory.CreateDirectory(directory);
        var baseUrl = $"https://raw.githubusercontent.com/runetfreedom/russia-v2ray-custom-routing-list/{routeSha}/v2rayN/";
        async Task<string> TextFile(string remote, string local)
        {
            if (!Regex.IsMatch(remote, @"\A[A-Za-z0-9_-][A-Za-z0-9_.-]{0,99}\z") || remote.Contains("..", StringComparison.Ordinal)) throw new InvalidOperationException("Некорректный файл правил.");
            var bytes = await downloads.BytesAsync(new Uri(baseUrl + remote), 4 * 1024 * 1024, ct);
            await File.WriteAllBytesAsync(Path.Combine(directory, local), bytes, ct); return Encoding.UTF8.GetString(bytes);
        }
        var templateText = await TextFile("template.json", "template.json");
        using var template = JsonDocument.Parse(templateText);
        var routes = template.RootElement.GetProperty("routingItems");
        if (routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() is < 1 or > 12) throw new InvalidOperationException("В пресете не найдены маршруты.");
        var texts = new List<string>(); var index = 0;
        foreach (var item in routes.EnumerateArray())
        {
            var uri = new Uri(item.GetProperty("url").GetString()!); var name = Path.GetFileName(uri.AbsolutePath);
            var text = await TextFile(name, $"route-{index++}.json");
            using var rules = JsonDocument.Parse(text);
            if (rules.RootElement.ValueKind != JsonValueKind.Array || rules.RootElement.GetArrayLength() == 0) throw new InvalidOperationException("Получен пустой список правил.");
            texts.Add(text);
        }
        foreach (var name in new[] { "simple_dns.json", "v2ray.json", "sing_box.json" })
        {
            var text = await TextFile(name, name); var doc = JsonNode.Parse(text) ?? throw new InvalidOperationException("Некорректные настройки DNS.");
            foreach (var field in new[] { "normalDNS", "tunDNS" })
            {
                var value = doc[field]?.GetValue<string>();
                if (value != null && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                {
                    if (!uri.AbsoluteUri.StartsWith("https://raw.githubusercontent.com/runetfreedom/", StringComparison.Ordinal) && !uri.AbsoluteUri.StartsWith("https://cdn.jsdelivr.net/gh/runetfreedom/russia-v2ray-custom-routing-list@", StringComparison.Ordinal)) throw new InvalidOperationException("Неизвестный источник DNS.");
                    var resource = Path.GetFileName(uri.AbsolutePath); var raw = await TextFile(resource, "dns-" + field + "-" + name);
                    using var parsed = JsonDocument.Parse(raw); doc[field] = raw;
                }
            }
            var final = doc.ToJsonString(); texts.Add(final); await File.WriteAllTextAsync(Path.Combine(directory, name), final, ct);
        }
        foreach (var name in new[] { "geoip.dat", "geosite.dat" })
        {
            var asset = geo.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == name);
            if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("В выпуске отсутствуют геоданные.");
            var expected = $"https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/download/{release}/{name}";
            if (asset.GetProperty("browser_download_url").GetString() != expected) throw new InvalidOperationException("Некорректный адрес геоданных.");
            await downloads.DownloadAsync(new Uri(expected), Path.Combine(directory, name), 160L * 1024 * 1024, Digest(asset), ct, progress);
        }
        var tags = Regex.Matches(string.Join("\n", texts), @"(geoip|geosite):([A-Za-z0-9_@!.-]+)").Select(m => m.Groups[1].Value + "-" + m.Groups[2].Value).Distinct().ToList();
        if (tags.Count > 200) throw new InvalidOperationException("Слишком много файлов правил.");
        foreach (var tag in tags)
        {
            var type = tag.StartsWith("geosite-", StringComparison.Ordinal) ? "geosite" : "geoip";
            await downloads.DownloadAsync(new Uri($"https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/{rulesSha}/sing-box/rule-set-{type}/{tag}.srs"), Path.Combine(directory, "srss", tag + ".srs"), 24L * 1024 * 1024, null, ct, progress);
        }
        var manifest = new MoskomRouteManifest(routeSha, rulesSha, release, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, Json), ct);
        return manifest;
    }
}

public static class MoskomDnsChoices
{
    public static readonly string[] Names = ["Автоматически · Russia", "Cloudflare · 1.1.1.1", "Google · 8.8.8.8", "Quad9 · 9.9.9.9", "Свой DNS"];
    public static string? RemoteAddress(string choice, string? custom = null) => choice switch
    {
        "auto" => null,
        "cloudflare" => "https://1.1.1.1/dns-query",
        "google" => "https://dns.google/dns-query",
        "quad9" => "https://dns.quad9.net/dns-query",
        "custom" => Validate(custom),
        _ => throw new InvalidOperationException("Выберите DNS из списка.")
    };
    private static string Validate(string? value)
    {
        value = value?.Trim() ?? "";
        if (value.Length <= 1024 && (System.Net.IPAddress.TryParse(value, out _) || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment))) return value;
        throw new InvalidOperationException("Укажите IP-адрес DNS или HTTPS-адрес DNS-over-HTTPS.");
    }
}
