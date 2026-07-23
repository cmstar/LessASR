namespace LocalAsrClient.App.Dialogs;

public enum ConfirmationDialogTone
{
    Standard,
    Destructive
}

public sealed record ConfirmationDialogOptions
{
    private const int PreviewCharacterLimit = 160;

    public required string Title { get; init; }

    public required string Heading { get; init; }

    public required string Message { get; init; }

    public string ConfirmText { get; init; } = "确认";

    public string CancelText { get; init; } = "取消";

    public string? Preview { get; init; }

    public ConfirmationDialogTone Tone { get; init; } = ConfirmationDialogTone.Standard;

    public bool IsConfirmDefault { get; init; }

    public bool IsCancelDefault => !IsConfirmDefault;

    public string? DisplayedPreview
    {
        get
        {
            var normalized = Preview?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            return normalized.Length <= PreviewCharacterLimit
                ? normalized
                : $"{normalized[..PreviewCharacterLimit]}…";
        }
    }

    public bool HasPreview => DisplayedPreview is not null;
}
