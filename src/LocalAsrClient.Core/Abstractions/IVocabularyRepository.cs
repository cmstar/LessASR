using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface IVocabularyRepository
{
    Task<IReadOnlyList<VocabularyProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task<VocabularyProfile?> GetActiveAsync(CancellationToken cancellationToken);

    Task<VocabularyProfile> CreateAsync(string name, CancellationToken cancellationToken);

    Task UpdateAsync(
        Guid id,
        string name,
        string entriesText,
        CancellationToken cancellationToken);

    Task SetActiveAsync(Guid? id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
