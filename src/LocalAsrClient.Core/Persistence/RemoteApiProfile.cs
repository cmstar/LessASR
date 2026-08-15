namespace LocalAsrClient.Core.Persistence;

public sealed record RemoteApiProfile(
    Guid Id,
    string Name,
    string Endpoint,
    string Model,
    string? ProtectedApiKey,
    bool UseVocabulary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
