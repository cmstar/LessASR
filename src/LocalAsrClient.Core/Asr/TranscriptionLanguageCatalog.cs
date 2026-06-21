namespace LocalAsrClient.Core.Asr;

public sealed record TranscriptionLanguageOption(string Id, string DisplayName);

public static class TranscriptionLanguageCatalog
{
    public const string DefaultId = "auto";

    private static readonly TranscriptionLanguageOption[] Options =
    [
        new("auto", "（自动）"),
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁体中文"),
        new("en", "English"),
        new("ar", "阿拉伯语"),
        new("de", "德语"),
        new("ru", "俄语"),
        new("fr", "法语"),
        new("ko", "한국어"),
        new("it", "意大利语"),
        new("ja", "日本語"),
        new("pt", "葡萄牙语"),
        new("es", "西班牙语"),
        new("th", "泰语"),
        new("vi", "越南语")
    ];

    private static readonly Dictionary<string, TranscriptionLanguageOption> OptionsById =
        Options.ToDictionary(option => option.Id, StringComparer.Ordinal);

    public static IReadOnlyList<TranscriptionLanguageOption> All { get; } = Options;

    public static bool TryGet(string? id, out TranscriptionLanguageOption option)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            option = OptionsById[DefaultId];
            return false;
        }

        return OptionsById.TryGetValue(id, out option!);
    }

    public static string NormalizeId(string? id)
    {
        return TryGet(id, out _) ? id! : DefaultId;
    }

    public static string? ResolveLanguage(string? id)
    {
        var normalizedId = NormalizeId(id);
        return normalizedId switch
        {
            DefaultId => null,
            "zh-Hans" or "zh-Hant" => "zh",
            _ => OptionsById[normalizedId].Id
        };
    }
}
