using System.Globalization;

namespace LocalAsrClient.Core.Utilities;

public static class TextMetrics
{
    public static int CountCharacters(string text)
    {
        return text.Count(c => !char.IsWhiteSpace(c));
    }

    public static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        var inLatinWord = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsCjk(rune))
            {
                if (inLatinWord)
                {
                    inLatinWord = false;
                }

                count++;
                continue;
            }

            if (IsLatinWordRune(rune))
            {
                if (!inLatinWord)
                {
                    count++;
                    inLatinWord = true;
                }

                continue;
            }

            inLatinWord = false;
        }

        return count;
    }

    private static bool IsLatinWordRune(Rune rune)
    {
        return Rune.IsLetterOrDigit(rune) || rune.Value == '_' || rune.Value == '-';
    }

    private static bool IsCjk(Rune rune)
    {
        return rune.Value is >= 0x4E00 and <= 0x9FFF;
    }
}