using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerProcessManagerStatusTests
{
    [Fact]
    public async Task EnsureStartedAsync_WhenConfigurationIsInvalid_PublishesFailedStatus()
    {
        var manager = new WhisperServerProcessManager(new WhisperServerOptions(
            "missing-whisper-server.exe",
            "missing-model.bin",
            "127.0.0.1",
            18080));
        var publishedStatuses = new List<WhisperServerStatus>();
        manager.StatusChanged += publishedStatuses.Add;

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            manager.EnsureStartedAsync(CancellationToken.None));

        Assert.Equal(WhisperServerStatus.Failed, manager.Status);
        Assert.Equal([WhisperServerStatus.Failed], publishedStatuses);
    }
}
