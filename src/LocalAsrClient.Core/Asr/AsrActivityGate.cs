namespace LocalAsrClient.Core.Asr;

/// <summary>
/// Serializes a dictation session with service mutations so the selected backend
/// cannot change between recording and persistence.
/// </summary>
public sealed class AsrActivityGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<AsrActivityLease?> TryEnterAsync(CancellationToken cancellationToken)
    {
        return await _gate.WaitAsync(0, cancellationToken)
            ? new AsrActivityLease(_gate)
            : null;
    }
}

public sealed class AsrActivityLease : IAsyncDisposable
{
    private SemaphoreSlim? _gate;

    internal AsrActivityLease(SemaphoreSlim gate)
    {
        _gate = gate;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _gate, null)?.Release();
        return ValueTask.CompletedTask;
    }
}
