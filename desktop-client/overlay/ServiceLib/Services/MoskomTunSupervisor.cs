using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
namespace ServiceLib.Services;

// Only the bundled core is executed with system consent. Configuration and keys
// cross a local authenticated channel, never a command line or AppleScript string.
public sealed class MoskomTunSupervisor : IAsyncDisposable
{
 public static Func<int,uint,int,bool>? VerifyUnixPeer {get;set;}
 public static Func<uint>? UnixUid {get;set;}
 private Stream? _stream;private IDisposable? _listener;private Process? _launcher;
 private string? _socketDirectory;private int _stopped;public bool HasExited{get;private set;}
 private static bool Admin => OperatingSystem.IsWindows()?new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator):UnixUid?.Invoke()==0;
 private static string OwnExecutable=>Environment.ProcessPath??throw new InvalidOperationException("Не найден файл приложения.");
 private static string Started=>Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
 private static async Task Write(Stream stream,byte[] bytes,CancellationToken ct){var header=new byte[4];BinaryPrimitives.WriteInt32BigEndian(header,bytes.Length);await stream.WriteAsync(header,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);}
 private static async Task<byte[]> Read(Stream stream,CancellationToken ct){var header=new byte[4];await stream.ReadExactlyAsync(header,ct);var length=BinaryPrimitives.ReadInt32BigEndian(header);if(length<1||length>2*1024*1024)throw new InvalidDataException("Некорректный ответ службы TUN.");var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,ct);return bytes;}
 private static string QuoteShell(string text)=>"'"+text.Replace("'","'\\''")+"'";
 private static string QuoteApple(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
 public static async Task<MoskomTunSupervisor> StartAsync(byte[] config,CancellationToken ct)
 {
  var owner=new MoskomTunSupervisor();
  try{
   var nonce=Guid.NewGuid().ToString("N");string endpoint;Task<Stream> accept;
   if(OperatingSystem.IsWindows()){
    endpoint="MoskomVPN-Tun-"+nonce;var pipe=SecurePipe(endpoint);owner._listener=pipe;
    accept=Task.Run(async()=>{await pipe.WaitForConnectionAsync(ct);if(Encoding.UTF8.GetString(await Read(pipe,ct))!="hello:"+nonce)throw new IOException("Не подтверждён канал TUN.");var authenticated=false;pipe.RunAsClient(()=>authenticated=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));if(!authenticated)throw new InvalidOperationException("Служба TUN не получила права администратора.");return (Stream)pipe;},ct);
   }else{
    owner._socketDirectory=Path.Combine(Path.GetTempPath(),"mkr-"+nonce[..12]);Directory.CreateDirectory(owner._socketDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);endpoint=Path.Combine(owner._socketDirectory,"tun.sock");
    var listener=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);listener.Bind(new UnixDomainSocketEndPoint(endpoint));File.SetUnixFileMode(endpoint,UnixFileMode.UserRead|UnixFileMode.UserWrite);listener.Listen(1);owner._listener=listener;
    accept=Task.Run(async()=>{var socket=await listener.AcceptAsync(ct);if(VerifyUnixPeer?.Invoke((int)socket.Handle,0,0)!=true){socket.Dispose();throw new InvalidOperationException("Не подтверждены права службы TUN.");}var channel=new NetworkStream(socket,true);if(Encoding.UTF8.GetString(await Read(channel,ct))!="hello:"+nonce){channel.Dispose();throw new IOException("Не подтверждён канал TUN.");}return (Stream)channel;},ct);
   }
   var args=new[]{"--moskom-tun-helper",Environment.ProcessId.ToString(),Started,endpoint,nonce,OperatingSystem.IsWindows()?"0":(UnixUid?.Invoke()??uint.MaxValue).ToString()};
   ProcessStartInfo start;
   if(Admin){start=new(OwnExecutable){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in args)start.ArgumentList.Add(arg);}
   else if(OperatingSystem.IsWindows()) {start=new(OwnExecutable){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};foreach(var arg in args)start.ArgumentList.Add(arg);}
   else {
    start=new("/usr/bin/osascript"){UseShellExecute=false,CreateNoWindow=true};var command="exec "+QuoteShell(OwnExecutable)+" "+string.Join(' ',args.Select(QuoteShell));start.ArgumentList.Add("-e");start.ArgumentList.Add("do shell script "+QuoteApple(command)+" with administrator privileges with prompt \"МосКомпРемонт VPN: разрешите избирательную маршрутизацию TUN\"");
   }
   var launch=Task.Run(()=>Process.Start(start));var deadline=DateTime.UtcNow.AddMinutes(2);
   while(!accept.IsCompleted){ct.ThrowIfCancellationRequested();if(launch.IsCompleted){owner._launcher=await launch;if(owner._launcher?.HasExited==true)throw new InvalidOperationException("Изменение маршрутизации не разрешено.");}if(DateTime.UtcNow>deadline)throw new TimeoutException("Не получено разрешение TUN.");await Task.Delay(100,ct);}
   owner._stream=await accept;using var startTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);startTimeout.CancelAfter(TimeSpan.FromSeconds(20));await Write(owner._stream,config,startTimeout.Token);
   var reply=Encoding.UTF8.GetString(await Read(owner._stream,startTimeout.Token));if(reply!="started:"+nonce)throw new InvalidOperationException("Не удалось запустить ядро TUN.");
   _=owner.ObserveAsync();return owner;
  }catch{await owner.DisposeAsync();throw;}
 }
 private async Task ObserveAsync(){try{if(_stream!=null){var buffer=new byte[1];_ = await _stream.ReadAsync(buffer);}}catch{}finally{HasExited=true;}}
 public async ValueTask DisposeAsync(){if(Interlocked.Exchange(ref _stopped,1)!=0)return;HasExited=true;_stream?.Dispose();_listener?.Dispose();if(_launcher!=null){try{await _launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));}catch{}finally{_launcher.Dispose();}}if(_socketDirectory!=null){try{Directory.Delete(_socketDirectory,true);}catch{}}}
 public static async Task<int> RunHelperAsync(string[] args)
 {
  if(args.Length!=6||!Admin||!int.TryParse(args[1],out var pid)||!long.TryParse(args[2],out var ticks)||args[4].Length!=32||!uint.TryParse(args[5],out var uid))return 2;
  using var parent=Process.GetProcessById(pid);if(parent.HasExited||parent.StartTime.ToUniversalTime().Ticks!=ticks)return 2;
  Stream channel;
  if(OperatingSystem.IsWindows()){
   var pipe=new NamedPipeClientStream(".",args[3],PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Impersonation);await pipe.ConnectAsync(10000);if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var server)||server!=pid){pipe.Dispose();return 2;}channel=pipe;
  }else{
   var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await socket.ConnectAsync(new UnixDomainSocketEndPoint(args[3]),timeout.Token);if(VerifyUnixPeer?.Invoke((int)socket.Handle,uid,pid)!=true){socket.Dispose();return 2;}channel=new NetworkStream(socket,true);
  }
  await using(channel){
   using var setup=new CancellationTokenSource(TimeSpan.FromSeconds(20));await Write(channel,Encoding.UTF8.GetBytes("hello:"+args[4]),setup.Token);var bytes=await Read(channel,setup.Token);var json=JsonNode.Parse(bytes) as JsonObject;
   if(json?["inbounds"] is not JsonArray inbounds||!inbounds.OfType<JsonObject>().Any(i=>i["type"]?.GetValue<string>()=="tun")||inbounds.OfType<JsonObject>().Where(i=>i["type"]?.GetValue<string>()!="tun").Any(i=>i["listen"]?.GetValue<string>()!="127.0.0.1"))return 2;
   var core=Utils.GetBinPath(OperatingSystem.IsWindows()?"sing-box.exe":"sing-box","sing_box");if(!File.Exists(core))return 2;
   var directory=Directory.CreateTempSubdirectory("moskom-tun-").FullName;Process? process=null;WindowsJobService? job=null;
   try{
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    var file=Path.Combine(directory,"config.json");var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;await using(var output=new FileStream(file,options)){await output.WriteAsync(bytes,setup.Token);}
    var start=new ProcessStartInfo(core){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"run","-c",file,"--disable-color"})start.ArgumentList.Add(arg);
    process=Process.Start(start)??throw new IOException("Не запущено ядро.");if(OperatingSystem.IsWindows()){job=new();if(!job.AddProcess(process.Id))throw new IOException("Не подготовлено завершение ядра.");}
#if MOSKOM_TUN_TEST
    _=process.StandardOutput.ReadToEndAsync().ContinueWith(t=>Console.Error.WriteLine(t.Result));_=process.StandardError.ReadToEndAsync().ContinueWith(t=>Console.Error.WriteLine(t.Result));
#else
    _=process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);_=process.StandardError.BaseStream.CopyToAsync(Stream.Null);
#endif
    await Write(channel,Encoding.UTF8.GetBytes("started:"+args[4]),setup.Token);var buffer=new byte[1];var eof=channel.ReadAsync(buffer).AsTask();
    await Task.WhenAny(eof,process.WaitForExitAsync(),parent.WaitForExitAsync());
   }finally{
    if(process!=null){try{if(!process.HasExited){if(OperatingSystem.IsMacOS())kill(process.Id,15);else process.Kill(true);try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch{if(!process.HasExited)process.Kill(true);}}}finally{process.Dispose();}}
    job?.Dispose();try{Directory.Delete(directory,true);}catch{}
   }
  }
  return 0;
 }
 [DllImport("libc",SetLastError=true)]private static extern int kill(int pid,int signal);
 [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out int pid);
 [StructLayout(LayoutKind.Sequential)]private struct SecurityAttributes {public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor,int revision,out IntPtr pointer,out uint size);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafePipeHandle CreateNamedPipe(string name,uint openMode,uint pipeMode,uint maxInstances,uint outSize,uint inSize,uint timeout,ref SecurityAttributes security);
 [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr value);
 private static NamedPipeServerStream SecurePipe(string name){
  var sid=WindowsIdentity.GetCurrent().User?.Value??throw new InvalidOperationException("Не определён пользователь Windows.");if(!ConvertStringSecurityDescriptorToSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{sid})",1,out var descriptor,out _))throw new IOException("Не защищён канал TUN.");
  try{var security=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=descriptor};var handle=CreateNamedPipe(@"\\.\pipe\"+name,0x40080003,8,1,4096,4096,10000,ref security);if(handle.IsInvalid)throw new IOException("Не создан канал TUN.");return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle);}finally{LocalFree(descriptor);}
 }
}
