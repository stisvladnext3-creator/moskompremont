using System;
using System.Globalization;

namespace ServiceLib.Services;

// Cached metadata deliberately contains no subscription URL or account secret.
public sealed record MoskomSubscriptionTermData
{
    public string Kind { get; init; } = "unspecified";
    public string? ExpiresAt { get; init; }
    public bool Expired { get; init; }
}

public sealed class MoskomSubscriptionTerm
{
    public MoskomSubscriptionTermData Data { get; }
    private readonly long? _remaining;
    private readonly long _observed;
    private MoskomSubscriptionTerm(MoskomSubscriptionTermData data, long? remaining, long observed)
    { Data = data; _remaining = remaining; _observed = observed; }

    public static MoskomSubscriptionTerm From(WebsiteSubscription row, DateTimeOffset now, long elapsedMillis)
        => Create(new() { Kind = row.TermKind, ExpiresAt = row.ExpiresAt, Expired = row.IsExpired }, row.RemainingSeconds, now, elapsedMillis);
    public static MoskomSubscriptionTerm FromCache(MoskomSubscriptionTermData data, DateTimeOffset now, long elapsedMillis)
        => Create(data, null, now, elapsedMillis);
    private static MoskomSubscriptionTerm Create(MoskomSubscriptionTermData data, long? seconds, DateTimeOffset now, long elapsedMillis)
    {
        long? remaining = null;
        if (data.Kind == "fixed")
            remaining = seconds is { } provided ? Math.Max(0, provided)
                : WebsiteTime.Parse(data.ExpiresAt) is { } end ? Math.Max(0, (long)Math.Ceiling((end - now).TotalSeconds)) : null;
        return new(data, remaining, elapsedMillis);
    }
    public long? Remaining(long elapsedMillis) => _remaining is { } seconds
        ? Math.Max(0, seconds - Math.Max(0, elapsedMillis - _observed) / 1000) : null;
    public string State(long elapsedMillis) => Data.Expired || Remaining(elapsedMillis) == 0 ? "expired"
        : Data.Kind == "lifetime" ? "lifetime" : Data.Kind == "fixed" && _remaining.HasValue ? "fixed" : "unspecified";
    public string Text(long elapsedMillis)
    {
        var state = State(elapsedMillis);
        if (state == "expired") return "Срок истёк";
        if (state == "lifetime") return "Бессрочная подписка";
        if (state != "fixed" || Remaining(elapsedMillis) is not { } seconds) return "Срок не указан";
        var days = seconds / 86400; var hours = seconds % 86400 / 3600; var minutes = seconds % 3600 / 60;
        return days > 0 ? $"Осталось: {days} дн. {hours} ч."
            : hours > 0 ? $"Осталось: {hours} ч. {minutes} мин."
            : minutes > 0 ? $"Осталось: {minutes} мин." : "Осталось меньше минуты";
    }
    public string? EndText => Data.Kind == "fixed" && WebsiteTime.Parse(Data.ExpiresAt) is { } end
        ? "Действует до: " + end.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture) + " (МСК)" : null;
}
