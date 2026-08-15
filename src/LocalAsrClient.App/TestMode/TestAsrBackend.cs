using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.TestMode;

public sealed class TestAsrBackend : IAsrBackend
{
    private readonly string _text;

    public TestAsrBackend(string text)
    {
        _text = text;
    }

    public string Name => "test-asr";
    public string ModelId => "test-model";

    public AsrBackendStatus Status => AsrBackendStatus.Ready;

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AsrResult(_text, null, TimeSpan.FromMilliseconds(25), null));
    }
}
