using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Abstractions;

public interface IAsrBackend
{
    string Name { get; }
    string ModelId { get; }
    AsrBackendStatus Status { get; }
    Task EnsureReadyAsync(CancellationToken cancellationToken);
    Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken);
}
