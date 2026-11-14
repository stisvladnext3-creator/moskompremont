using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
namespace ServiceLib.Services;
public sealed class MoskomMacProxyLease : IMoskomProxyLease
{
    private static string LeasePath=>Path.Combine(Utils.StartupPath(),"mac-proxy-lease.json");
    private string Session=Guid.NewGuid().ToString("N");
    private sealed record Lease(int Pid,string Started,string Session);
    public async Task AcquireAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (MoskomLauncherRuntime.SelectiveMode) return;
        Session=Guid.NewGuid().ToString("N");
        if(File.Exists(LeasePath))await RecoverAfterCrashAsync();
        if(File.Exists(LeasePath))throw new InvalidOperationException("Сначала разрешите восстановление прежних настроек прокси macOS.");
        Directory.CreateDirectory(Utils.StartupPath());File.SetUnixFileMode(Utils.StartupPath(),UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        MoskomMacNative.Check(MoskomMacNative.mkr_authorize(1));
        var started=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MoskomMacNative.Check(MoskomMacNative.mkr_proxy_prepare(LeasePath,AppManager.Instance.GetLocalPort(EInboundProtocol.socks),Environment.ProcessId,started,Session));
        try {
            var watch=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
            watch.ArgumentList.Add("--moskom-watch-mac-proxy");watch.ArgumentList.Add(Environment.ProcessId.ToString());watch.ArgumentList.Add(Session);
            using var child=Process.Start(watch)??throw new InvalidOperationException("Не запущено восстановление прокси при сбое приложения.");
            var credential=new byte[32];
            try{MoskomMacNative.Check(MoskomMacNative.mkr_auth_export(credential));await child.StandardInput.WriteLineAsync(Convert.ToBase64String(credential));await child.StandardInput.FlushAsync();child.StandardInput.Close();}
            finally{CryptographicOperations.ZeroMemory(credential);}
            var ready=await child.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(15),ct);
            if(ready!="ready")throw new InvalidOperationException("Не удалось подготовить восстановление прокси.");
            ct.ThrowIfCancellationRequested();MoskomMacNative.Check(MoskomMacNative.mkr_proxy_apply(LeasePath,Session));
        } catch {await RestoreAsync();throw;}
    }
    public Task RestoreAsync()
    {
        if(File.Exists(LeasePath)) {var lease=Read();if(lease?.Session==Session)MoskomMacNative.Check(MoskomMacNative.mkr_proxy_restore(LeasePath,Session));}
        return Task.CompletedTask;
    }
    private static Lease? Read(){try{return JsonSerializer.Deserialize<Lease>(File.ReadAllText(LeasePath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});}catch{return null;}}
    private static bool Alive(Lease lease){try{using var process=Process.GetProcessById(lease.Pid);return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)==lease.Started;}catch(ArgumentException){return false;}}
    public static Task RecoverAfterCrashAsync()
    {
        if(!File.Exists(LeasePath))return Task.CompletedTask;
        var lease=Read();if(lease==null)throw new InvalidOperationException("Повреждена резервная копия прокси macOS. Обратитесь в поддержку.");
        if(Alive(lease))throw new InvalidOperationException("Другое окно приложения ещё использует прокси.");
        MoskomMacNative.Check(MoskomMacNative.mkr_authorize(1));MoskomMacNative.Check(MoskomMacNative.mkr_proxy_restore(LeasePath,lease.Session));return Task.CompletedTask;
    }
    public static async Task WatchAsync(int pid,string session)
    {
        var encoded=await Console.In.ReadLineAsync();var credential=encoded==null?[]:Convert.FromBase64String(encoded);
        try {if(credential.Length!=32||MoskomMacNative.mkr_auth_import(credential)!=0)return;}
        finally{CryptographicOperations.ZeroMemory(credential);}
        var lease=Read();if(lease==null||lease.Session!=session||lease.Pid!=pid)return;
        Console.WriteLine("ready");Console.Out.Flush();var refresh=DateTime.UtcNow;
        while(File.Exists(LeasePath)&&Read()?.Session==session&&Alive(lease)) {
            await Task.Delay(1000);
            if(DateTime.UtcNow-refresh>TimeSpan.FromMinutes(1)){_ = MoskomMacNative.mkr_authorize(0);refresh=DateTime.UtcNow;}
        }
        try{if(File.Exists(LeasePath)&&Read()?.Session==session)MoskomMacNative.Check(MoskomMacNative.mkr_proxy_restore(LeasePath,session));}
        finally{MoskomMacNative.mkr_auth_release();}
    }
}
