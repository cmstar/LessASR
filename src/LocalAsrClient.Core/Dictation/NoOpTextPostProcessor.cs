namespace LocalAsrClient.Core.Dictation;

public interface ITextPostProcessor
{
    Task<string> ProcessAsync(string text, CancellationToken cancellationToken);
}

public sealed class NoOpTextPostProcessor : ITextPostProcessor
{
    public Task<string> ProcessAsync(string text, CancellationToken cancellationToken)
    {
        return Task.FromResult(text);
    }
}
