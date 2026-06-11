namespace LocalAsrClient.App.TextInjection;

internal enum TextInjectionMethod
{
    ReplaceSelectionMessage,
    ScintillaReplaceSelectionMessage,
    ClipboardPaste
}

internal static class TextInjectionStrategy
{
    private static readonly HashSet<string> ReplaceSelectionClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit",
        "RICHEDIT",
        "RichEdit20W",
        "RichEdit50W",
        "RICHEDIT50W",
        "RichEditD2DPT",
        "RICHEDIT60W",
        "ThunderRT6TextBox",
    };

    public static TextInjectionMethod Select(string className)
    {
        if (string.Equals(className, "Scintilla", StringComparison.OrdinalIgnoreCase))
        {
            return TextInjectionMethod.ScintillaReplaceSelectionMessage;
        }

        return ReplaceSelectionClassNames.Contains(className)
            ? TextInjectionMethod.ReplaceSelectionMessage
            : TextInjectionMethod.ClipboardPaste;
    }
}
