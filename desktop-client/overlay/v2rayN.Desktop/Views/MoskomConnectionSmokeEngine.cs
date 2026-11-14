using ServiceLib.Services;
using System.Net;
using System.Net.Sockets;

namespace v2rayN.Desktop.Views;

// A local SOCKS greeting server for connected-session regression tests. It does
// not forward traffic, start a VPN core, or change the machine's proxy settings.
internal sealed class MoskomConnectionSmokeEngine : IMoskomEngine
{
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _worker;
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public Task StartAndVerifyAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!MoskomLauncherRuntime.SmokeTest) throw new InvalidOperationException("Test profiles only.");
        _stop = new();
        _listener = new(IPAddress.Loopback, AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
        _listener.Start(); Starts++;
        var listener = _listener; var ct = _stop.Token;
        _worker = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(ct);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(2000);
                    var stream = client.GetStream(); var greeting = new byte[3];
                    await stream.ReadExactlyAsync(greeting, timeout.Token);
                    await stream.WriteAsync(new byte[] { 5, 0 }, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (SocketException) when (ct.IsCancellationRequested) { }
        });
        return Task.CompletedTask;
    }
    public async Task StopAsync()
    {
        Stops++;
        if (_stop == null) return;
        _stop.Cancel(); _listener!.Stop();
        if (_worker != null) await _worker;
        _stop.Dispose(); _stop = null; _listener = null; _worker = null;
    }
}
