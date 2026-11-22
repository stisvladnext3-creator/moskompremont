using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.NetworkInformation;
using ServiceLib.Services;
internal static partial class Program {
 static int _requests;
 static readonly string Marker="moskom-selective-vpn-fixture";
 static readonly bool Mac=OperatingSystem.IsMacOS();
 static async Task<int> Main(string[] args) {
  if(Mac){MoskomTunSupervisor.UnixUid=MoskomMacNative.mkr_uid;MoskomTunSupervisor.VerifyUnixPeer=(fd,uid,pid)=>MoskomMacNative.mkr_check_peer(fd,uid,pid)==0;}
  if(args.FirstOrDefault()=="--moskom-tun-helper")return await MoskomTunSupervisor.RunHelperAsync(args);
  if(args.FirstOrDefault()=="--fixture-parent"){
   await using var lease=await MoskomTunSupervisor.StartAsync(await File.ReadAllBytesAsync(args[1]),CancellationToken.None);
   await Wait(Tunnel,"Crash parent TUN did not start");await File.WriteAllTextAsync(args[2],"ready");await Task.Delay(Timeout.Infinite);return 0;
  }
  var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Environment.SetEnvironmentVariable("MOSKOM_TUN_TEST_LOG",Path.Combine(output,"core.log"));
  try {await Run(output);Console.WriteLine("PASS: real TUN selected process/service isolation, direct fallback and parent-crash cleanup");return 0;}
  catch(Exception error){Console.Error.WriteLine(error);var logs=Path.Combine(output,"core.log");if(File.Exists(logs))Console.Error.WriteLine(await File.ReadAllTextAsync(logs));return 1;}
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static async Task Wait(Func<bool> condition,string message){for(var i=0;i<100;i++){if(condition())return;await Task.Delay(100);}throw new Exception(message);}
 static bool Tunnel()=>NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()=="172.19.0.1"));
 static byte[] Config(int socks,int port,string selected,bool service){
  var core=new JsonObject{["outbounds"]=new JsonArray(new JsonObject{["type"]="socks",["tag"]="proxy",["server"]="127.0.0.1",["server_port"]=socks,["version"]="5"},new JsonObject{["type"]="direct",["tag"]="direct"})};
  core["dns"]=JsonNode.Parse("""{"servers":[{"tag":"direct-dns-1","type":"udp","server":"77.88.8.8"},{"tag":"local-local","type":"udp","server":"77.88.8.8"},{"tag":"remote-dns-1","type":"tcp","server":"192.0.2.53","server_port":53,"detour":"proxy"}]}""");
  var prefs=new MoskomSelectiveSettings{Mode=service?"services":"apps",Apps=[new(selected,"Selected curl",selected)],Services=["telegram","whatsapp","chatgpt","youtube","speedtest"],CustomDomains=["domain:custom.fixture","domain:otherside.xyz"]};
  var catalog=MoskomServiceCatalog.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"service-catalog.json")));
  var config=MoskomSelectivePolicy.Configure(core,prefs,catalog,port,Mac?"utun177":"MoskomVPN-test",Mac,_networkDns);config["log"]!["level"]="debug";return JsonSerializer.SerializeToUtf8Bytes(config);
 }
 static async Task Run(string output){
  Check(!Tunnel(),"Fixture must start without the test TUN");await PrepareDns();var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var socks=((IPEndPoint)listener.LocalEndpoint).Port;
  using var stop=new CancellationTokenSource();var accept=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){var socket=await listener.AcceptTcpClientAsync(stop.Token);_=Serve(socket,stop.Token);}}catch(OperationCanceledException){}});
  var directory=Mac?Directory.CreateDirectory(Path.Combine("/private/tmp","moskom-process-fixture-"+Guid.NewGuid().ToString("N"))).FullName:MoskomNetCompat.CreateTempDirectory("moskom-process-fixture-");var selected=Path.Combine(directory,Mac?"selected-curl":"selected-curl.exe");var direct=Path.Combine(directory,Mac?"direct-curl":"direct-curl.exe");
  try{
   var source=Mac?"/usr/bin/curl":Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"curl.exe");File.Copy(source,selected);File.Copy(source,direct);
   if(Mac){
#if NET7_0_OR_GREATER
    File.SetUnixFileMode(selected,UnixFileMode.UserRead|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);File.SetUnixFileMode(direct,UnixFileMode.UserRead|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);
#endif
   }
   var port=18838;var apps=Config(socks,port,selected,false);await File.WriteAllBytesAsync(Path.Combine(output,"selected-apps.json"),apps);await CoreCheck(apps,directory);
   await using(var tunnel=await MoskomTunSupervisor.StartAsync(apps,CancellationToken.None)){
   await Wait(Tunnel,"Real test TUN did not open");await Task.Delay(5000);var routed=await Curl(selected,"unselected.invalid");Check(routed.Contains(Marker),"Selected executable did not traverse the proxy");
    var count=_requests;var bypass=await Curl(direct,"unselected.invalid");Check(!bypass.Contains(Marker)&&_requests==count,"Unselected executable was proxied");
    await CheckPublicDirect(direct,false);
   }
   await Wait(()=>!Tunnel(),"Normal disconnect left the TUN/routes active");
   var services=Config(socks,port,selected,true);await File.WriteAllBytesAsync(Path.Combine(output,"selected-services.json"),services);await CoreCheck(services,directory);
   await using(var tunnel=await MoskomTunSupervisor.StartAsync(services,CancellationToken.None)){
   await Wait(Tunnel,"Service test TUN did not open");await Task.Delay(5000);var routed=await Curl(direct,"youtube.com");Check(routed.Contains(Marker),"Selected service did not traverse proxy");
    var count=_requests;var bypass=await Curl(selected,"unselected.invalid");Check(!bypass.Contains(Marker)&&_requests==count,"Unselected service was proxied");
    foreach(var ip in new[]{"91.105.192.8","57.144.0.8"}){var media=await Curl(direct,"media.fixture.invalid",ip);Check(media.Contains(Marker)&&media.Length>128*1024,"Messenger media transfer did not traverse VPN: "+ip);}
    Check((await Curl(direct,"custom.fixture")).Contains(Marker),"Personal domain did not traverse VPN");
    Check((await Curl(direct,"otherside.xyz")).Contains(Marker),"Reported personal domain did not traverse VPN");
    var customDns=await QueryDns("custom.fixture");Check(customDns.Equals(IPAddress.Parse("198.51.100.77")),"Personal domain DNS did not traverse VPN");
    var selectedDns=await QueryDns("youtube.com");Check(selectedDns.Equals(IPAddress.Parse("198.51.100.77")),"Selected service DNS did not traverse the VPN resolver");
    await CheckPublicDirect(selected,true);
    await CheckRussianDirect(selected);
   }
   await Wait(()=>!Tunnel(),"Service disconnect left TUN/routes active");
   var configFile=Path.Combine(directory,"crash-config.json");await File.WriteAllBytesAsync(configFile,apps);var ready=Path.Combine(directory,"ready");
   var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};foreach(var arg in new[]{"--fixture-parent",configFile,ready})start.ArgumentList.Add(arg);
   using(var parent=Process.Start(start)!){await Wait(()=>File.Exists(ready),"Crash parent did not start");await Wait(Tunnel,"Crash fixture has no TUN");parent.Kill();await parent.WaitForExitAsync();}
   await Wait(()=>!Tunnel(),"Killed UI parent left privileged core/TUN active");
   await File.WriteAllTextAsync(Path.Combine(output,"tun-validation.json"),JsonSerializer.Serialize(new{selectedProcess=true,unselectedProcessDirect=true,selectedService=true,unselectedServiceDirect=true,directHttpsWithDns=true,splitDns=true,personalDomain=true,messengerMedia128KiB=true,normalStop=true,parentCrash=true,requests=_requests}));
  }finally{stop.Cancel();listener.Stop();try{await accept;}catch{}Directory.Delete(directory,true);}
 }
 static async Task CoreCheck(byte[] config,string folder){var file=Path.Combine(folder,"check.json");await File.WriteAllBytesAsync(file,config);var start=new ProcessStartInfo(Utils.GetBinPath(Mac?"sing-box":"sing-box.exe","sing_box")){UseShellExecute=false};start.ArgumentList.Add("check");start.ArgumentList.Add("-c");start.ArgumentList.Add(file);using var p=Process.Start(start)!;await p.WaitForExitAsync();Check(p.ExitCode==0,"Native core rejected selective config");}
 static async Task<string> Curl(string file,string host,string destination="198.51.100.77"){var start=new ProcessStartInfo(Mac?"/usr/bin/sudo":file){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};if(Mac){var user=Environment.GetEnvironmentVariable("SUDO_USER");Check(!string.IsNullOrEmpty(user)&&user!="root","Mac traffic fixture needs the ordinary runner user");foreach(var arg in new[]{"-n","-u",user!,"--",file})start.ArgumentList.Add(arg);}foreach(var arg in new[]{"--silent","--show-error","--noproxy","*","--proxy","","--connect-timeout","3","--max-time","5","--resolve",host+":18839:"+destination,"http://"+host+":18839/fixture"})start.ArgumentList.Add(arg);using var process=Process.Start(start)!;var text=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));var stderr=await error;var stdout=await text;Console.WriteLine("Curl fixture: "+Path.GetFileName(file)+" "+host+" exit="+process.ExitCode+" stderr="+stderr+" bytes="+stdout.Length);return stdout;}
 static async Task Serve(TcpClient client,CancellationToken ct){using(client){try{
  var stream=client.GetStream();var header=new byte[2];await stream.ReadExactlyAsync(header,ct);var methods=new byte[header[1]];await stream.ReadExactlyAsync(methods,ct);await stream.WriteAsync(new byte[]{5,0},ct);
  var request=new byte[4];await stream.ReadExactlyAsync(request,ct);var length=request[3]switch{1=>4,4=>16,3=>stream.ReadByte(),_=>throw new IOException()};var destination=new byte[length+2];await stream.ReadExactlyAsync(destination,ct);
  var port=(destination[^2]<<8)|destination[^1];await stream.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,0,0},ct);
  if(port==53){await AnswerProxyDns(stream,ct);return;}
  if(port==443){Interlocked.Increment(ref _proxiedHttps);throw new IOException("Unselected HTTPS incorrectly entered the VPN");}
  var data=new byte[8192];var count=await stream.ReadAsync(data,ct);if(count==0)return;Interlocked.Increment(ref _requests);var body=Marker+new string('x',128*1024);var response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: "+body.Length+"\r\n\r\n"+body);await stream.WriteAsync(response,ct);
 }catch(Exception e)when(e is IOException or OperationCanceledException or SocketException){}}}
}
