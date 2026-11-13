using ServiceLib.Services;
using Xunit;

public sealed class ConnectionTests
{
    private sealed class Engine : IMoskomEngine
    {
        public int Starts, Stops;
        public bool Fail, Wait;
        public readonly TaskCompletionSource<bool> Began = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task StartAndVerifyAsync(CancellationToken ct) { Starts++; Began.TrySetResult(true); if (Wait) await Task.Delay(Timeout.Infinite, ct); if (Fail) throw new InvalidOperationException("test"); }
        public Task StopAsync() { Stops++; return Task.CompletedTask; }
    }
    private sealed class Proxy : IMoskomProxyLease
    {
        public int Acquires, Restores;
        public bool Fail, FailRestore;
        public Task AcquireAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Acquires++; if (Fail) throw new InvalidOperationException("test"); return Task.CompletedTask; }
        public Task RestoreAsync() { Restores++; if (FailRestore) throw new IOException("test"); return Task.CompletedTask; }
    }
    [Fact] public async Task ProxyChangesOnlyAfterVerifiedCoreAndRestoresOnOff()
    {
        var e = new Engine(); var p = new Proxy(); var c = new MoskomConnection(e, p);
        Assert.Equal(MoskomConnectionState.Disconnected, c.State); await c.ConnectAsync();
        Assert.True(c.IsConnected); Assert.Equal(1, e.Starts); Assert.Equal(1, p.Acquires);
        await c.DisconnectAsync(); Assert.False(c.IsConnected); Assert.Equal(1, e.Stops); Assert.Equal(1, p.Restores);
    }
    [Fact] public async Task CoreFailureNeverEnablesProxy()
    {
        var e = new Engine { Fail = true }; var p = new Proxy(); var c = new MoskomConnection(e,p);
        await Assert.ThrowsAsync<InvalidOperationException>(c.ConnectAsync); Assert.Equal(0,p.Acquires); Assert.Equal(1,p.Restores); Assert.Equal(1,e.Stops); Assert.Equal(MoskomConnectionState.Failed,c.State);
    }
    [Fact] public async Task ProxyFailureRollsBackAndStopsCore()
    {
        var e = new Engine(); var p = new Proxy { Fail = true }; var c = new MoskomConnection(e,p);
        await Assert.ThrowsAsync<InvalidOperationException>(c.ConnectAsync); Assert.Equal(1,p.Restores); Assert.Equal(1,e.Stops); Assert.False(c.IsConnected);
    }
    [Fact] public async Task CancelDoesNotLeaveConnectingOrEnableProxy()
    {
        var e = new Engine { Wait = true }; var p = new Proxy(); var c = new MoskomConnection(e,p);
        var start = c.ConnectAsync(); await e.Began.Task; c.Cancel(); await start;
        Assert.Equal(MoskomConnectionState.Disconnected,c.State); Assert.Equal(0,p.Acquires); Assert.Equal(1,e.Stops);
    }
    [Fact] public async Task RepeatedClickDoesNotStartTwoCores()
    {
        var e = new Engine { Wait = true }; var p = new Proxy(); var c = new MoskomConnection(e,p);
        var start = c.ConnectAsync(); await e.Began.Task; await c.ConnectAsync(); c.Cancel(); await start; Assert.Equal(1,e.Starts);
    }
    [Fact] public async Task RepeatedConnectOnActiveConnectionIsIdempotent()
    {
        var e = new Engine(); var p = new Proxy(); var c = new MoskomConnection(e,p);
        await c.ConnectAsync(); await c.ConnectAsync(); Assert.Equal(1,e.Starts); Assert.Equal(1,p.Acquires);
    }
    [Fact] public async Task RestoreFailureStillStopsCoreAndIsNotShownAsConnected()
    {
        var e = new Engine(); var p = new Proxy(); var c = new MoskomConnection(e,p);
        await c.ConnectAsync(); p.FailRestore = true; await Assert.ThrowsAsync<IOException>(c.DisconnectAsync);
        Assert.Equal(1,e.Stops); Assert.Equal(MoskomConnectionState.Failed,c.State);
    }
}
