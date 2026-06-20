using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.App.Infrastructure;

public sealed class AppFileLog : IAppLog
{
    public void Write(string context, string message)
    {
        AppExceptionLogger.Write(context, message);
    }
}
