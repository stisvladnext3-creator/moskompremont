namespace ServiceLib.Services;

public static class MoskomUpdateNotes
{
    public static string Desktop =>
        "• Флаги стран в списке серверов отображаются как графические значки.\n" +
        "• Сохранены подписки, выбор сервера и настройки маршрутизации.\n" +
        "• Установщик проверяется по SHA-256 перед обновлением.";

    public static string Summarize(string? body, string fallback = "")
    {
        var lines = (body ?? "").Replace("\r", "").Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal))
            .Select(x => x.StartsWith("-", StringComparison.Ordinal) || x.StartsWith("*", StringComparison.Ordinal) ? "•" + x[1..].Trim() : "• " + x)
            .Take(5).ToList();
        return lines.Count == 0 ? fallback : string.Join("\n", lines);
    }
}
