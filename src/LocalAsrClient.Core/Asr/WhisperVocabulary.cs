using System.Globalization;

namespace LocalAsrClient.Core.Asr;

public sealed record WhisperVocabularyParseResult(
    IReadOnlyList<string> Entries,
    string NormalizedText,
    string? ErrorMessage)
{
    public bool IsValid => ErrorMessage is null;
}

public static class WhisperVocabulary
{
    public const int MaxEntries = 100;
    public const int MaxEntryCharacters = 30;

    public static WhisperVocabularyParseResult Parse(string? text)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = NormalizeLineEndings(text ?? string.Empty).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var entry = lines[index].Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (CountTextElements(entry) > MaxEntryCharacters)
            {
                return new WhisperVocabularyParseResult(
                    entries,
                    string.Join('\n', entries),
                    $"第 {index + 1} 行超过 {MaxEntryCharacters} 个字符。");
            }

            if (!seen.Add(entry))
            {
                continue;
            }

            entries.Add(entry);
            if (entries.Count > MaxEntries)
            {
                return new WhisperVocabularyParseResult(
                    entries,
                    string.Join('\n', entries),
                    $"最多可以添加 {MaxEntries} 个词条。");
            }
        }

        return new WhisperVocabularyParseResult(
            entries,
            string.Join('\n', entries),
            ErrorMessage: null);
    }

    public static string? CreateInitialPrompt(string? vocabularyText)
    {
        var result = Parse(vocabularyText);
        return result.IsValid ? BuildInitialPrompt(result.Entries) : null;
    }

    public static string? BuildInitialPrompt(IReadOnlyList<string> entries)
    {
        return entries.Count == 0
            ? null
            : string.Join(", ", entries.Reverse());
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

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
