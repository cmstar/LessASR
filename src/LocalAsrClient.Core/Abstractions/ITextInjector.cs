using LocalAsrClient.Core.Text;

namespace LocalAsrClient.Core.Abstractions;

public interface ITextInjector
{
    Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken);
}
