namespace LocalAsrClient.Core.Text;

public enum TextInjectionStatus
{
    Success,
    NoEditableTarget,
    PermissionDenied,
    UnsupportedTarget,
    Failed
}

public sealed record TextInjectionResult(TextInjectionStatus Status, string? Message)
{
    public bool Succeeded => Status == TextInjectionStatus.Success;
}
