using System.Diagnostics;

namespace LocalAsrClient.App.Tests.E2E;

public sealed class ProcessRunner : IAsyncDisposable
{
    private readonly List<(Process Process, string Arguments)> _processes = [];

    public Process Start(string fileName, string arguments = "", IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        _processes.Add((process, arguments));
        return process;
    }

    public ValueTask DisposeAsync()
    {
        if (ShouldLeaveProcessesRunning())
        {
            return ValueTask.CompletedTask;
        }

        foreach (var (process, _) in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
            }

            process.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private bool ShouldLeaveProcessesRunning()
    {
        return _processes.Any(entry =>
            entry.Arguments.Contains("--pause", StringComparison.OrdinalIgnoreCase));
    }
}
