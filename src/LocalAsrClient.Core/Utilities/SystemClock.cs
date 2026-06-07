using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Utilities;

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}