using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class SwitchableAsrBackendTests
{
    [Fact]
    public async Task Replace_ChangesMetadataStatusAndDelegationAtomically()
    {
        var local = new RecordingBackend("本地 Whisper", "local-model", "local result");
        var remote = new RecordingBackend("Office API", "whisper-1", "remote result");
        var backend = new SwitchableAsrBackend(local);

        Assert.Equal("本地 Whisper", backend.Name);
        Assert.Equal("local-model", backend.ModelId);

        backend.Replace(remote);
        var result = await backend.TranscribeAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("Office API", backend.Name);
        Assert.Equal("whisper-1", backend.ModelId);
        Assert.Equal(AsrBackendStatus.Ready, backend.Status);
        Assert.Equal("remote result", result.Text);
        Assert.Equal(0, local.TranscribeCalls);
        Assert.Equal(1, remote.TranscribeCalls);
    }

    private static AsrRequest CreateRequest() => new(
        new InMemoryAudioInput([1, 2, 3], "wav", 16000, 1),
        null,
        new Dictionary<string, string>(),
        null);

    private sealed class RecordingBackend(string name, string modelId, string result) : IAsrBackend
    {
        public string Name => name;
        public string ModelId => modelId;
        public AsrBackendStatus Status => AsrBackendStatus.Ready;
        public int TranscribeCalls { get; private set; }

        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
        {
            TranscribeCalls++;
            return Task.FromResult(new AsrResult(result, null, null, null));
        }
    }
}
