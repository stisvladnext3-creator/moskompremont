using System.Net;
using System.Text;
using System.Text.Json;
using ServiceLib.Services;
using Xunit;

public sealed class WebsiteTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    [Fact] public async Task LoginUsesWebsiteOriginCsrfAndRememberWithoutPuttingPasswordInUrl()
    {
        var calls = 0;
        using var client = new MoskomWebsiteClient(handler: new Handler(async r =>
        {
            calls++;
            if (r.Method == HttpMethod.Get) return Reply("{\"user\":null,\"csrf\":\"token-one\"}");
            Assert.Equal("https://moskompremont.ru", r.Headers.GetValues("Origin").Single());
            Assert.Equal("token-one", r.Headers.GetValues("X-CSRF-Token").Single());
            Assert.DoesNotContain("secret-password", r.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal("person@example.test", body.RootElement.GetProperty("email").GetString());
            Assert.Equal("secret-password", body.RootElement.GetProperty("password").GetString());
            Assert.True(body.RootElement.GetProperty("remember").GetBoolean());
            return Reply("{\"user\":{\"id\":3,\"first_name\":\"Тест\",\"last_name\":\"Клиент\"},\"csrf\":\"token-two\"}");
        }));
        var result = await client.LoginAsync(" person@example.test ", "secret-password");
        Assert.Equal(3, result.User!.Id); Assert.Equal("Тест Клиент", result.User.FullName); Assert.Equal(2, calls);
    }
    [Fact] public async Task CsrfRefreshIsRetriedOnceWithNewToken()
    {
        var post = 0;
        using var client = new MoskomWebsiteClient(handler: new Handler(r =>
        {
            if (r.Method == HttpMethod.Get) return Task.FromResult(Reply("{\"csrf\":\"old\"}"));
            if (++post == 1) return Task.FromResult(Reply("{\"code\":\"csrf_refresh\",\"csrf\":\"fresh\"}", HttpStatusCode.Forbidden));
            Assert.Equal("fresh", r.Headers.GetValues("X-CSRF-Token").Single());
            return Task.FromResult(Reply("{\"user\":{\"id\":1},\"csrf\":\"next\"}"));
        }));
        await client.LoginAsync("a@example.test", "test123"); Assert.Equal(2, post);
    }
    [Fact] public async Task WrongPasswordKeeps401AndDoesNotFetchSubscriptions()
    {
        using var client = new MoskomWebsiteClient(handler: new Handler(r =>
        {
            Assert.DoesNotContain("subscriptions", r.RequestUri!.Query);
            return Task.FromResult(r.Method == HttpMethod.Get ? Reply("{\"csrf\":\"x\"}") : Reply("{\"error\":\"Неверная почта или пароль.\"}", HttpStatusCode.Unauthorized));
        }));
        var ex = await Assert.ThrowsAsync<MoskomWebsiteException>(() => client.LoginAsync("a@example.test", "wrong")); Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
    }
    [Fact] public async Task MalformedOrIncompleteSnapshotCannotBeMistakenForNoSubscriptions()
    {
        foreach (var json in new[] { "{}", "<html>error</html>", "{\"subscriptions\":[{\"id\":1},{\"id\":1}]}" })
        {
            using var client = new MoskomWebsiteClient(handler: new Handler(_ => Task.FromResult(Reply(json))));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => client.SubscriptionsAsync());
        }
    }
    [Fact] public async Task MultipleSubscriptionsIncludeChangedUrlsAndExpiredRowsWithoutUrls()
    {
        using var client = new MoskomWebsiteClient(handler: new Handler(_ => Task.FromResult(Reply("{\"subscriptions\":[{\"id\":4,\"name\":\"VPN4\",\"url\":\"https://example.test/new\",\"content_updated_at\":\"2026-11-13 20:00:00\"},{\"id\":5,\"name\":\"VPN5\",\"is_expired\":true}]}"))));
        var rows = await client.SubscriptionsAsync(); Assert.Equal(2, rows.Count); Assert.True(rows[0].Usable(DateTimeOffset.UtcNow)); Assert.False(rows[1].Usable(DateTimeOffset.UtcNow)); Assert.NotNull(rows[0].ContentUpdatedAt);
    }
    [Fact] public void PastExpiryIsUnavailableEvenWhenRemoteFlagHasNotUpdated()
    {
        var now = new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);
        Assert.False(new WebsiteSubscription { Url = "https://example.test/sub", ExpiresAt = "2026-11-13 19:59:59" }.Usable(now));
        Assert.True(new WebsiteSubscription { Url = "https://example.test/sub" }.Usable(now));
    }
    [Fact] public void StaleOrFutureMonitorDoesNotShowGreenOrOldLatency()
    {
        var now = new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);
        var fresh = new WebsiteNode { Status = "up", LatencyMs = 258, CheckedAt = "2026-11-13 19:59:00" };
        Assert.Equal("Доступен · 258 мс", fresh.Display(now));
        fresh.CheckedAt = "2026-11-13 19:50:00"; Assert.Equal("unknown", fresh.EffectiveState(now)); Assert.DoesNotContain("258", fresh.Display(now));
        fresh.CheckedAt = "2026-11-13 21:00:00"; Assert.Equal("unknown", fresh.EffectiveState(now));
    }
    [Fact] public void RemoteHttpSiteIsRejectedButLocalFixtureIsAllowed()
    {
        Assert.Throws<ArgumentException>(() => new MoskomWebsiteClient(new Uri("http://example.test/")));
        using var client = new MoskomWebsiteClient(new Uri("http://127.0.0.1:12345/"));
    }
}
