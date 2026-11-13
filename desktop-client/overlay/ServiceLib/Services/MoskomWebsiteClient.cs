using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Services;

public sealed class MoskomWebsiteException(string message, HttpStatusCode status) : InvalidOperationException(message)
{
    public HttpStatusCode Status { get; } = status;
}

// The ordinary website API is used with its existing Origin, CSRF and cookie protections.
// Passwords are sent only to the fixed HTTPS site and are never stored by this client.
public sealed class MoskomWebsiteClient : IDisposable
{
    public static readonly Uri Site = new("https://moskompremont.ru/");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly HttpClient _http;
    private string _csrf = "";
    public Uri Root { get; }
    public CookieContainer Cookies { get; } = new();

    public MoskomWebsiteClient(Uri? root = null, HttpMessageHandler? handler = null)
    {
        Root = root ?? Site;
        if (Root.Scheme != "https" && !(Root.Scheme == "http" && Root.IsLoopback)) throw new ArgumentException("Для сайта требуется HTTPS.");
        _http = new HttpClient(handler ?? new HttpClientHandler
        {
            CookieContainer = Cookies, UseCookies = true, UseProxy = false, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MoskomVPN/0.2 (+https://moskompremont.ru/)");
    }

    public async Task<WebsiteSession> MeAsync(CancellationToken ct = default)
    {
        var result = await SendAsync<WebsiteSession>("api/account.php?action=me", null, ct);
        _csrf = result.Csrf ?? ""; return result;
    }
    public async Task<WebsiteSession> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        await MeAsync(ct);
        var result = await SendAsync<WebsiteSession>("api/account.php?action=login", new { email = email.Trim(), password, remember = true }, ct);
        if (result.User == null) throw new InvalidOperationException("Сайт не подтвердил вход.");
        _csrf = result.Csrf ?? ""; return result;
    }
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_csrf)) await MeAsync(ct);
        await SendAsync<WebsiteSession>("api/account.php?action=logout", new { }, ct);
    }
    public async Task<IReadOnlyList<WebsiteSubscription>> SubscriptionsAsync(CancellationToken ct = default)
    {
        var result = await SendAsync<SubscriptionReply>("api/account.php?action=subscriptions", null, ct);
        if (result.Subscriptions == null || result.Subscriptions.Count > 1000 || result.Subscriptions.Any(s => s.Id <= 0) || result.Subscriptions.Select(s => s.Id).Distinct().Count() != result.Subscriptions.Count)
            throw new InvalidOperationException("Не удалось прочитать список подписок сайта.");
        foreach (var sub in result.Subscriptions.Where(s => !s.IsExpired && !string.IsNullOrEmpty(s.Url)))
            if (!Uri.TryCreate(sub.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("Сайт вернул некорректную ссылку подписки.");
        return result.Subscriptions;
    }
    public Task<WebsiteMonitor> MonitorAsync(CancellationToken ct = default) => SendAsync<WebsiteMonitor>("api/server-status.php", null, ct);
    public Task<WebsiteNotice> NoticeAsync(CancellationToken ct = default) => SendAsync<WebsiteNotice>("api/service-notice.php", null, ct);

    private async Task<T> SendAsync<T>(string path, object? body, CancellationToken ct, bool retryCsrf = true)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(Root, path));
        if (body != null)
        {
            request.Headers.TryAddWithoutValidation("Origin", Root.GetLeftPart(UriPartial.Authority));
            request.Headers.TryAddWithoutValidation("X-CSRF-Token", _csrf);
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        using var input = await response.Content.ReadAsStreamAsync(ct); using var bytes = new MemoryStream();
        var buffer = new byte[16384]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + read > 2 * 1024 * 1024) throw new InvalidOperationException("Ответ сайта превышает допустимый размер.");
            bytes.Write(buffer, 0, read);
        }
        WebsiteError? error = null;
        try { error = JsonSerializer.Deserialize<WebsiteError>(bytes.ToArray(), Json); } catch (JsonException) { }
        if (!response.IsSuccessStatusCode)
        {
            if (body != null && retryCsrf && response.StatusCode == HttpStatusCode.Forbidden && error?.Code == "csrf_refresh" && !string.IsNullOrEmpty(error.Csrf))
            { _csrf = error.Csrf; return await SendAsync<T>(path, body, ct, false); }
            var message = error?.Error;
            throw new MoskomWebsiteException(!string.IsNullOrWhiteSpace(message) && message.Length < 500 ? message : "Сайт временно недоступен. Повторите запрос позже.", response.StatusCode);
        }
        try { return JsonSerializer.Deserialize<T>(bytes.ToArray(), Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidOperationException("Не удалось прочитать ответ сайта. Попробуйте позже."); }
    }
    public void Dispose() => _http.Dispose();
    private sealed class WebsiteError { public string? Error { get; set; } public string? Code { get; set; } public string? Csrf { get; set; } }
    private sealed class SubscriptionReply { public List<WebsiteSubscription>? Subscriptions { get; set; } }
}

public sealed class WebsiteSession { public WebsiteUser? User { get; set; } public string? Csrf { get; set; } }
public sealed class WebsiteUser
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string FullName => (FirstName + " " + LastName).Trim();
}
public sealed class WebsiteSubscription
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Url { get; set; }
    public bool IsExpired { get; set; }
    public string? ExpiresAt { get; set; }
    public string? ContentUpdatedAt { get; set; }
    public bool Usable(DateTimeOffset now) => !IsExpired && !string.IsNullOrWhiteSpace(Url) && (WebsiteTime.Parse(ExpiresAt) is not { } end || end > now);
}
public sealed class WebsiteNotice
{
    public bool Enabled { get; set; }
    public string Message { get; set; } = "";
    public string Level { get; set; } = "warning";
    public string Title => !Enabled ? "Статус сервиса не опубликован" : Level switch { "ok" => "Сервис работает без сбоев", "down" => "Сервис временно недоступен", _ => "Возможны перебои" };
}
public sealed class WebsiteMonitor
{
    public bool Enabled { get; set; }
    public List<WebsiteNode> Nodes { get; set; } = [];
    public WebsiteNode? Main { get; set; }
    public string? ReportedAt { get; set; }
    public string? SourceStatus { get; set; }
}
public sealed class WebsiteNode
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public int? LatencyMs { get; set; }
    public string? CheckedAt { get; set; }
    public string EffectiveState(DateTimeOffset now) => WebsiteTime.Fresh(CheckedAt, now) && Status is "up" or "down" or "unsupported" ? Status : "unknown";
    public string Display(DateTimeOffset now)
    {
        var state = EffectiveState(now);
        return state switch { "up" => "Доступен" + (LatencyMs is > 0 ? $" · {LatencyMs} мс" : ""), "down" => "Недоступен", "unsupported" => "Требуется настройка", _ => "Нет свежих данных" };
    }
}
public static class WebsiteTime
{
    public static DateTimeOffset? Parse(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ? time : null;
    public static bool Fresh(string? value, DateTimeOffset now) => Parse(value) is { } t && now - t <= TimeSpan.FromSeconds(180) && t - now <= TimeSpan.FromSeconds(120);
}
