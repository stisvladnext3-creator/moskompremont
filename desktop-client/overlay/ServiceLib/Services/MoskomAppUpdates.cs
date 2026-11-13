using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Services;

public sealed record MoskomAppRelease(Version Version, string Tag, Uri Installer, Uri Checksums, string FileName, long Size);

public sealed class MoskomAppUpdates(MoskomTransfers downloads)
{
    private const string Prefix = "moskom-vpn-v";
    public static MoskomAppRelease? SelectRelease(JsonElement releases, Version current)
    {
        if (releases.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Не удалось прочитать список обновлений.");
        var candidates = new List<MoskomAppRelease>();
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            if (!release.TryGetProperty("tag_name", out var tagValue)) continue;
            var tag = tagValue.GetString() ?? "";
            if (!tag.StartsWith(Prefix, StringComparison.Ordinal) || !Regex.IsMatch(tag[Prefix.Length..], @"\A\d+\.\d+\.\d+\z") || !Version.TryParse(tag[Prefix.Length..], out var version) || version <= current) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            var name = $"MoskomVPN-Setup-{version}.exe";
            Uri? installer = null, checksums = null; long size = 0;
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var n) || !asset.TryGetProperty("browser_download_url", out var u)) continue;
                var file = n.GetString(); if (file != name && file != "SHA256SUMS.txt") continue;
                if (!Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri)) continue;
                var expected = $"https://github.com/{MoskomBuildInfo.Repository}/releases/download/{tag}/{file}";
                if (uri.AbsoluteUri != expected) continue;
                if (file == name) { installer = uri; if (asset.TryGetProperty("size", out var s)) size = s.GetInt64(); }
                else checksums = uri;
            }
            if (installer != null && checksums != null && size is > 0 and <= 512L * 1024 * 1024) candidates.Add(new(version, tag, installer, checksums, name, size));
        }
        return candidates.OrderByDescending(c => c.Version).FirstOrDefault();
    }
    public async Task<MoskomAppRelease?> CheckAsync(Version current, CancellationToken ct)
    {
        // All app-specific releases are considered, including our explicitly published preview channel.
        using var json = await downloads.JsonAsync(new Uri($"https://api.github.com/repos/{MoskomBuildInfo.Repository}/releases?per_page=30"), ct);
        return SelectRelease(json.RootElement, current);
    }
    public static string ParseChecksum(string content, string filename)
    {
        foreach (var line in content.TrimStart('\ufeff').Split('\n'))
        {
            var match = Regex.Match(line.TrimEnd('\r'), @"\A([a-fA-F0-9]{64}) [ *](.+)\z");
            if (match.Success && match.Groups[2].Value == filename) return match.Groups[1].Value.ToLowerInvariant();
        }
        throw new InvalidOperationException("В выпуске не найдена контрольная сумма установщика.");
    }
    public async Task<string> DownloadAsync(MoskomAppRelease release, string directory, CancellationToken ct, IProgress<MoskomTransferProgress>? progress)
    {
        var manifest = Encoding.UTF8.GetString(await downloads.BytesAsync(release.Checksums, 65536, ct));
        var sha = ParseChecksum(manifest, release.FileName); var target = Path.Combine(directory, release.FileName);
        await downloads.DownloadAsync(release.Installer, target, 512L * 1024 * 1024, sha, ct, progress);
        using var file = File.OpenRead(target);
        if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') { file.Dispose(); File.Delete(target); throw new InvalidOperationException("Получен некорректный установщик."); }
        return target;
    }
}
