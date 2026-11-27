using System.Text.Json;
using ServiceLib.Services;
using Xunit;

public sealed class SubscriptionTermTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-27T20:00:00Z");
    private static MoskomSubscriptionTerm Term(string kind, long? remaining, string? end = null, bool expired = false)
        => MoskomSubscriptionTerm.From(new() { TermKind = kind, RemainingSeconds = remaining, ExpiresAt = end, IsExpired = expired }, Now, 1000);
    [Theory]
    [InlineData(223200, "Осталось: 2 дн. 14 ч.")]
    [InlineData(7260, "Осталось: 2 ч. 1 мин.")]
    [InlineData(120, "Осталось: 2 мин.")]
    [InlineData(59, "Осталось меньше минуты")]
    [InlineData(0, "Срок истёк")]
    public void CountdownMatchesCabinet(long seconds, string text) => Assert.Equal(text, Term("fixed", seconds).Text(1000));
    [Fact] public void MissingOrUnspecifiedTermNeverBecomesLifetime()
    {
        Assert.Equal("Бессрочная подписка", Term("lifetime", null).Text(100000));
        Assert.Equal("Срок не указан", Term("unspecified", null).Text(1000));
        Assert.Equal("Срок не указан", Term("fixed", null, "bad-date").Text(1000));
        Assert.Equal("Срок истёк", Term("fixed", 60, expired: true).Text(1000));
    }
    [Fact] public void ElapsedClockCountsDownWithoutUsingDeviceWallTime()
    {
        var term = Term("fixed", 120, "2000-01-01 00:00:00");
        Assert.Equal("Осталось: 2 мин.", term.Text(1000));
        Assert.Equal("Осталось: 1 мин.", term.Text(61000));
        Assert.Equal("Срок истёк", term.Text(121000));
        Assert.Equal("Осталось: 2 мин.", term.Text(0));
    }
    [Fact] public void SqlUtcAndIsoDatesDisplaySameMoscowDate()
    {
        foreach (var date in new[] { "2026-11-27 21:30:00", "2026-11-27T21:30:00Z" })
            Assert.Equal("Действует до: 28.11.2026, 00:30 (МСК)", Term("fixed", 5400, date).EndText);
        Assert.Null(Term("lifetime", null).EndText);
    }
    [Fact] public void CachedTermRestoresExpiryWithoutCredentialsAndRenewalReplacesDeadline()
    {
        var old = Term("fixed", 60, "2026-11-27T20:01:00Z");
        var data = JsonSerializer.Deserialize<MoskomSubscriptionTermData>(JsonSerializer.Serialize(old.Data))!;
        var restored = MoskomSubscriptionTerm.FromCache(data, Now.AddMinutes(2), 0);
        Assert.Equal("Срок истёк", restored.Text(0));
        var renewed = Term("fixed", 86400, "2026-11-28T20:00:00Z");
        Assert.Equal("Осталось: 1 дн. 0 ч.", renewed.Text(1000));
        Assert.Equal("fixed", renewed.State(1000));
        Assert.DoesNotContain("url", JsonSerializer.Serialize(data), StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void ActualWebsiteFieldsDeserializeIncludingNullRemainingTime()
    {
        var rows = JsonSerializer.Deserialize<WebsiteSubscription[]>("""[{"id":1,"term_kind":"lifetime","remaining_seconds":null},{"id":2,"term_kind":"fixed","remaining_seconds":90000,"expires_at":"2026-11-29 00:00:00"}]""", MoskomWebsiteClient.Json)!;
        Assert.Equal("Бессрочная подписка", MoskomSubscriptionTerm.From(rows[0], Now, 0).Text(0));
        Assert.Equal("Осталось: 1 дн. 1 ч.", MoskomSubscriptionTerm.From(rows[1], Now, 0).Text(0));
    }
}
