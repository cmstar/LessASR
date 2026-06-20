using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace LocalAsrClient.App.Diagnostics;

public sealed class JsonlDiagnosticEventSink : IDiagnosticEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _sequenceId;

    private JsonlDiagnosticEventSink(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static JsonlDiagnosticEventSink Create(string diagnosticsDirectory)
    {
        Directory.CreateDirectory(diagnosticsDirectory);
        var fileName = $"diagnostics-{DateTime.Now:yyyy-MM-dd-HHmmss}-p{Environment.ProcessId}.jsonl";
        return new JsonlDiagnosticEventSink(Path.Combine(diagnosticsDirectory, fileName));
    }

    public async Task WriteAsync(DiagnosticEvent diagnosticEvent)
    {
        var next = diagnosticEvent with
        {
            SequenceId = Interlocked.Increment(ref _sequenceId),
            Timestamp = diagnosticEvent.Timestamp == DateTimeOffset.MinValue
                ? DateTimeOffset.Now
                : diagnosticEvent.Timestamp
        };

        var line = JsonSerializer.Serialize(next, JsonOptions);
        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(FilePath, line + Environment.NewLine);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writeLock.WaitAsync();
        _writeLock.Release();
        _writeLock.Dispose();
    }
}
