namespace LocalAsrClient.Core.Abstractions;

public interface IAppLog
{
    void Write(string context, string message);
}
