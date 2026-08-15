using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface IRemoteApiProfileRepository
{
    Task<IReadOnlyList<RemoteApiProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task<RemoteApiProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<RemoteApiProfile> CreateAsync(
        string name,
        string endpoint,
        string model,
        string? protectedApiKey,
        bool useVocabulary,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        Guid id,
        string name,
        string endpoint,
        string model,
        string? protectedApiKey,
        bool useVocabulary,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
