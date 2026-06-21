using OpenccNetLib;

namespace LocalAsrClient.Core.Dictation;

public static class OpenCcScriptConverter
{
    private static readonly Lazy<Opencc> TraditionalToSimplified = new(() => new Opencc(OpenccConfig.T2S));
    private static readonly Lazy<Opencc> SimplifiedToTraditional = new(() => new Opencc(OpenccConfig.S2T));

    public static string Convert(string text, string preferredLanguageId)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return preferredLanguageId switch
        {
            "zh-Hans" => TraditionalToSimplified.Value.Convert(text),
            "zh-Hant" => SimplifiedToTraditional.Value.Convert(text),
            _ => text
        };
    }
}
