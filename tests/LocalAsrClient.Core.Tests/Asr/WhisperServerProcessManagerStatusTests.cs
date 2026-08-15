using LocalAsrClient.Core.Asr;
using System.Net;
using System.Net.Sockets;
using System.Text;

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

    [Fact]
    public async Task UpdateOptions_WhileReady_MarksRestartRequiredWithoutInterruptingActiveEndpoint()
    {
        var executable = Path.GetTempFileName();
        var model = Path.GetTempFileName();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var responseTask = RespondOkOnceAsync(listener);
        try
        {
            var manager = new WhisperServerProcessManager(new WhisperServerOptions(
                executable, model, "127.0.0.1", port));
            await manager.EnsureStartedAsync(CancellationToken.None);

            manager.UpdateOptions(new WhisperServerOptions(
                executable, model, "127.0.0.1", port + 1));

            Assert.Equal(WhisperServerStatus.Ready, manager.Status);
            Assert.Equal(port, manager.BaseUri.Port);
            Assert.True(manager.IsRestartRequired);

            await manager.StopAsync(CancellationToken.None);

            Assert.Equal(port + 1, manager.BaseUri.Port);
            Assert.False(manager.IsRestartRequired);
            await responseTask;
        }
        finally
        {
            File.Delete(executable);
            File.Delete(model);
        }
    }

    private static async Task RespondOkOnceAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var buffer = new byte[1024];
        _ = await stream.ReadAsync(buffer);
        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
    }
}
