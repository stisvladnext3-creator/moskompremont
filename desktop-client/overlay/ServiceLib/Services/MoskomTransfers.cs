using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Services;

public sealed record MoskomTransferProgress(string File, long Received, long? Total)
{
    public int? Percent => Total is > 0 ? (int)Math.Min(100, Received * 100 / Total.Value) : null;
}

// Downloads never forward website cookies or credentials. Redirects stay on GitHub's HTTPS hosts.
public sealed class MoskomTransfers(HttpClient client)
{
    public static bool Allowed(Uri uri) => uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort && uri.Host is
        "api.github.com" or "github.com" or "raw.githubusercontent.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    private async Task<HttpResponseMessage> OpenAsync(Uri uri, CancellationToken ct)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            if (!Allowed(uri)) throw new InvalidOperationException("Недопустимый адрес обновления.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("MoskomVPN/" + MoskomBuildInfo.Version);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next = response.Headers.Location; response.Dispose();
                if (next == null) throw new InvalidOperationException("Не удалось получить файл обновления.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode; response.Dispose();
                throw new InvalidOperationException(status == HttpStatusCode.Forbidden || (int)status == 429 ? "Сервер обновлений ограничил запросы. Повторите позже." : "Сервер обновлений временно недоступен.");
            }
            return response;
        }
        throw new InvalidOperationException("Слишком много перенаправлений при загрузке.");
    }
    public async Task<byte[]> BytesAsync(Uri uri, int limit, CancellationToken ct)
    {
        using var response = await OpenAsync(uri, ct);
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("Файл обновления превышает допустимый размер.");
        using var input = await response.Content.ReadAsStreamAsync(ct); using var memory = new MemoryStream();
        var buffer = new byte[16384]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        { if (memory.Length + read > limit) throw new InvalidOperationException("Файл обновления превышает допустимый размер."); memory.Write(buffer, 0, read); }
        return memory.ToArray();
    }
    public async Task<JsonDocument> JsonAsync(Uri uri, CancellationToken ct) => JsonDocument.Parse(await BytesAsync(uri, 2 * 1024 * 1024, ct));
    public async Task DownloadAsync(Uri uri, string target, long limit, string? sha256, CancellationToken ct, IProgress<MoskomTransferProgress>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var part = target + ".part";
        try
        {
            using var response = await OpenAsync(uri, ct);
            var size = response.Content.Headers.ContentLength;
            if (size > limit) throw new InvalidOperationException("Файл обновления превышает допустимый размер.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                using var input = await response.Content.ReadAsStreamAsync(ct); var buffer = new byte[65536]; long received = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    received += count; if (received > limit) throw new InvalidOperationException("Файл обновления превышает допустимый размер.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress?.Report(new(Path.GetFileName(target), received, size));
                }
                if (size != null && received != size) throw new IOException("Загрузка файла прервалась. Повторите попытку.");
                await output.FlushAsync(ct);
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (sha256 != null && !string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Контрольная сумма не совпала. Обновление не применено.");
            ct.ThrowIfCancellationRequested(); File.Move(part, target, true);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }
}
