namespace LocalAsrClient.Core.Asr;

public static class WhisperServerStartupArguments
{
    public static string Build(WhisperServerOptions options)
    {
        var threads = WhisperServerThreadCount.RecommendForCurrentMachine();
        return $"--host {options.Host} --port {options.Port} --threads {threads} --max-context 0 -m \"{options.ModelPath}\"";
    }
}
