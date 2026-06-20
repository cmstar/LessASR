namespace LocalAsrClient.Core.Asr;

public static class WhisperServerStartupArguments
{
    public static string Build(WhisperServerOptions options)
    {
        return $"--host {options.Host} --port {options.Port} --max-context 0 -m \"{options.ModelPath}\"";
    }
}
