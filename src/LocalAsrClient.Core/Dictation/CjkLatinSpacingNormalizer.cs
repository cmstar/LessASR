using System.Globalization;
using System.Text.RegularExpressions;

namespace LocalAsrClient.Core.Dictation;

public static class CjkLatinSpacingNormalizer
{
    private static readonly string[] ProtectedWords = LoadProtectedWords();

    private static readonly Regex ProtectedText = new(
        "```[\\s\\S]*?```|`[^`\\r\\n]*`|(?:https?://|www\\.)[^\\s<>\"“”‘’「」『』，。！？；：（）【】]+"
        + "|[\\p{L}\\p{N}._%+-]+@[\\p{L}\\p{N}.-]+\\.[\\p{L}]{2,}"
        + "|(?:[A-Za-z]:\\\\|\\\\\\\\|\\.{1,2}[/\\\\])[^\\s<>\"“”‘’「」『』，。！？；：（）【】]+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex Quotation = new(
        "“[^“”]*”|‘[^‘’]*’|「[^「」]*」|『[^『』]*』",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var protectedBoundaries = new bool[text.Length];
        var protectedWordCharacters = new bool[text.Length];
        foreach (Match match in ProtectedText.Matches(text))
        {
            Protect(match.Index, match.Length);
        }

        foreach (Match match in Quotation.Matches(text))
        {
            // 完整外语引文保留引号内的排版；混合文字引文仍按普通正文处理。
            if (match.Value.EnumerateRunes().Any(IsLatin)
                && !match.Value.EnumerateRunes().Any(IsEastAsianLetter))
            {
                Protect(match.Index, match.Length);
            }
        }

        foreach (var word in ProtectedWords)
        {
            var searchStart = 0;
            while (searchStart < text.Length)
            {
                var start = text.IndexOf(word, searchStart, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    break;
                }

                var end = start + word.Length;
                // 拉丁一侧必须完整匹配，避免把 XA股、卡拉OKR 中的子串误当作固定词。
                var joinsLeft = IsLatinTokenPart(Rune.GetRuneAt(word, 0)) && start > 0
                    && IsLatinTokenPart(RuneBefore(text, start));
                var joinsRight = IsLatinTokenPart(RuneBefore(word, word.Length)) && end < text.Length
                    && IsLatinTokenPart(Rune.GetRuneAt(text, end));
                if (!joinsLeft && !joinsRight)
                {
                    Protect(start, word.Length);
                    Array.Fill(protectedWordCharacters, true, start, word.Length);
                }

                searchStart = start + 1;
            }
        }

        var builder = new StringBuilder(text.Length);
        var runes = text.EnumerateRunes().ToArray();
        var scripts = runes.Select(Classify).ToArray();
        for (var i = 0; i < runes.Length; i++)
        {
            if (runes[i].Value is '’' or '‘' && i > 0 && i + 1 < runes.Length
                && IsLatin(runes[i - 1]) && IsLatin(runes[i + 1]))
            {
                scripts[i] = Script.Other;
            }

            if (runes[i].Value is not (>= '0' and <= '9'))
            {
                continue;
            }

            var start = i;
            while (i + 1 < runes.Length && runes[i + 1].Value is >= '0' and <= '9')
            {
                i++;
            }

            // GPT4、3D 等词中的数字随拉丁字母处理，纯数字保持原样。
            if ((start > 0 && scripts[start - 1] == Script.Latin)
                || (i + 1 < scripts.Length && scripts[i + 1] == Script.Latin))
            {
                Array.Fill(scripts, Script.Latin, start, i - start + 1);
            }
        }

        var previous = Script.Other;
        var offset = 0;
        for (var i = 0; i < runes.Length; i++)
        {
            var rune = runes[i];
            var category = Rune.GetUnicodeCategory(rune);
            var isMark = category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            // 固定词整词视为中文，词首或词尾的拉丁字母不再触发中文侧的空格。
            var current = protectedWordCharacters[offset]
                ? Script.EastAsian
                : isMark ? previous : scripts[i];
            if (!isMark && offset > 0 && !protectedBoundaries[offset]
                && ((previous == Script.EastAsian && current == Script.Latin)
                    || (previous == Script.Latin && current == Script.EastAsian)))
            {
                builder.Append(' ');
            }

            builder.Append(rune);
            previous = current;
            offset += rune.Utf16SequenceLength;
        }

        return builder.ToString();

        void Protect(int start, int length)
        {
            Array.Fill(protectedBoundaries, true, start + 1, length - 1);
        }
    }

    private static string[] LoadProtectedWords()
    {
        using var stream = typeof(CjkLatinSpacingNormalizer).Assembly.GetManifestResourceStream(
            "LocalAsrClient.Core.Dictation.MixedScriptProtectedWords.txt")
            ?? throw new InvalidOperationException("缺少内置混合文字保护词表。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Rune RuneBefore(string text, int end)
    {
        var index = end - 1;
        if (char.IsLowSurrogate(text[index]) && index > 0 && char.IsHighSurrogate(text[index - 1]))
        {
            index--;
        }

        return Rune.GetRuneAt(text, index);
    }

    private static bool IsLatinTokenPart(Rune rune)
    {
        return IsLatin(rune) || Rune.IsDigit(rune) || rune.Value == '_'
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static Script Classify(Rune rune)
    {
        if (IsLatin(rune))
        {
            return Script.Latin;
        }

        return IsEastAsianLetter(rune) || "，。！？：；（）【】《》〈〉「」『』、“”‘’".Contains(rune.ToString(), StringComparison.Ordinal)
            ? Script.EastAsian
            : Script.Other;
    }

    private static bool IsLatin(Rune rune)
    {
        return Rune.IsLetter(rune) && rune.Value is >= 0x0041 and <= 0x007A
            or >= 0x00C0 and <= 0x02AF
            or >= 0x1D00 and <= 0x1DBF
            or >= 0x1E00 and <= 0x1EFF
            or >= 0x2C60 and <= 0x2C7F
            or >= 0xA720 and <= 0xA7FF
            or >= 0xAB30 and <= 0xAB6F
            or >= 0xFF21 and <= 0xFF3A
            or >= 0xFF41 and <= 0xFF5A
            or >= 0x10780 and <= 0x107BF
            or >= 0x1DF00 and <= 0x1DFFF;
    }

    private static bool IsEastAsianLetter(Rune rune)
    {
        return Rune.IsLetter(rune) && (CjkScriptHelper.IsHan(rune.Value)
            || rune.Value is >= 0x2E80 and <= 0x2FFF
                or >= 0x3040 and <= 0x30FF
                or >= 0x31F0 and <= 0x31FF
                or >= 0x1100 and <= 0x11FF
                or >= 0x3130 and <= 0x318F
                or >= 0xA960 and <= 0xA97F
                or >= 0xAC00 and <= 0xD7FF
                or >= 0xF900 and <= 0xFAFF
                or >= 0xFF66 and <= 0xFFDC
                or >= 0x1AFF0 and <= 0x1B16F
                or >= 0x2F800 and <= 0x2FA1F
                or >= 0x31350 and <= 0x323AF
                or >= 0x2EBF0 and <= 0x2EE5F
                or 0x3005 or 0x3006 or 0x3007);
    }

    private enum Script
    {
        Other,
        Latin,
        EastAsian
    }
}
