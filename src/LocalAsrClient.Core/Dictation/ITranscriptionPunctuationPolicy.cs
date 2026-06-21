namespace LocalAsrClient.Core.Dictation;

public interface ITranscriptionPunctuationPolicy
{
    bool ShouldUseChinesePunctuation(string text, string preferredLanguageId);
}
