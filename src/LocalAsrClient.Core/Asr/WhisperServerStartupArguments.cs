namespace LocalAsrClient.Core.Asr;

public static class WhisperServerStartupArguments
{
    public static string Build(WhisperServerOptions options)
    {
        var threads = WhisperServerThreadCount.Resolve(options.ThreadCount);
        return $"--host {options.Host} --port {options.Port} --threads {threads} -m \"{options.ModelPath}\"";
    }
}
