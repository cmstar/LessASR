namespace LocalAsrClient.Core.Dictation;

internal static class CjkScriptHelper
{
    public static bool IsHan(int codePoint)
    {
        return codePoint is >= 0x4E00 and <= 0x9FFF
            or >= 0x3400 and <= 0x4DBF
            or >= 0x20000 and <= 0x2A6DF
            or >= 0x2A700 and <= 0x2B73F
            or >= 0x2B740 and <= 0x2B81F
            or >= 0x2B820 and <= 0x2CEAF
            or >= 0x2CEB0 and <= 0x2EBEF
            or >= 0x30000 and <= 0x3134F;
    }

    public static bool IsAsciiLetterOrDigit(int codePoint)
    {
        return codePoint is >= '0' and <= '9'
            or >= 'A' and <= 'Z'
            or >= 'a' and <= 'z';
    }
}
