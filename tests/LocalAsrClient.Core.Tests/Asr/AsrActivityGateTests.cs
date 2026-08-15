using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class AsrActivityGateTests
{
    [Fact]
    public async Task TryEnterAsync_WhileLeaseIsHeld_ReturnsNullUntilReleased()
    {
        var gate = new AsrActivityGate();

        await using var first = Assert.IsType<AsrActivityLease>(
            await gate.TryEnterAsync(CancellationToken.None));

        Assert.Null(await gate.TryEnterAsync(CancellationToken.None));

        await first.DisposeAsync();
        await using var second = Assert.IsType<AsrActivityLease>(
            await gate.TryEnterAsync(CancellationToken.None));
    }
}
