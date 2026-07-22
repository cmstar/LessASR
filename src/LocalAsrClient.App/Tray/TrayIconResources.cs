using System.Drawing;
using Microsoft.Win32;

namespace LocalAsrClient.App.Tray;

internal static class TrayIconResources
{
    internal const string DarkGlyphResourceName =
        "LocalAsrClient.App.Assets.Brand.LessASR.Tray.Dark.ico";

    internal const string LightGlyphResourceName =
        "LocalAsrClient.App.Assets.Brand.LessASR.Tray.Light.ico";

    internal static string SelectResourceName(bool systemUsesLightTheme) =>
        systemUsesLightTheme ? DarkGlyphResourceName : LightGlyphResourceName;

    internal static Icon Load(bool systemUsesLightTheme)
    {
        var resourceName = SelectResourceName(systemUsesLightTheme);
        using var stream = typeof(TrayIconResources).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"找不到托盘图标资源：{resourceName}");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    internal static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is not int value || value != 0;
    }
}
