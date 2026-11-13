using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLib.Services;

public enum MoskomConnectionState { Disconnected, Connecting, Connected, Disconnecting, Failed }
public interface IMoskomEngine
{
    Task StartAndVerifyAsync(CancellationToken token);
    Task StopAsync();
}
public interface IMoskomProxyLease
{
    Task AcquireAsync(CancellationToken token);
    Task RestoreAsync();
}

// This small coordinator is independent of UI and Windows, allowing failure/cancellation tests.
public sealed class MoskomConnection(IMoskomEngine engine, IMoskomProxyLease proxy)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _connecting;
    public MoskomConnectionState State { get; private set; }
    public long Revision { get; private set; }
    public event Action<MoskomConnectionState>? Changed;
    public bool IsConnected => State == MoskomConnectionState.Connected;
    private void SetState(MoskomConnectionState state) { State = state; Revision++; Changed?.Invoke(state); }

    public void Cancel() => _connecting?.Cancel();
    public async Task ConnectAsync()
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            if (IsConnected) return;
            _connecting = new CancellationTokenSource();
            SetState(MoskomConnectionState.Connecting);
            try
            {
                await engine.StartAndVerifyAsync(_connecting.Token);
                _connecting.Token.ThrowIfCancellationRequested();
                // Do not change Windows proxy until the core and its outbound were verified.
                await proxy.AcquireAsync(_connecting.Token);
                _connecting.Token.ThrowIfCancellationRequested();
                SetState(MoskomConnectionState.Connected);
            }
            catch (OperationCanceledException)
            {
                try { await CleanupAsync(); SetState(MoskomConnectionState.Disconnected); }
                catch { SetState(MoskomConnectionState.Failed); throw; }
            }
            catch
            {
                try { await CleanupAsync(); } finally { SetState(MoskomConnectionState.Failed); } throw;
            }
            finally { _connecting.Dispose(); _connecting = null; }
        }
        finally { _gate.Release(); }
    }
    public async Task DisconnectAsync()
    {
        Cancel(); await _gate.WaitAsync();
        try { SetState(MoskomConnectionState.Disconnecting); await CleanupAsync(); SetState(MoskomConnectionState.Disconnected); }
        catch { SetState(MoskomConnectionState.Failed); throw; }
        finally { _gate.Release(); }
    }
    // A health probe from the old core must not disconnect a newly reconnected subscription.
    public async Task DisconnectIfCurrentAsync(long expectedRevision)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsConnected || Revision != expectedRevision) return;
            SetState(MoskomConnectionState.Disconnecting); await CleanupAsync(); SetState(MoskomConnectionState.Disconnected);
        }
        catch { SetState(MoskomConnectionState.Failed); throw; }
        finally { _gate.Release(); }
    }
    private async Task CleanupAsync()
    {
        try { await proxy.RestoreAsync(); }
        finally { await engine.StopAsync(); }
    }
}
