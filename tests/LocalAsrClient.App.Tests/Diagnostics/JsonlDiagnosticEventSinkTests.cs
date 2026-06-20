using System.Text.Json;
using LocalAsrClient.App.Diagnostics;

namespace LocalAsrClient.App.Tests.Diagnostics;

public sealed class JsonlDiagnosticEventSinkTests
{
    [Fact]
    public async Task WriteAsyncCreatesJsonLineWithEventNameAndSequence()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var sink = JsonlDiagnosticEventSink.Create(directory);

        await sink.WriteAsync(new DiagnosticEvent(
            SequenceId: 0,
            Timestamp: DateTimeOffset.MinValue,
            EventName: "Test.Event",
            State: "Idle",
            ThreadId: 123,
            Snapshot: DiagnosticWindowSnapshot.Empty,
            Properties: new Dictionary<string, string?> { ["key"] = "value" }));

        var line = Assert.Single(File.ReadAllLines(sink.FilePath));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("sequenceId").GetInt64());
        Assert.Equal("Test.Event", document.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("value", document.RootElement.GetProperty("properties").GetProperty("key").GetString());
    }
}
