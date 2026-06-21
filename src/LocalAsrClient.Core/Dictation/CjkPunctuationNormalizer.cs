using System.Text;

namespace LocalAsrClient.Core.Dictation;

public static class CjkPunctuationNormalizer
{
    private static readonly Dictionary<int, int> AsciiToFullWidth = new()
    {
        [','] = '，',
        ['.'] = '。',
        ['?'] = '？',
        ['!'] = '！',
        [':'] = '：',
        [';'] = '；',
        ['('] = '（',
        [')'] = '）',
    };

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var runes = text.EnumerateRunes().ToArray();
        if (runes.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < runes.Length; i++)
        {
            var rune = runes[i];
            if (AsciiToFullWidth.TryGetValue(rune.Value, out var fullWidth)
                && ShouldConvert(runes, i))
            {
                builder.Append(char.ConvertFromUtf32(fullWidth));
            }
            else
            {
                builder.Append(rune);
            }
        }

        return builder.ToString();
    }

    private static bool ShouldConvert(Rune[] runes, int index)
    {
        var left = NearestSignificant(runes, index, -1);
        var right = NearestSignificant(runes, index, 1);
        var punctuation = runes[index].Value;

        if (punctuation == '.')
        {
            if (left >= 0 && right >= 0
                && CjkScriptHelper.IsAsciiLetterOrDigit(left)
                && CjkScriptHelper.IsAsciiLetterOrDigit(right))
            {
                return false;
            }

            if (right >= 0 && CjkScriptHelper.IsAsciiLetterOrDigit(right))
            {
                return false;
            }
        }

        return (left >= 0 && CjkScriptHelper.IsHan(left))
            || (right >= 0 && CjkScriptHelper.IsHan(right));
    }

    private static int NearestSignificant(Rune[] runes, int index, int direction)
    {
        for (var i = index + direction; i >= 0 && i < runes.Length; i += direction)
        {
            if (!char.IsWhiteSpace((char)runes[i].Value) && runes[i].Value != '\u3000')
            {
                return runes[i].Value;
            }
        }

        return -1;
    }
}
