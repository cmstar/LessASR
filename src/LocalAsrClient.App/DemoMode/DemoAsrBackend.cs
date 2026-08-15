using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.DemoMode;

public sealed class DemoAsrBackend : IAsrBackend
{
    private readonly IReadOnlyList<string> _segments;
    private int _nextIndex;

    public DemoAsrBackend(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("演示转写文本不能为空。", nameof(segments));
        }

        _segments = segments;
    }

    public string Name => "demo-asr";
    public string ModelId => "demo-model";

    public AsrBackendStatus Status => AsrBackendStatus.Ready;

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _nextIndex) - 1;
        var text = _segments[index % _segments.Count];
        return Task.FromResult(
            new AsrResult(text, null, TimeSpan.FromMilliseconds(180), null));
    }
}
