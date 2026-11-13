using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using ServiceLib.Handler.SysProxy;

namespace ServiceLib.Services;

[SupportedOSPlatform("windows")]
public sealed class MoskomProxyLease : IMoskomProxyLease
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static readonly string[] Keys = ["ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL"];
    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    private static bool _watchStarted;
    private static string LeasePath => Path.Combine(Utils.StartupPath(), "proxy-lease.json");
    public sealed record Entry(RegistryValueKind Kind, string Value);
    public sealed class Snapshot
    {
        public int Pid { get; set; }
        public long Start { get; set; }
        public string Session { get; set; } = "";
        public string Ours { get; set; } = "";
        public Dictionary<string, Entry> Values { get; set; } = [];
        public Dictionary<string, Entry> Connections { get; set; } = [];
    }
    private static Entry? Read(RegistryKey key, string name)
    {
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value == null) return null;
        var kind = key.GetValueKind(name);
        return new(kind, kind == RegistryValueKind.Binary ? Convert.ToBase64String((byte[])value) : JsonSerializer.Serialize(value));
    }
    private static void Write(RegistryKey key, string name, Entry entry)
    {
        object value = entry.Kind switch
        {
            RegistryValueKind.Binary => Convert.FromBase64String(entry.Value),
            RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(entry.Value),
            RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(entry.Value),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(entry.Value)!,
            _ => JsonSerializer.Deserialize<string>(entry.Value)!
        };
        key.SetValue(name, value, entry.Kind);
    }
    public Task AcquireAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        using var connections = key.CreateSubKey("Connections", true);
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var ours = $"127.0.0.1:{port}";
        var snapshot = new Snapshot { Pid = Environment.ProcessId, Start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, Session = SessionId, Ours = ours };
        foreach (var name in Keys) { var value = Read(key, name); if (value != null) snapshot.Values[name] = value; }
        foreach (var name in connections.GetValueNames()) { var value = Read(connections, name); if (value != null) snapshot.Connections[name] = value; }
        Directory.CreateDirectory(Utils.StartupPath());
        var temp = LeasePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(snapshot)); File.Move(temp, LeasePath, true);
        if (!_watchStarted)
        {
            var start = new ProcessStartInfo(Utils.GetExePath()) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--moskom-watch-proxy"); start.ArgumentList.Add(snapshot.Pid.ToString()); start.ArgumentList.Add(SessionId);
            _ = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить восстановление прокси.");
            _watchStarted = true;
        }
        key.SetValue("AutoConfigURL", "", RegistryValueKind.String);
        _ = ProxySettingWindows.SetProxy(ours, "<local>;localhost;127.*;[::1]", 2);
        if ((int)(key.GetValue("ProxyEnable", 0) ?? 0) != 1 || !string.Equals(key.GetValue("ProxyServer") as string, ours, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows не приняла настройки прокси.");
        return Task.CompletedTask;
    }
    public Task RestoreAsync() { Restore(SessionId); return Task.CompletedTask; }
    public static void Restore(string? expectedSession = null)
    {
        if (!File.Exists(LeasePath)) return;
        Snapshot? s;
        try { s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(LeasePath)); } catch { return; }
        if (s == null || expectedSession != null && s.Session != expectedSession) return;
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        // Respect another VPN/user change made after us; never overwrite its new proxy settings.
        var owned = (int)(key.GetValue("ProxyEnable", 0) ?? 0) == 1
            && string.Equals(key.GetValue("ProxyServer") as string, s.Ours, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(key.GetValue("AutoConfigURL") as string);
        if (owned)
        {
            foreach (var name in Keys) { if (s.Values.TryGetValue(name, out var e)) Write(key, name, e); else key.DeleteValue(name, false); }
            using var connections = key.CreateSubKey("Connections", true);
            foreach (var pair in s.Connections) Write(connections, pair.Key, pair.Value);
            key.Flush(); connections.Flush();
            _ = InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0); _ = InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }
        File.Delete(LeasePath);
    }
    public static void RecoverAfterCrash()
    {
        if (!File.Exists(LeasePath)) return;
        try
        {
            var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(LeasePath)); if (s == null) return;
            try { using var p = Process.GetProcessById(s.Pid); if (!p.HasExited && p.StartTime.ToUniversalTime().Ticks == s.Start) return; } catch (ArgumentException) { }
            Restore(s.Session);
        }
        catch { /* A broken lease is kept for support rather than blindly clearing the user's proxy. */ }
    }
    public static void Watch(int pid, string session)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(LeasePath));
            using var p = Process.GetProcessById(pid);
            if (s != null && s.Session == session && s.Pid == pid && p.StartTime.ToUniversalTime().Ticks == s.Start) p.WaitForExit();
        }
        catch (Exception e) when (e is ArgumentException or IOException or JsonException) { }
        try { Restore(session); } catch { }
    }
    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);
}
