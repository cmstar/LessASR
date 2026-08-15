namespace LocalAsrClient.Core.Persistence;

public enum ApiKeyAvailability
{
    NotConfigured,
    Available,
    Unavailable
}

public sealed record RemoteApiProfile(
    Guid Id,
    string Name,
    string Endpoint,
    string Model,
    string? ProtectedApiKey,
    bool UseVocabulary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ProxyUrl = null)
{
    public ApiKeyAvailability ApiKeyAvailability { get; init; } =
        string.IsNullOrWhiteSpace(ProtectedApiKey)
            ? ApiKeyAvailability.NotConfigured
            : ApiKeyAvailability.Available;
}
