using System.Globalization;

namespace LocalAsrClient.Core.Persistence;

public sealed record VocabularyProfile(
    Guid Id,
    string Name,
    string EntriesText,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record VocabularyProfileNameResult(string NormalizedName, string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;
}

public static class VocabularyProfileName
{
    public const int MaxCharacters = 30;

    public static VocabularyProfileNameResult Validate(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return new VocabularyProfileNameResult(normalized, "词汇表名称不能为空。");
        }

        if (CountTextElements(normalized) > MaxCharacters)
        {
            return new VocabularyProfileNameResult(
                normalized,
                $"词汇表名称最多 {MaxCharacters} 个字符。");
        }

        return new VocabularyProfileNameResult(normalized, ErrorMessage: null);
    }

    private static int CountTextElements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        var count = 0;
        while (enumerator.MoveNext())
        {
            count++;
        }

        return count;
    }
}
