using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Asr;

public sealed class SwitchableAsrBackend : IAsrBackend
{
    private IAsrBackend _current;

    public SwitchableAsrBackend(IAsrBackend initialBackend)
    {
        _current = initialBackend ?? throw new ArgumentNullException(nameof(initialBackend));
    }

    public IAsrBackend Current => Volatile.Read(ref _current);
    public string Name => Current.Name;
    public string ModelId => Current.ModelId;
    public AsrBackendStatus Status => Current.Status;

    public void Replace(IAsrBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        Interlocked.Exchange(ref _current, backend);
    }

    public Task EnsureReadyAsync(CancellationToken cancellationToken) =>
        Current.EnsureReadyAsync(cancellationToken);

    public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken) =>
        Current.TranscribeAsync(request, cancellationToken);
}
