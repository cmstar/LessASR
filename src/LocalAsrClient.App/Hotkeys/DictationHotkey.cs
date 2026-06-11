namespace LocalAsrClient.App.Hotkeys;

/// <summary>
/// 听写切换热键配置。默认 F10；后续可从用户设置读取。
/// </summary>
public static class DictationHotkey
{
    public static int ToggleVirtualKey => DefaultToggleVirtualKey;
    public static string ToggleDisplayName => DefaultToggleDisplayName;

    public const int DefaultToggleVirtualKey = Win32HotkeyNative.VkF10;
    public const string DefaultToggleDisplayName = "F10";
}
