namespace LocalAsrClient.Core.Abstractions;

public interface IAudioLevelSource
{
    event Action<float>? AudioLevelChanged;
}
